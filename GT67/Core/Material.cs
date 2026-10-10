using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>Surface appearance. Textures are borrowed (owned by whoever loaded them, usually the asset cache).</summary>
public sealed class Material
{
    /// <summary>Tint multiplied with the diffuse texture (sRGB).</summary>
    public Color4 Color { get; set; } = Color4.White;

    public float Opacity { get; set; } = 1f;
    public float Shininess { get; set; } = 32f;
    public float SpecularStrength { get; set; } = 0.5f;

    /// <summary>Skip lighting and output the colour directly (grid lines, gizmos).</summary>
    public bool Unlit { get; set; }

    /// <summary>UV multiplier, useful to tile a texture over large planes.</summary>
    public Vector2 UvScale { get; set; } = Vector2.One;

    public Texture? DiffuseMap { get; set; }
    public Texture? SpecularMap { get; set; }
    public Texture? NormalMap { get; set; }

    public bool IsTransparent => Opacity < 0.999f || Color.A < 0.999f;

    /// <summary>
    /// Name of a custom shader (a .shader file in Shaders/Custom, see <see cref="ShaderLibrary"/>) that draws this
    /// object, or empty for the normal lit shader.
    /// </summary>
    public string CustomShader { get; set; } = "";

    private Dictionary<string, Vector4>? _shaderParams;

    /// <summary>Values this object gives the custom shader's <c>param</c>s (the ones not set use the file's defaults).</summary>
    public IReadOnlyDictionary<string, Vector4> ShaderParams => _shaderParams ??= new();

    /// <summary>Sets one of the custom shader's parameters, e.g. <c>SetShaderParam("Speed", 3f)</c> or <c>("Tint", 1f, 0f, 0f)</c>.</summary>
    public void SetShaderParam(string name, float x, float y = 0f, float z = 0f) =>
        (_shaderParams ??= new())[name] = new Vector4(x, y, z, 0f);

    public void ClearShaderParam(string name) => _shaderParams?.Remove(name);

    private Dictionary<string, string>? _shaderTextures;

    /// <summary>Images this object gives the custom shader's <c>texture</c> params, as file paths (the others use the file's defaults).</summary>
    public IReadOnlyDictionary<string, string> ShaderTextures => _shaderTextures ??= new();

    /// <summary>Sets one of the custom shader's texture parameters to an image file, e.g. <c>SetShaderTexture("Mask", "Assets/Textures/mask.png")</c>.</summary>
    public void SetShaderTexture(string name, string path) => (_shaderTextures ??= new())[name] = path;

    public void ClearShaderTexture(string name) => _shaderTextures?.Remove(name);

    public Material Clone()
    {
        var copy = (Material)MemberwiseClone();
        copy._shaderParams = _shaderParams == null ? null : new Dictionary<string, Vector4>(_shaderParams);
        copy._shaderTextures = _shaderTextures == null ? null : new Dictionary<string, string>(_shaderTextures);
        return copy;
    }
}
