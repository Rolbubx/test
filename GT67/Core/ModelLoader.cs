using System.Text.Json;
using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using Silk.NET.Assimp;
using StbImageSharp;
using AiMaterial = Silk.NET.Assimp.Material;
using AiMesh = Silk.NET.Assimp.Mesh;
using AiScene = Silk.NET.Assimp.Scene;
using AiTexture = Silk.NET.Assimp.Texture;

namespace GT67.Core;

/// <summary>
/// Assimp import for FBX, glTF/GLB, OBJ and friends: triangulates, bakes node transforms, generates normals and
/// tangents when missing, and loads textures (external files, files stored under another machine's paths, and
/// textures embedded in the model file).
/// </summary>
internal static unsafe class ModelLoader
{
    private sealed class Context
    {
        public required Assimp Assimp { get; init; }
        public required AiScene* Scene { get; init; }
        public required string Directory { get; init; }
        public Dictionary<string, Texture?> Textures { get; } = new();
        public Dictionary<string, PendingTexture> Pending { get; } = new();
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A texture to decode: from a file, from bytes embedded in the model, or already raw pixels.</summary>
    private sealed class PendingTexture
    {
        public required string Name { get; init; }      // the file path, or "embedded:N"
        public required bool Srgb { get; init; }
        public string? File { get; init; }
        public byte[]? Encoded { get; init; }            // PNG/JPG bytes stored inside the model
        public byte[]? Rgba { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string? Error { get; set; }
    }

    public static Model Load(string path)
    {
        if (!System.IO.File.Exists(path)) throw new FileNotFoundException($"Model file not found: {path}", path);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var assimp = Assimp.GetApi();
        const PostProcessSteps steps =
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.CalculateTangentSpace |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.GenerateUVCoords |
            PostProcessSteps.SortByPrimitiveType |
            PostProcessSteps.ImproveCacheLocality |
            PostProcessSteps.RemoveRedundantMaterials |   // identical materials become one...
            PostProcessSteps.OptimizeMeshes |             // ...so meshes sharing a material merge: fewer draw calls
            PostProcessSteps.PreTransformVertices; // flattens the node tree into world-space meshes

        AiScene* scene = assimp.ImportFile(path, (uint)steps);
        if (scene == null || scene->MRootNode == null || (scene->MFlags & (uint)SceneFlags.Incomplete) != 0)
            throw new InvalidOperationException($"Assimp failed to import '{path}': {assimp.GetErrorStringS()}");

        try
        {
            var context = new Context
            {
                Assimp = assimp,
                Scene = scene,
                Directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "",
            };
            context.Files.Add(Path.GetFullPath(path));
            AddCompanionFiles(path, context.Files);
            long importMs = watch.ElapsedMilliseconds;

            // Textures first, all of them decoded at once on every core (the slowest part of loading a big model),
            // while the meshes are built on this thread.
            var used = new List<uint>();
            for (uint i = 0; i < scene->MNumMeshes; i++)
            {
                AiMesh* aiMesh = scene->MMeshes[i];
                if (aiMesh->MNumVertices == 0 || aiMesh->MNumFaces == 0) continue;
                used.Add(i);
                RequestTextures(context, scene->MMaterials[aiMesh->MMaterialIndex]);
            }
            var decoding = DecodeTexturesAsync(context);

            var meshes = new List<(uint index, Mesh mesh)>();
            foreach (uint i in used) meshes.Add((i, BuildMesh(scene->MMeshes[i])));

            decoding.Wait();
            long decodedMs = watch.ElapsedMilliseconds;
            UploadTextures(context);
            long uploadMs = watch.ElapsedMilliseconds - decodedMs;

            var parts = new List<ModelPart>();
            foreach (var (i, mesh) in meshes)
            {
                AiMesh* aiMesh = scene->MMeshes[i];
                var material = BuildMaterial(context, scene->MMaterials[aiMesh->MMaterialIndex]);
                string name = aiMesh->MName.AsString;
                parts.Add(new ModelPart(mesh, material, string.IsNullOrEmpty(name) ? $"mesh{i}" : name));
            }

            if (parts.Count == 0) throw new InvalidOperationException($"'{path}' contains no triangle meshes.");

            var model = new Model(Path.GetFileName(path), parts, context.Textures.Values.OfType<Texture>())
            {
                SourcePath = Path.GetFullPath(path),
            };
            model.SourceFiles = context.Files.ToList();
            int textureCount = context.Textures.Values.Count(t => t != null);
            Log.Info($"'{Path.GetFileName(path)}': {parts.Count} part(s), {parts.Sum(p => p.Mesh.Indices.Length / 3)} triangles, " +
                     $"{textureCount} texture(s) in {watch.ElapsedMilliseconds} ms (Assimp {importMs} ms" +
                     (textureCount > 0 ? $", textures decoded {decodedMs - importMs} ms, GPU upload {uploadMs} ms)." : ")."));
            return model;
        }
        finally
        {
            assimp.ReleaseImport(scene);
        }
    }

