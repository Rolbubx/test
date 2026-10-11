using GT67.Core;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Editor;

/// <summary>Small pickable spheres at the position of every point and spot light, so lights can be selected and moved in the viewport.</summary>
public sealed class LightMarkers
{
    private readonly Dictionary<Light, GameObject> _markers = new();
    private readonly List<Light> _gone = new();

    public LightMarkers(Scene scene) => Scene = scene;

    /// <summary>The scene whose lights get markers (the editor changes it when another scene is opened).</summary>
    public Scene Scene { get; set; }

    public static Vector3 PositionOf(Light light) => light switch
    {
        PointLight p => p.Position,
        SpotLight s => s.Position,
        _ => Vector3.Zero,
    };

    private IEnumerable<Light> Lights()
    {
        foreach (var point in Scene.PointLights) yield return point;
        foreach (var spot in Scene.SpotLights) yield return spot;
    }

    /// <summary>Creates, moves and removes the markers; call every frame.</summary>
    public void Update(Camera camera, bool show, Light? selected)
    {
        var lights = Lights().ToList();

        _gone.Clear();
        foreach (var (light, marker) in _markers)
        {
            if (lights.Contains(light)) continue;
            marker.Destroy();
            _gone.Add(light);
        }
        foreach (var light in _gone) _markers.Remove(light);

        foreach (var light in lights)
        {
            if (!_markers.TryGetValue(light, out var marker))
            {
                marker = new GameObject(Primitives.Sphere(0.5f, 14), Vector3.Zero)
                {
                    Name = "Light marker",
                    Unlit = true,
                    CastShadow = false,
                    ReceiveShadow = false,
                    Collision = CollisionMode.None,
                    EditorOnly = true,
                    Overlay = true,
                };
                Scene.Add(marker);
                _markers[light] = marker;
            }

            var position = PositionOf(light);
            float size = MathF.Max((camera.Position - position).Length * 0.035f, 0.12f);
            bool isSelected = light == selected;
            marker.Visible = show;
            marker.Position = position;
            marker.UniformScale = isSelected ? size * 1.35f : size;
            var color = light.Color;
            marker.Color = isSelected
                ? new Color4(1f, 0.95f, 0.3f, 1f)
                : light.Enabled ? new Color4(color.R, color.G, color.B, 1f) : new Color4(0.35f, 0.35f, 0.35f, 1f);
        }
    }

    /// <summary>The light whose marker the ray passes closest to, or null.</summary>
    public Light? Pick(Ray ray, Camera camera)
    {
        Light? best = null;
        float bestDistance = float.MaxValue;
        foreach (var light in Lights())
        {
            var position = PositionOf(light);
            float radius = MathF.Max((camera.Position - position).Length * 0.035f, 0.12f) * 0.9f;
            float along = Vector3.Dot(position - ray.Origin, ray.Direction);
            if (along <= 0f) continue;

            float distance = Vector3.Cross(ray.Direction, position - ray.Origin).Length;
            if (distance < radius && along < bestDistance)
            {
                bestDistance = along;
                best = light;
            }
        }
        return best;
    }
}
