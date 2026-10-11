using GT67.Scripting;
using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>A model (or mesh) placed in the world, with a transform, material and optional behaviour.</summary>
public sealed class GameObject
{
    private readonly List<GameObject> _children = new();
    private Model _model;

    // ---- construction ----------------------------------------------------------------------------

    public GameObject(Model model, float x, float y, float z) : this(model, new Vector3(x, y, z)) { }

    public GameObject(Model model) : this(model, Vector3.Zero) { }

    public GameObject(Model model, Vector3 position)
    {
        _model = model;
        model.Retain();
        Transform.Position = position;
    }

    /// <summary>Wraps the mesh in a model owned by this object (the mesh is freed on <see cref="Destroy"/>).</summary>
    public GameObject(Mesh mesh, float x, float y, float z) : this(Model.FromMesh(mesh), new Vector3(x, y, z)) { }

    public GameObject(Mesh mesh, Vector3 position) : this(Model.FromMesh(mesh), position) { }

    public GameObject(Mesh mesh) : this(Model.FromMesh(mesh), Vector3.Zero) { }

    // ---- identity --------------------------------------------------------------------------------

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "GameObject";
    public string Tag { get; set; } = "";

    /// <summary>The prefab this object was made from (see <see cref="Prefab"/>), or null. Kept by <see cref="Clone"/>.</summary>
    public string? PrefabName { get; set; }

    /// <summary>A disabled object is neither updated nor drawn (nor are its children).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hides this object only; children are still drawn.</summary>
    public bool Visible { get; set; } = true;

    public bool IsDestroyed { get; private set; }

    /// <summary>Belongs to the editor (gizmos, brush ring): not listed in the hierarchy, not pickable, not saved in the map.</summary>
    public bool EditorOnly { get; set; }

    /// <summary>Drawn last with the depth test off, so it always shows on top (gizmos). Never casts shadows or reflects.</summary>
    public bool Overlay { get; set; }
    public Scene? Scene { get; internal set; }
    public GameObject? Parent { get; private set; }
    public IReadOnlyList<GameObject> Children => _children;
    public Model Model => _model;

    /// <summary>Behaviour hook: called every frame with the object and the frame time.</summary>
    public Action<GameObject, float>? OnUpdate { get; set; }

    private readonly List<Script> _scripts = new();

    /// <summary>Behaviours attached to this object (see <see cref="Script"/>).</summary>
    public IReadOnlyList<Script> Scripts => _scripts;

    /// <summary>Attaches a new script of type <typeparamref name="T"/> and returns it so fields can be set.</summary>
    public T AddScript<T>() where T : Script, new() => AddScript(new T());

    public T AddScript<T>(T script) where T : Script
    {
        script.Self = this;
        _scripts.Add(script);
        return script;
    }

    /// <summary>Attaches a script created from its type (used by the editor and generated maps).</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "GT67 is a trimmer root (GT67.csproj), so every script keeps its constructor.")]
    public Script AddScript(Type type)
    {
        if (!typeof(Script).IsAssignableFrom(type) || type.IsAbstract)
            throw new ArgumentException($"{type.Name} is not a script type.", nameof(type));
        return AddScript((Script)Activator.CreateInstance(type)!);
    }

    public T? GetScript<T>() where T : Script => _scripts.OfType<T>().FirstOrDefault();

    public void RemoveScript(Script script)
    {
        if (!_scripts.Remove(script)) return;
        if (script.Started) script.OnDestroy();
    }

    /// <summary>Called once when a key goes down (any key, automatically). Null = ignore.</summary>
    public KeyCallback? OnKeyDown { get; set; }

    /// <summary>Called every frame for each key that is held down. Null = ignore.</summary>
    public KeyCallback? OnKeyHeld { get; set; }

    /// <summary>Called when a key is released. Null = ignore.</summary>
    public KeyCallback? OnKeyUp { get; set; }

    // ---- transform -------------------------------------------------------------------------------

    public Transform Transform { get; } = new();

    public Vector3 Position
    {
        get => Transform.Position;
        set => Transform.Position = value;
    }

    public float X { get => Transform.Position.X; set => Transform.Position = Transform.Position with { X = value }; }
    public float Y { get => Transform.Position.Y; set => Transform.Position = Transform.Position with { Y = value }; }
    public float Z { get => Transform.Position.Z; set => Transform.Position = Transform.Position with { Z = value }; }

    public Quaternion Rotation
    {
        get => Transform.Rotation;
        set => Transform.Rotation = value;
    }