    // ---- meshes ----------------------------------------------------------------------------------

    private static Mesh BuildMesh(AiMesh* m)
    {
        var vertices = new Vertex[m->MNumVertices];
        bool hasNormals = m->MNormals != null;
        bool hasUvs = m->MTextureCoords[0] != null;
        bool hasTangents = m->MTangents != null;
        bool hasColors = m->MColors[0] != null;

        for (int v = 0; v < vertices.Length; v++)
        {
            var p = m->MVertices[v];
            vertices[v].Position = new Vector3(p.X, p.Y, p.Z);
            vertices[v].Color = Vector4.One;

            if (hasNormals)
            {
                var n = m->MNormals[v];
                vertices[v].Normal = new Vector3(n.X, n.Y, n.Z);
            }
            if (hasUvs)
            {
                var uv = m->MTextureCoords[0][v];
                vertices[v].UV = new Vector2(uv.X, uv.Y);
            }
            if (hasTangents)
            {
                var t = m->MTangents[v];
                vertices[v].Tangent = new Vector3(t.X, t.Y, t.Z);
            }
            if (hasColors)
            {
                var c = m->MColors[0][v];
                vertices[v].Color = new Vector4(c.X, c.Y, c.Z, c.W);
            }
        }

        var indices = new List<uint>((int)m->MNumFaces * 3);
        for (uint f = 0; f < m->MNumFaces; f++)
        {
            var face = m->MFaces[f];
            if (face.MNumIndices != 3) continue; // points/lines are skipped
            indices.Add(face.MIndices[0]);
            indices.Add(face.MIndices[1]);
            indices.Add(face.MIndices[2]);
        }
        var indexArray = indices.ToArray();

        if (!hasNormals) Mesh.GenerateNormals(vertices, indexArray);
        return new Mesh(vertices, indexArray, OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, generateTangents: !hasTangents);
    }

    // ---- materials -------------------------------------------------------------------------------

    private static Material BuildMaterial(Context ctx, AiMaterial* m)
    {
        var assimp = ctx.Assimp;
        var material = new Material
        {
            // glTF stores its colour texture as "base colour", FBX/OBJ as "diffuse"
            DiffuseMap = LoadTexture(ctx, m, TextureType.Diffuse, srgb: true)
                         ?? LoadTexture(ctx, m, TextureType.BaseColor, srgb: true),
            SpecularMap = LoadTexture(ctx, m, TextureType.Specular, srgb: false),
            // OBJ files often store bump maps under "Height"
            NormalMap = LoadTexture(ctx, m, TextureType.Normals, srgb: false)
                        ?? LoadTexture(ctx, m, TextureType.Height, srgb: false),
        };

        System.Numerics.Vector4 color;
        if (assimp.GetMaterialColor(m, Assimp.MaterialColorDiffuseBase, 0, 0, &color) == Return.Success ||
            assimp.GetMaterialColor(m, "$clr.base", 0, 0, &color) == Return.Success)
        {
            material.Color = new Color4(color.X, color.Y, color.Z, 1f);
            // A white-ish tint is expected when a texture supplies the colour; very dark factors come from
            // exporters that leave the factor at (0,0,0) next to a texture, which would hide it.
            if (material.DiffuseMap != null && color.X + color.Y + color.Z < 0.03f) material.Color = Color4.White;
        }

        float shininess = 0;
        uint max = 1;
        if (assimp.GetMaterialFloatArray(m, Assimp.MaterialShininessBase, 0, 0, &shininess, &max) == Return.Success
            && shininess > 1f)
            material.Shininess = Math.Min(shininess, 256f);

        float opacity = 1f;
        max = 1;
        if (assimp.GetMaterialFloatArray(m, Assimp.MaterialOpacityBase, 0, 0, &opacity, &max) == Return.Success &&
            opacity is > 0.01f and < 0.999f)
            material.Opacity = opacity;

        return material;
    }

