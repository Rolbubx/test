using GT67.Core;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Editor;

/// <summary>Turns a model file (fbx, gltf, glb, obj, ...) into a placed, solid object in the scene.</summary>
public static class ModelImporter
{
    public static readonly string[] Extensions = { ".fbx", ".gltf", ".glb", ".obj", ".dae", ".3ds", ".blend", ".stl", ".ply" };

    public static bool IsModelFile(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads the file (textures included, from wherever it lies on disk), sizes it sensibly, sets it on the ground at
    /// <paramref name="position"/> and adds it to the scene with mesh collision.
    /// </summary>
    public static GameObject? Import(Scene scene, string path, Vector3 position, out string message, ProjectPaths? paths = null)
    {
        Model model;
        try
        {
            model = Model.Load(path);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            message = $"Could not import '{Path.GetFileName(path)}': {e.Message}";
            Log.Warn(message);
            return null;
        }

        var obj = new GameObject(model, position)
        {
            Name = UniqueName(scene, Path.GetFileNameWithoutExtension(path)),
            Collision = CollisionMode.Mesh,
        };

        // Many FBX files are modelled in centimetres (a person is 180 units tall) and some tiny props in metres.
        var size = model.Bounds.Size;
        float largest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        string note = "";
        float scale = 1f;
        if (largest > 40f && largest <= 4000f)
        {
            scale = 0.01f;
            note = " (looked like centimetres, scaled by 0.01)";
        }
        else if (largest > 4000f)
        {
            scale = 4f / largest;
            note = " (very large, scaled to fit)";
        }
        else if (largest < 0.05f && largest > 0f)
        {
            scale = 1f / largest;
            note = " (very small, scaled up)";
        }
        obj.UniformScale = scale;


        // Stand it on the point instead of burying half of it.
        obj.Position = position + new Vector3(0f, -model.Bounds.Min.Y * scale, 0f);

        scene.Add(obj);
        string copied = paths != null ? CopyIntoProject(model, paths) : "";
        int textures = model.Parts.Select(p => p.Material.DiffuseMap).OfType<Texture>().Distinct().Count();
        message = $"Imported '{Path.GetFileName(path)}' ({model.Parts.Count} part(s), " +
                  $"{model.Parts.Sum(p => p.Mesh.Indices.Length / 3)} triangles, " +
                  $"{(textures == 0 ? "no textures" : textures == 1 ? "1 texture" : $"{textures} textures")}){note}.{copied}";
        Log.Info(message);
        return obj;
    }

    /// <summary>
    /// Copies a model from outside the project, with its textures and side files (.bin, .mtl), into
    /// Assets/Models/&lt;name&gt;/ of the project (kept) and of the build output (listed in Assets &gt; Models right away),
    /// and points the loaded model at that copy. Models already in the project are left where they are.
    /// </summary>
    private static string CopyIntoProject(Model model, ProjectPaths paths)
    {
        string source = model.SourcePath!;
        string? relative = paths.ToProjectRelative(source);
        if (relative != null && (relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                                 relative.StartsWith("Generated/", StringComparison.OrdinalIgnoreCase)))
            return "";

        string folder = Path.GetFileNameWithoutExtension(source);
        string modelDirectory = Path.GetDirectoryName(source)!;
        string outputModels = Path.Combine(AppContext.BaseDirectory, "Assets", "Models");
        var copies = new List<string>();
        try
        {
            foreach (var file in model.SourceFiles.Append(source).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(file)) continue;
                string inside = Path.GetRelativePath(modelDirectory, file);
                if (inside.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(inside)) inside = Path.GetFileName(file);
                foreach (var root in new[] { paths.Models, outputModels })
                {
                    string target = Path.Combine(root, folder, inside);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: true);
                }
                copies.Add(Path.Combine(outputModels, folder, inside));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not copy '{Path.GetFileName(source)}' into the project: {e.Message}");
            return " (It stays where it is: copying it into Assets/Models failed.)";
        }

        string copiedModel = Path.Combine(outputModels, folder, Path.GetFileName(source));
        ModelCache.Alias(copiedModel, model);
        model.SourcePath = copiedModel;
        model.SourceFiles = copies;
        return $" Copied into Assets/Models/{folder}/ ({copies.Count} file(s)).";
    }

    public static string UniqueName(Scene scene, string baseName)
    {
        if (scene.Find(baseName) == null) return baseName;
        for (int i = 2; ; i++)
        {
            string candidate = $"{baseName} {i}";
            if (scene.Find(candidate) == null) return candidate;
        }
    }
}