    /// <summary>Rotation as Euler angles in degrees.</summary>
    public Vector3 EulerAngles
    {
        get => Transform.EulerAngles;
        set => Transform.EulerAngles = value;
    }

    public Vector3 Scale
    {
        get => Transform.Scale;
        set => Transform.Scale = value;
    }

    /// <summary>Sets/gets the scale as a single number (getter returns the largest axis).</summary>
    public float UniformScale
    {
        get => MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));
        set => Transform.Scale = new Vector3(value);
    }

    public Vector3 Forward => Transform.Forward;
    public Vector3 Right => Transform.Right;
    public Vector3 Up => Transform.Up;

    public Vector3 WorldPosition => Transform.WorldPosition;

    // ---- appearance ------------------------------------------------------------------------------

    public Material Material { get; private set; } = new();

    public Color4 Color
    {
        get => Material.Color;
        set => Material.Color = value;
    }

    public float Opacity
    {
        get => Material.Opacity;
        set => Material.Opacity = value;
    }

    public float Shininess
    {
        get => Material.Shininess;
        set => Material.Shininess = value;
    }

    public bool Unlit
    {
        get => Material.Unlit;
        set => Material.Unlit = value;
    }

    public bool CastShadow { get; set; } = true;
    public bool ReceiveShadow { get; set; } = true;
    public bool Wireframe { get; set; }
    public bool CullFace { get; set; } = true;

    /// <summary>How solid this object is for <see cref="Scene.MoveAndSlide"/> (default: exact triangle mesh).</summary>
    public CollisionMode Collision { get; set; } = CollisionMode.Mesh;

    /// <summary>
    /// Called when this object, or something it moved with <see cref="MoveAndSlide"/>, touches another solid object:
    /// (self, other, contact normal pointing away from <c>other</c>). Null = ignore.
    /// </summary>
    public Action<GameObject, GameObject, Vector3>? OnCollision { get; set; }

    public bool IsTransparent => Material.IsTransparent || _model.Parts.Any(p => p.Material.IsTransparent);

    // ---- bounds ----------------------------------------------------------------------------------

    /// <summary>World-space AABB of the model under the current transform.</summary>
    public Aabb Bounds => _model.Bounds.Transformed(Transform.WorldMatrix);

    public BoundingSphere BoundingSphere
    {
        get
        {
            var world = Transform.WorldMatrix;
            var center = Vector3.TransformPosition(_model.Bounds.Center, world);
            var s = world.ExtractScale();
            float maxScale = MathF.Max(MathF.Abs(s.X), MathF.Max(MathF.Abs(s.Y), MathF.Abs(s.Z)));
            return new BoundingSphere(center, _model.Bounds.Extents.Length * maxScale);
        }
    }

    // ---- transform helpers -----------------------------------------------------------------------

    public void SetPosition(float x, float y, float z) => Transform.Position = new Vector3(x, y, z);
    public void Translate(float dx, float dy, float dz) => Transform.Position += new Vector3(dx, dy, dz);
    public void Translate(Vector3 delta) => Transform.Position += delta;

    public void Rotate(Vector3 axis, float degrees) => Transform.Rotate(axis, degrees);
    public void RotateX(float degrees) => Transform.Rotate(Vector3.UnitX, degrees);
    public void RotateY(float degrees) => Transform.Rotate(Vector3.UnitY, degrees);
    public void RotateZ(float degrees) => Transform.Rotate(Vector3.UnitZ, degrees);

    public void SetScale(float uniform) => Transform.Scale = new Vector3(uniform);
    public void SetScale(float x, float y, float z) => Transform.Scale = new Vector3(x, y, z);

    public void LookAt(Vector3 target) => Transform.LookAt(target);
    public void LookAt(GameObject target) => Transform.LookAt(target.WorldPosition);

    public float DistanceTo(GameObject other) => (WorldPosition - other.WorldPosition).Length;

    public bool Intersects(GameObject other) => Bounds.Intersects(other.Bounds);

    // ---- material helpers ------------------------------------------------------------------------

    /// <summary>Loads (and caches) a diffuse texture. Relative paths resolve next to the executable.</summary>
    /// <summary>Draws this object with a custom shader (a .shader file, see <see cref="ShaderLibrary"/>); "" goes back to the normal look.</summary>
    public void SetShader(string name) => Material.CustomShader = name;

    /// <summary>Changes a parameter of this object's custom shader, e.g. <c>obj.SetShaderParam("Speed", 3f)</c> or a colour <c>("Tint", 1f, 0f, 0f)</c>.</summary>
    public void SetShaderParam(string name, float x, float y = 0f, float z = 0f) => Material.SetShaderParam(name, x, y, z);

    /// <summary>Gives a <c>texture</c> parameter of this object's custom shader an image, e.g. <c>obj.SetShaderTexture("Mask", "Assets/Textures/mask.png")</c>.</summary>
    public void SetShaderTexture(string name, string path) => Material.SetShaderTexture(name, path);

    public void SetTexture(string path) => Material.DiffuseMap = ModelCache.GetTexture(path);

    public void SetTexture(Texture texture) => Material.DiffuseMap = texture;

    public void SetMaterial(Material material) => Material = material;

    public void SetColor(float r, float g, float b, float a = 1f) => Material.Color = new Color4(r, g, b, a);
    public void SetColor(Color4 color) => Material.Color = color;

    // ---- hierarchy -------------------------------------------------------------------------------

    public void AddChild(GameObject child)
    {
        if (child == this || child.IsAncestorOf(this))
            throw new InvalidOperationException("A GameObject cannot be parented to itself or its descendant.");

        child.Parent?.RemoveChild(child);
        child.Scene?.RemoveRoot(child);
        child.Parent = this;
        child.Transform.Parent = Transform;
        Scene.SetScene(child, Scene);
        _children.Add(child);
    }

    /// <summary>
    /// Moves this object under <paramref name="newParent"/> (null = back to the scene's top level) without moving it in
    /// the world: from then on it moves, turns and scales with its parent.
    /// </summary>
    public void SetParent(GameObject? newParent)
    {
        if (newParent == Parent) return;
        var world = Transform.WorldMatrix;
        var scene = Scene ?? newParent?.Scene;
        if (newParent == null)
        {
            Parent!.RemoveChild(this);
            scene?.Add(this);
        }
        else newParent.AddChild(this);

        var local = newParent == null ? world : world * Matrix4.Invert(newParent.Transform.WorldMatrix);
        Position = local.ExtractTranslation();
        Rotation = local.ExtractRotation();
        Scale = local.ExtractScale();
    }

    /// <summary>True if <paramref name="other"/> is this object or one of its children (at any depth).</summary>
    public bool Contains(GameObject other)
    {
        for (var p = other; p != null; p = p.Parent)
            if (p == this) return true;
        return false;
    }

    public void RemoveChild(GameObject child)
    {
        if (!_children.Remove(child)) return;
        child.Parent = null;
        child.Transform.Parent = null;
        Scene.SetScene(child, null);
    }

    private bool IsAncestorOf(GameObject other)
    {
        for (var p = other.Parent; p != null; p = p.Parent)
            if (p == this) return true;
        return false;
    }

    /// <summary>Copy sharing the model; children are cloned too. The clone is not added to any scene.</summary>
    public GameObject Clone()
    {
        var copy = new GameObject(_model, Transform.Position)
        {
            Name = Name + " (Clone)",
            Tag = Tag,
            PrefabName = PrefabName,
            Enabled = Enabled,
            Visible = Visible,
            CastShadow = CastShadow,
            ReceiveShadow = ReceiveShadow,
            Wireframe = Wireframe,
            CullFace = CullFace,
            OnUpdate = OnUpdate,
            OnKeyDown = OnKeyDown,
            OnKeyHeld = OnKeyHeld,
            OnKeyUp = OnKeyUp,
            Collision = Collision,
            OnCollision = OnCollision,
        };
        copy.Transform.Rotation = Transform.Rotation;
        copy.Transform.Scale = Transform.Scale;
        copy.Material = Material.Clone();
        foreach (var script in _scripts)
        {
            var copyScript = copy.AddScript(script.GetType());
            ParameterAccess.CopyValues(script, copyScript);
        }
        foreach (var child in _children) copy.AddChild(child.Clone());
        return copy;
    }

    /// <summary>
    /// Moves this object like a sphere of <paramref name="radius"/> (centred on its position, plus
    /// <paramref name="offset"/>) by <paramref name="delta"/>, sliding along solid objects in the scene instead of
    /// passing through them. Fires <see cref="OnCollision"/> on both sides of every contact. Gravity is yours to add:
    /// <c>self.MoveAndSlide(velocity * dt, 0.4f)</c>, and check <see cref="MoveResult.Grounded"/> before jumping.
    /// For a standing capsule put the sphere in its feet: <c>offset = new Vector3(0, radius - height / 2, 0)</c>.
    /// </summary>
    public MoveResult MoveAndSlide(Vector3 delta, float radius, Vector3 offset = default)
    {
        var start = WorldPosition + offset;
        if (Scene == null)
        {
            Translate(delta);
            return new MoveResult(start + delta, false, false, Vector3.UnitY, null);
        }

        var contacts = new List<(GameObject other, Vector3 normal)>();
        var result = Scene.MoveAndSlide(start, delta, radius, this, (other, normal) => contacts.Add((other, normal)));

        var position = result.Position - offset;
        Transform.Position = Parent == null
            ? position
            : Vector3.TransformPosition(position, Matrix4.Invert(Parent.Transform.WorldMatrix));

        foreach (var (other, normal) in contacts)
        {
            OnCollision?.Invoke(this, other, normal);
            foreach (var script in _scripts.ToArray()) if (script.Enabled) script.OnCollision(other, normal);
            other.OnCollision?.Invoke(other, this, -normal);
            foreach (var script in other._scripts.ToArray()) if (script.Enabled) script.OnCollision(this, -normal);
        }
        return result;
    }

    internal KeyCallback? GetKeyCallback(KeyAction action) => action switch
    {
        KeyAction.Down => OnKeyDown,
        KeyAction.Held => OnKeyHeld,
        _ => OnKeyUp,
    };

    /// <summary>Removes the object (and its children) from the scene and releases the model reference.</summary>
    public void Destroy()
    {
        if (IsDestroyed) return;
        IsDestroyed = true;
        foreach (var script in _scripts.ToArray()) if (script.Started) script.OnDestroy();

        foreach (var child in _children.ToArray()) child.Destroy();
        _children.Clear();

        Parent?.RemoveChild(this);
        Scene?.RemoveRoot(this);
        Scene = null;
        _model.Release();
    }

    // ---- update / draw ---------------------------------------------------------------------------

    public void Update(float deltaTime)
    {
        if (!Enabled || IsDestroyed) return;
        if (Scene is { Simulate: false }) return; // the editor pauses the world while you edit it

        OnUpdate?.Invoke(this, deltaTime);
        RunScripts(deltaTime);

        if (_children.Count == 0) return;
        foreach (var child in _children.ToArray()) child.Update(deltaTime);
    }

    /// <summary>Calls <see cref="Script.OnLateUpdate"/> once every object has updated (the scene does this each frame).</summary>
    public void LateUpdate(float deltaTime)
    {
        if (!Enabled || IsDestroyed) return;
        if (Scene is { Simulate: false }) return;

        foreach (var script in _scripts.ToArray())
        {
            if (IsDestroyed) return;
            if (!script.Enabled || !script.Started) continue;
            try
            {
                script.OnLateUpdate(deltaTime);
            }
            catch (Exception e)
            {
                script.Enabled = false;
                Log.Error($"Script {script.GetType().Name} on '{Name}' failed and was disabled: {e.Message}");
            }
        }

        if (_children.Count == 0) return;
        foreach (var child in _children.ToArray()) child.LateUpdate(deltaTime);
    }

    private void RunScripts(float deltaTime)
    {
        if (_scripts.Count == 0) return;
        foreach (var script in _scripts.ToArray())
        {
            if (IsDestroyed) return;
            if (!script.Enabled) continue;
            try
            {
                if (!script.Started)
                {
                    script.Started = true;
                    script.OnStart();
                }
                script.OnUpdate(deltaTime);
            }
            catch (Exception e)
            {
                script.Enabled = false; // do not repeat the error every frame
                Log.Error($"Script {script.GetType().Name} on '{Name}' failed and was disabled: {e.Message}");
            }
        }
    }

    /// <summary>True if a key callback or any script wants key events of this kind.</summary>
    internal bool HasKeyHandler(KeyAction action)
    {
        if (GetKeyCallback(action) != null) return true;
        foreach (var script in _scripts)
            if (script.Enabled && script.Handles(action)) return true;
        return false;
    }

    internal void RaiseKey(KeyAction action, OpenTK.Windowing.GraphicsLibraryFramework.Keys key, Vector3 mouseWorld,
        Vector3 cameraPosition, float deltaTime)
    {
        GetKeyCallback(action)?.Invoke(key, mouseWorld, cameraPosition, WorldPosition, this, deltaTime);
        foreach (var script in _scripts.ToArray())
        {
            if (!script.Enabled || !script.Handles(action)) continue;
            try
            {
                script.RaiseKey(action, key, mouseWorld, cameraPosition);
            }
            catch (Exception e)
            {
                script.Enabled = false;
                Log.Error($"Script {script.GetType().Name} on '{Name}' failed and was disabled: {e.Message}");
            }
        }
    }

    /// <summary>Draws with the lit shader; frame uniforms (camera, lights) must already be set.</summary>
    public void Draw(Camera camera, Shader shader, bool forceWireframe = false)
    {
        if (!Visible || !Enabled || IsDestroyed) return;

        var world = Transform.WorldMatrix;
        shader.SetMatrix4("uModel", world);
        shader.SetMatrix3("uNormalMatrix", NormalMatrix(world));
        shader.SetBool("uReceiveShadow", ReceiveShadow);

        if (CullFace)
        {
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(TriangleFace.Back);
        }
        else
        {
            GL.Disable(EnableCap.CullFace);
        }

        bool wire = Wireframe || forceWireframe;
        if (wire) GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);

        foreach (var part in _model.Parts)
        {
            ApplyMaterial(shader, part.Material);
            part.Mesh.Draw();
        }

        if (wire && !forceWireframe) GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
    }

    /// <summary>Depth-only draw used by the shadow pass.</summary>
    public void DrawShadow(Shader depthShader)
    {
        if (!Visible || !Enabled || IsDestroyed || !CastShadow) return;
        depthShader.SetMatrix4("uModel", Transform.WorldMatrix);
        foreach (var part in _model.Parts) part.Mesh.Draw();
    }

    private void ApplyMaterial(Shader shader, Material part)
    {
        // Object-level values tint/override the model's own material.
        var tint = MathUtil.ToLinear(Material.Color) * MathUtil.ToLinear(part.Color);
        float opacity = Material.Opacity * part.Opacity * Material.Color.A * part.Color.A;
        var diffuse = Material.DiffuseMap ?? part.DiffuseMap;
        var specular = Material.SpecularMap ?? part.SpecularMap;
        var normal = Material.NormalMap ?? part.NormalMap;

        shader.SetVector4("uColor", new Vector4(tint, opacity));
        shader.SetFloat("uShininess", Material.Shininess);
        shader.SetFloat("uSpecularStrength", Material.SpecularStrength);
        shader.SetVector2("uUvScale", Material.UvScale * part.UvScale);
        shader.SetBool("uUnlit", Material.Unlit || part.Unlit);

        BindMap(shader, diffuse, TextureUnit.Texture0, "uHasDiffuse");
        BindMap(shader, specular, TextureUnit.Texture1, "uHasSpecular");
        BindMap(shader, normal, TextureUnit.Texture2, "uHasNormal");
    }

    private static void BindMap(Shader shader, Texture? texture, TextureUnit unit, string flag)
    {
        shader.SetBool(flag, texture != null);
        texture?.Bind(unit);
    }

    private static Matrix3 NormalMatrix(Matrix4 world)
    {
        if (MathF.Abs(world.Determinant) < 1e-12f) return Matrix3.Identity;
        return new Matrix3(Matrix4.Transpose(Matrix4.Invert(world)));
    }

    // ---- picking ---------------------------------------------------------------------------------

    /// <summary>Ray test against the AABB first, then the actual triangles. Distance is along the ray.</summary>
    public bool Raycast(Ray ray, out float distance)
    {
        distance = float.MaxValue;
        if (EditorOnly || !Visible || !Enabled || !Bounds.IntersectRay(ray, out _)) return false;

        var world = Transform.WorldMatrix;
        if (MathF.Abs(world.Determinant) < 1e-12f) return false;

        // Work in local space; the un-normalised direction keeps t identical to the world-space distance.
        var inverse = Matrix4.Invert(world);
        var origin = Vector3.TransformPosition(ray.Origin, inverse);
        var direction = Vector3.TransformVector(ray.Direction, inverse);

        bool hit = false;
        foreach (var part in _model.Parts)
        {
            var mesh = part.Mesh;
            if (mesh.PrimitiveType != PrimitiveType.Triangles) continue;

            var v = mesh.Vertices;
            var idx = mesh.Indices;
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                if (MathUtil.RayTriangle(origin, direction, v[idx[i]].Position, v[idx[i + 1]].Position,
                        v[idx[i + 2]].Position, out float t) && t < distance)
                {
                    distance = t;
                    hit = true;
                }
            }
        }
        return hit;
    }
}