    // The texture slots read for every material (the same list BuildMaterial uses).
    private static readonly (TextureType type, bool srgb)[] TextureSlots =
    {
        (TextureType.Diffuse, true), (TextureType.BaseColor, true), (TextureType.Specular, false),
        (TextureType.Normals, false), (TextureType.Height, false),
    };

    /// <summary>The texture of a material slot, decoded and uploaded earlier (null if the slot is empty or failed).</summary>
    private static Texture? LoadTexture(Context ctx, AiMaterial* m, TextureType type, bool srgb) =>
        Reference(ctx, m, type) is { } reference && ctx.Textures.TryGetValue(Key(reference, srgb), out var texture) ? texture : null;

    private static string Key(string reference, bool srgb) => (srgb ? "s:" : "l:") + reference;

    private static string? Reference(Context ctx, AiMaterial* m, TextureType type)
    {
        if (ctx.Assimp.GetMaterialTextureCount(m, type) == 0) return null;
        AssimpString str;
        if (ctx.Assimp.GetMaterialTexture(m, type, 0, &str, null, null, null, null, null, null) != Return.Success) return null;
        string reference = str.AsString;
        return reference.Length == 0 ? null : reference;
    }

    /// <summary>Finds where each texture of the material comes from (file or embedded); decoding happens later, in parallel.</summary>
    private static void RequestTextures(Context ctx, AiMaterial* m)
    {
        foreach (var (type, srgb) in TextureSlots)
        {
            if (Reference(ctx, m, type) is not { } reference) continue;
            string key = Key(reference, srgb);
            if (ctx.Pending.ContainsKey(key) || ctx.Textures.ContainsKey(key)) continue;

            PendingTexture? pending;
            if (reference.StartsWith('*'))
            {
                pending = int.TryParse(reference.AsSpan(1), out int index) ? PrepareEmbedded(ctx, index, srgb) : null;
            }
            else if (ResolveTextureFile(ctx.Directory, reference) is { } file)
            {
                ctx.Files.Add(file);
                pending = new PendingTexture { Name = file, Srgb = srgb, File = file };
            }
            else
            {
                // Some files (GLB, FBX) keep the image inside and refer to it by its original file name.
                int index = FindEmbeddedByName(ctx, reference);
                pending = index >= 0 ? PrepareEmbedded(ctx, index, srgb) : null;
                if (pending == null) Log.Warn($"Texture '{reference}' referenced by the model was not found near '{ctx.Directory}'.");
            }

            if (pending != null) ctx.Pending[key] = pending;
            else ctx.Textures[key] = null;
        }
    }

    /// <summary>Decodes every requested image on the thread pool (file reading and PNG/JPG decoding).</summary>
    private static Task DecodeTexturesAsync(Context ctx)
    {
        var work = ctx.Pending.Values.Where(p => p.Rgba == null).ToList();
        if (work.Count == 0) return Task.CompletedTask;

        StbImage.stbi_set_flip_vertically_on_load(1); // GL's v axis points up (set once, before the workers start)
        return Task.Run(() => Parallel.ForEach(work, pending =>
        {
            try
            {
                var bytes = pending.Encoded ?? System.IO.File.ReadAllBytes(pending.File!);
                var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
                pending.Rgba = image.Data;
                pending.Width = image.Width;
                pending.Height = image.Height;
            }
            catch (Exception e)
            {
                pending.Error = e.Message;
            }
        }));
    }

    /// <summary>Creates the GL textures (this must happen on the GL thread).</summary>
    private static void UploadTextures(Context ctx)
    {
        foreach (var (key, pending) in ctx.Pending)
        {
            if (pending.Rgba == null)
            {
                Log.Warn($"Texture '{pending.Name}' failed to load: {pending.Error ?? "unknown format"}");
                ctx.Textures[key] = null;
                continue;
            }
            var texture = Texture.FromPixels(pending.Rgba, pending.Width, pending.Height, pending.Name, pending.Srgb);
            texture.SetMaxAnisotropy(8f);   // sharp at grazing angles (floors, walls seen along their length)
            ctx.Textures[key] = texture;
        }
        ctx.Pending.Clear();
    }

