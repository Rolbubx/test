namespace GT67.Core;

/// <summary>A mesh paired with the material it was authored with.</summary>
public sealed class ModelPart
{
    public Mesh Mesh { get; }
    public Material Material { get; }
    public string Name { get; }

    public ModelPart(Mesh mesh, Material material, string name = "part")
    {
        Mesh = mesh;
        Material = material;
        Name = name;
    }
}