    /// <summary>The embedded texture whose stored file name matches <paramref name="reference"/>, or -1.</summary>
    private static int FindEmbeddedByName(Context ctx, string reference)
    {
        string wanted = Path.GetFileName(Uri.UnescapeDataString(reference.Replace('\\', '/')));
        for (int i = 0; i < ctx.Scene->MNumTextures; i++)
        {
            string stored = ctx.Scene->MTextures[i]->MFilename.AsString;
            if (stored.Length > 0 && string.Equals(Path.GetFileName(stored.Replace('\\', '/')), wanted, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Textures can be stored relative to the model, as absolute paths from the artist's machine, or under a
    /// different folder name. Try the path as written, then the file name next to the model, then a search below
    /// the model's folder (and its parent).
    /// </summary>
    private static string? ResolveTextureFile(string directory, string reference)
    {
        if (reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;   // inline data: embedded
        // glTF writes URIs: "my%20wood.png" is the file "my wood.png".
        if (reference.Contains('%'))
        {
            string unescaped = Uri.UnescapeDataString(reference);
            if (unescaped != reference && ResolveTextureFile(directory, unescaped) is { } found) return found;
        }

        string normalized = reference.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        string direct = Path.IsPathRooted(normalized) ? normalized : Path.Combine(directory, normalized);
        if (System.IO.File.Exists(direct)) return direct;

        string fileName = Path.GetFileName(normalized);
        if (fileName.Length == 0) return null;

        string sibling = Path.Combine(directory, fileName);
        if (System.IO.File.Exists(sibling)) return sibling;

        foreach (var root in new[] { directory, Directory.GetParent(directory)?.FullName })
        {
            if (root == null || !Directory.Exists(root)) continue;
            try
            {
                var found = Directory.EnumerateFiles(root, fileName, new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MatchCasing = MatchCasing.CaseInsensitive,
                    MaxRecursionDepth = 4,
                    IgnoreInaccessible = true,
                }).FirstOrDefault();
                if (found != null) return found;
            }
            catch (IOException) { }
        }
        return null;
    }

    /// <summary>
    /// A texture stored inside the model file ("*0", "*1", ... as used by GLB and many FBX files): compressed bytes are
    /// copied out for the parallel decoder, raw texels are converted right away.
    /// </summary>
    private static PendingTexture? PrepareEmbedded(Context ctx, int index, bool srgb)
    {
        if (index < 0 || index >= ctx.Scene->MNumTextures)
        {
            Log.Warn($"Embedded texture '*{index}' does not exist.");
            return null;
        }

        AiTexture* embedded = ctx.Scene->MTextures[index];
        string name = $"embedded:{index}";
        if (embedded->MHeight == 0)
        {
            // Compressed (PNG/JPG...) bytes; MWidth is the byte count.
            var bytes = new ReadOnlySpan<byte>(embedded->PcData, (int)embedded->MWidth).ToArray();
            return new PendingTexture { Name = name, Srgb = srgb, Encoded = bytes };
        }

        // Raw BGRA texels; flip rows to match the bottom-left origin used everywhere else.
        int width = (int)embedded->MWidth, height = (int)embedded->MHeight;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var texel = embedded->PcData[y * width + x];
            int o = ((height - 1 - y) * width + x) * 4;
            pixels[o] = texel.R;
            pixels[o + 1] = texel.G;
            pixels[o + 2] = texel.B;
            pixels[o + 3] = texel.A;
        }
        return new PendingTexture { Name = name, Srgb = srgb, Rgba = pixels, Width = width, Height = height };
    }

    // ---- files the model depends on --------------------------------------------------------------

    /// <summary>Lists side files a format refers to by name: the .mtl of an OBJ, the .bin/images of a glTF.</summary>
    private static void AddCompanionFiles(string path, HashSet<string> files)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        try
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".obj":
                    foreach (var line in System.IO.File.ReadLines(path))
                        if (line.StartsWith("mtllib ", StringComparison.Ordinal))
                        {
                            string mtl = Path.GetFullPath(Path.Combine(directory, line[7..].Trim()));
                            if (System.IO.File.Exists(mtl)) files.Add(mtl);
                        }
                    break;

                case ".gltf":
                    using (var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path)))
                        foreach (var section in new[] { "buffers", "images" })
                            if (doc.RootElement.TryGetProperty(section, out var array))
                                foreach (var entry in array.EnumerateArray())
                                    if (entry.TryGetProperty("uri", out var uri) && uri.GetString() is { } value &&
                                        !value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string file = Path.GetFullPath(Path.Combine(directory, Uri.UnescapeDataString(value)));
                                        if (System.IO.File.Exists(file)) files.Add(file);
                                    }
                    break;
            }
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Log.Warn($"Could not scan '{path}' for side files: {e.Message}");
        }
    }
}
