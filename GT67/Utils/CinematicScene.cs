using GT67.Core;
using GT67.Effects;
using GT67.UI;
using OpenTK.Mathematics;

namespace GT67.Utils;

/// <summary>
/// "Golden hour": a tropical island with a lighthouse, palms and a rowing boat, seen at sunset through a camera that
/// flies along a path (<see cref="CinematicCamera"/>) with depth of field, sun shafts, clouds, grain and letterbox bars.
/// Start it with <c>--cinematic</c>.
/// </summary>
public static class CinematicScene
{
    // The island is described in "sun space": a runs towards the sun, b across it. That way the sun always sits behind
    // the lighthouse hill as seen from the sea, whatever the compass says.
    private static Vector2 _f, _r;

    private static Vector2 ToLocal(float x, float z) => new(x * _f.X + z * _f.Y, x * _r.X + z * _r.Y);
    private static Vector3 ToWorld(float a, float b, float y) => new(a * _f.X + b * _r.X, y, a * _f.Y + b * _r.Y);

    private static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Color4 Mix(Color4 a, Color4 b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, 1f);

    private static float Hash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)z * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFu) / 65535f;
        }
    }

    private static float Value(float x, float z)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float fx = x - ix, fz = z - iz;
        float sx = fx * fx * (3f - 2f * fx), sz = fz * fz * (3f - 2f * fz);
        float a = Hash(ix, iz), b = Hash(ix + 1, iz), c = Hash(ix, iz + 1), d = Hash(ix + 1, iz + 1);
        float top = a + (b - a) * sx, bottom = c + (d - c) * sx;
        return top + (bottom - top) * sz;
    }

    private static float Noise(float x, float z) =>
        0.55f * Value(x, z) + 0.3f * Value(x * 2.1f + 5f, z * 2.1f + 9f) + 0.15f * Value(x * 4.3f + 17f, z * 4.3f + 3f);

    private static float Bump(float a, float b, float ca, float cb, float radius, float height)
    {
        float d = MathF.Sqrt((a - ca) * (a - ca) + (b - cb) * (b - cb)) / radius;
        return height * MathF.Exp(-d * d * 2.2f);
    }

    /// <summary>Ground height at a world position: the water is at 0, the beach just above it.</summary>
    private static float Height(float x, float z)
    {
        var p = ToLocal(x, z);
        float a = p.X, b = p.Y;
        float wobble = (Noise(a * 0.012f + 40f, b * 0.012f) - 0.5f) * 0.28f;
        float r = MathF.Sqrt((a / 175f) * (a / 175f) + (b / 125f) * (b / 125f)) * (1f + wobble);
        float m = Math.Clamp(1f - r, 0f, 1f);

        if (r >= 1f) return -5f - MathF.Min(10f, (r - 1f) * 22f);

        float h = -5f + 5.8f * Smooth(0f, 0.3f, m);
        float land = Smooth(0.22f, 0.8f, m);
        h += land * 6f;
        h += Bump(a, b, -35f, 45f, 75f, 30f);     // the main hill
        h += Bump(a, b, 45f, -48f, 38f, 22f);     // the cliff under the lighthouse
        h += Bump(a, b, -95f, -35f, 45f, 11f);
        h += (Noise(a * 0.03f, b * 0.03f) - 0.5f) * 5f * Smooth(0.15f, 0.5f, m);
        h += (Noise(a * 0.15f, b * 0.15f) - 0.5f) * 0.9f * Smooth(0.2f, 0.4f, m);
        return h;
    }

    private static Vector4 GroundColor(float x, float z, float h)
    {
        float e = 0.5f;
        float slope = (MathF.Abs(Height(x + e, z) - Height(x - e, z)) + MathF.Abs(Height(x, z + e) - Height(x, z - e))) / (2f * e);
        float n = Noise(x * 0.09f, z * 0.09f);

        var sand = new Vector3(0.88f, 0.79f, 0.6f);
        var wet = new Vector3(0.55f, 0.47f, 0.34f);
        var grass = Vector3.Lerp(new Vector3(0.22f, 0.4f, 0.14f), new Vector3(0.38f, 0.5f, 0.2f), n);
        var forest = Vector3.Lerp(new Vector3(0.1f, 0.24f, 0.09f), new Vector3(0.16f, 0.32f, 0.1f), n);
        var rock = Vector3.Lerp(new Vector3(0.38f, 0.35f, 0.32f), new Vector3(0.55f, 0.5f, 0.44f), n);

        Vector3 c = sand;
        c = Vector3.Lerp(c, wet, 1f - Smooth(-0.3f, 0.5f, h));                 // wet sand at the waterline
        c = Vector3.Lerp(c, sand * (0.7f - 0.04f * MathF.Min(-h, 5f)), Smooth(0f, -0.5f, h)); // darker under water
        c = Vector3.Lerp(c, grass, Smooth(2.4f, 5.5f, h + (n - 0.5f) * 3f));
        c = Vector3.Lerp(c, forest, Smooth(9f, 20f, h + (n - 0.5f) * 8f));
        c = Vector3.Lerp(c, rock, Math.Clamp((slope - 0.55f) * 2.2f, 0f, 1f) * Smooth(0.5f, 3f, h));
        c = Vector3.Lerp(c, rock, Smooth(30f, 40f, h));
        return new Vector4(c.X, c.Y, c.Z, 1f);
    }

    public static void Build(Window window)
    {
        var scene = window.Scene;
        scene.Name = "Golden hour";

        // --- sun, sky, weather ------------------------------------------------------------------------------------
        var sun = new Sun { TimeOfDay = 17.55f, Latitude = 12f, Declination = 10f, NorthRotation = 0f, Intensity = 3f, ShadowSize = 90f, ShadowCenter = new Vector3(0f, 8f, 0f) };
        scene.Sun = sun;
        var toSun = sun.DirectionToSun;
        _f = Vector2.Normalize(new Vector2(toSun.X, toSun.Z));
        _r = new Vector2(-_f.Y, _f.X);

        scene.Skybox = new Skybox(new PhysicalSky
        {
            Brightness = 22f,
            Clouds = new CloudLayer { Coverage = 0.5f, Density = 1.1f, Altitude = 1800f, Thickness = 1600f, Evolution = 0.4f, WindSpeed = 6f, SunIntensity = 2.4f, Tint = new Color4(1f, 0.93f, 0.85f, 1f) },
        });
        scene.Rain = null;
        scene.Fog = 0.0002f;
        scene.AmbientIntensity = 0.45f;

        // --- ground -------------------------------------------------------------------------------------------------
        const float size = 420f;
        const int resolution = 420;
        var terrain = new Terrain(size, size, resolution, -5f);
        var heights = new float[(resolution + 1) * (resolution + 1)];
        var colors = new byte[heights.Length * 4];
        for (int j = 0; j <= resolution; j++)
        for (int i = 0; i <= resolution; i++)
        {
            float x = i * terrain.CellWidth - size * 0.5f, z = j * terrain.CellDepth - size * 0.5f;
            float h = Height(x, z);
            int k = j * (resolution + 1) + i;
            heights[k] = h;
            var c = GroundColor(x, z, h);
            colors[k * 4] = (byte)(Math.Clamp(c.X, 0f, 1f) * 255f);
            colors[k * 4 + 1] = (byte)(Math.Clamp(c.Y, 0f, 1f) * 255f);
            colors[k * 4 + 2] = (byte)(Math.Clamp(c.Z, 0f, 1f) * 255f);
            colors[k * 4 + 3] = 255;
        }
        var heightBytes = new byte[heights.Length * sizeof(float)];
        Buffer.BlockCopy(heights, 0, heightBytes, 0, heightBytes.Length);
        terrain.Import(Convert.ToBase64String(heightBytes), Convert.ToBase64String(colors));
        terrain.Object.Name = "Island";
        scene.Add(terrain);

        // --- the sea ------------------------------------------------------------------------------------------------
        var sea = new Water(0f, 0f, 0f, size, size) { Name = "Sea", ReachHorizon = true, WindDirection = _f };
        sea.ApplyBeachPreset();
        sea.WaveHeight = 0.28f;
        sea.WaveLength = 14f;
        sea.Choppiness = 0.5f;
        sea.SurfHeight = 0.35f;
        scene.Add(sea);

        // --- grass on the green parts -------------------------------------------------------------------------------
        scene.Grass = new GrassLayer { Height = 0.5f, MaxDistance = 90f };
        scene.Grass.Scatter(new Vector2(-170f, -170f), new Vector2(170f, 170f), 1.6f, (x, z) =>
        {
            float h = Height(x, z);
            if (h < 2.6f || h > 28f) return null;
            return h;
        });

        var random = new Random(11);

        // --- palms along the beach ----------------------------------------------------------------------------------
        var trunkColor = new Color4(0.42f, 0.3f, 0.2f, 1f);
        var frond = Primitives.Sphere(0.5f, 10);
        var trunkMesh = Primitives.Cylinder(0.16f, 1f, 8);
        int palms = 0;
        for (int tries = 0; tries < 900 && palms < 70; tries++)
        {
            float x = (float)(random.NextDouble() - 0.5) * 300f, z = (float)(random.NextDouble() - 0.5) * 240f;
            float h = Height(x, z);
            if (h < 1.3f || h > 7f) continue;
            if (random.NextDouble() < 0.35 && h > 4f) continue;
            palms++;

            float lean = 10f + (float)random.NextDouble() * 14f, yaw = (float)random.NextDouble() * 360f;
            float length = 6.5f + (float)random.NextDouble() * 4f;
            var trunk = new GameObject(trunkMesh, x, h + length * 0.5f - 0.3f, z) { Name = "Palm", Color = trunkColor, Collision = CollisionMode.None };
            trunk.SetScale(1f, length, 1f);
            // Lean the trunk around its base, then centre it on the leaning axis and put the crown at the tip.
            trunk.Rotate(Vector3.UnitZ, lean);
            trunk.Rotate(Vector3.UnitY, yaw);
            var baseP = new Vector3(x, h - 0.3f, z);
            var tip = baseP + trunk.Up * length;
            trunk.SetPosition((baseP.X + tip.X) * 0.5f, (baseP.Y + tip.Y) * 0.5f, (baseP.Z + tip.Z) * 0.5f);

            var crown = new GameObject(Primitives.Sphere(0.22f, 8), tip) { Name = "Crown", Color = new Color4(0.3f, 0.22f, 0.12f, 1f), Collision = CollisionMode.None, CastShadow = false };
            scene.Add(trunk, crown);
            int fronds = 8;
            for (int k = 0; k < fronds; k++)
            {
                float a = k * 360f / fronds + (float)random.NextDouble() * 20f;
                var pivot = new GameObject(Primitives.Sphere(0.01f, 4), 0, 0, 0) { Name = "Pivot", Collision = CollisionMode.None, CastShadow = false, Visible = false };
                var leaf = new GameObject(frond, 1.6f, 0f, 0f)
                    { Name = "Frond", Color = Mix(new Color4(0.14f, 0.36f, 0.1f, 1f), new Color4(0.3f, 0.5f, 0.14f, 1f), (float)random.NextDouble()), Collision = CollisionMode.None, CastShadow = false };
                leaf.SetScale(3.4f, 0.05f, 0.7f);
                leaf.Rotate(Vector3.UnitZ, -26f);
                pivot.AddChild(leaf);
                pivot.Rotate(Vector3.UnitY, a);
                crown.AddChild(pivot);
            }
        }

        // --- rocks in the shallows and on the shore -----------------------------------------------------------------
        for (int n = 0, rocks = 0; n < 600 && rocks < 40; n++)
        {
            float x = (float)(random.NextDouble() - 0.5) * 340f, z = (float)(random.NextDouble() - 0.5) * 280f;
            float h = Height(x, z);
            if (h < -1.6f || h > 1.2f) continue;
            rocks++;
            float s = 0.7f + (float)random.NextDouble() * 2.2f;
            var rock = new GameObject(Primitives.Sphere(0.5f, 14), x, h + s * 0.1f, z)
                { Name = "Rock", Color = Mix(new Color4(0.32f, 0.3f, 0.29f, 1f), new Color4(0.5f, 0.46f, 0.4f, 1f), (float)random.NextDouble()), Collision = CollisionMode.None };
            rock.Shininess = 6f;
            rock.SetScale(s * 1.3f, s * 0.8f, s);
            rock.RotateY((float)random.NextDouble() * 360f);
            scene.Add(rock);
        }

        // --- the lighthouse -----------------------------------------------------------------------------------------
        var lp = ToWorld(45f, -48f, 0f);
        float baseY = Height(lp.X, lp.Z) - 0.5f;
        const float towerHeight = 15f;
        var white = new Color4(0.93f, 0.91f, 0.86f, 1f);
        var red = new Color4(0.72f, 0.14f, 0.12f, 1f);
        var tower = new GameObject(Primitives.Cylinder(1.9f, towerHeight, 28), lp.X, baseY + towerHeight * 0.5f, lp.Z) { Name = "Lighthouse", Color = white };
        var band1 = new GameObject(Primitives.Cylinder(1.78f, 2.4f, 28), lp.X, baseY + 4.2f, lp.Z) { Name = "Band", Color = red, Collision = CollisionMode.None };
        var band2 = new GameObject(Primitives.Cylinder(1.62f, 2.4f, 28), lp.X, baseY + 9.4f, lp.Z) { Name = "Band", Color = red, Collision = CollisionMode.None };
        var gallery = new GameObject(Primitives.Cylinder(2.5f, 0.35f, 28), lp.X, baseY + towerHeight + 0.1f, lp.Z) { Name = "Gallery", Color = new Color4(0.15f, 0.15f, 0.16f, 1f), Collision = CollisionMode.None };
        var lamp = new GameObject(Primitives.Cylinder(1.25f, 2.2f, 20), lp.X, baseY + towerHeight + 1.3f, lp.Z) { Name = "Lamp", Color = new Color4(1f, 0.82f, 0.45f, 1f), Unlit = true, CastShadow = false, Collision = CollisionMode.None };
        var roof = new GameObject(Primitives.Cone(1.6f, 1.9f, 28), lp.X, baseY + towerHeight + 3.35f, lp.Z) { Name = "Roof", Color = red, Collision = CollisionMode.None };
        var hut = new GameObject(Primitives.Cube(1f), lp.X + 3.4f, baseY + 1.2f, lp.Z) { Name = "Keeper's hut", Color = white };
        hut.SetScale(3.2f, 2.4f, 3f);
        var hutRoof = new GameObject(Primitives.Cone(2.6f, 1.3f, 4), hut.Position + new Vector3(0f, 1.8f, 0f)) { Name = "Hut roof", Color = new Color4(0.35f, 0.18f, 0.12f, 1f), Collision = CollisionMode.None };
        hutRoof.RotateY(45f);
        scene.Add(tower, band1, band2, gallery, lamp, roof, hut, hutRoof);
        scene.AddLight(new PointLight(new Vector3(lp.X, baseY + towerHeight + 1.3f, lp.Z), new Color4(1f, 0.8f, 0.45f, 1f), intensity: 8f, range: 45f) { Name = "Lighthouse lamp" });

        // --- a rowing boat, bobbing in the cove ---------------------------------------------------------------------
        var bp = ToWorld(120f, 50f, 0f);
        var boat = new GameObject(Primitives.Sphere(0.1f, 6), bp.X, 0.2f, bp.Z) { Name = "Boat", Color = new Color4(0.5f, 0.3f, 0.16f, 1f), Collision = CollisionMode.None };
        boat.RotateY(70f);
        var hull = new GameObject(Primitives.Sphere(0.5f, 20), 0f, 0.35f, 0f) { Name = "Hull", Color = new Color4(0.5f, 0.3f, 0.16f, 1f), Collision = CollisionMode.None };
        hull.SetScale(4.2f, 0.9f, 1.6f);
        var mast = new GameObject(Primitives.Cylinder(0.05f, 4.2f, 8), 0.3f, 2.6f, 0f) { Name = "Mast", Color = new Color4(0.35f, 0.25f, 0.15f, 1f), Collision = CollisionMode.None };
        var sail = new GameObject(Primitives.Cube(1f), 0.3f, 2.9f, 0.05f) { Name = "Sail", Color = new Color4(0.95f, 0.93f, 0.88f, 1f), Collision = CollisionMode.None };
        sail.SetScale(1.8f, 2.6f, 0.02f);
        scene.Add(boat);
        boat.AddChild(hull);
        boat.AddChild(mast);
        boat.AddChild(sail);
        boat.AddScript(new Buoyancy { Density = 0.45f, Drift = 0f });

        // --- post-processing: the look ------------------------------------------------------------------------------
        var post = window.Renderer.Post;
        post.Set("Bloom", "Intensity", 0.38f);
        post.Set("Bloom", "Threshold", 1.1f);
        post.Set("Bloom", "Radius", 1.3f);
        post.Set("SunRays", "Intensity", 0.45f);
        post.Set("SunRays", "GroundHaze", 0.005f);
        post.Set("DepthOfField", "Enabled", true);
        post.Set("DepthOfField", "MaxBlur", 3.5f);
                post.Set("ChromaticAberration", "Enabled", true);
        post.Set("ChromaticAberration", "Strength", 0.0025f);
        post.Set("FilmGrain", "Enabled", true);
        post.Set("FilmGrain", "Strength", 0.04f);
        post.Set("Vignette", "Intensity", 0.55f);
        post.Set("ColorCorrection", "Contrast", 1.14f);
        post.Set("ColorCorrection", "Saturation", 1.15f);
        post.Set("ColorCorrection", "Temperature", 0.06f);
        post.Set("ToneMapping", "Exposure", 1.05f);

        // --- the camera ---------------------------------------------------------------------------------------------
        // Keyframes in sun space (a towards the sun, b across it, height in metres above the sea); the camera never goes
        // below the ground or the waves. Each shot is a few keys of the same smooth curve.
        var keys = new (float a, float b, float y, float ta, float tb, float ty, float fov)[]
        {
            // 1. low over the water, the sun behind the lighthouse hill
            (275f, -70f, 3.2f,   30f, -45f, 20f,   42f),
            (225f, -62f, 2.6f,   30f, -45f, 22f,   40f),
            (175f, -80f, 2.2f,   40f, -48f, 21f,   38f),
            // 2. around the cliff and the lighthouse, rising
            (130f, -110f, 5f,    45f, -48f, 24f,   36f),
            (85f, -125f, 12f,    45f, -48f, 26f,   34f),
            (40f, -118f, 22f,    45f, -48f, 28f,   38f),
            // 3. over the island, the beach and palms below
            (-10f, -80f, 38f,    -35f, 20f, 8f,    44f),
            (-70f, -30f, 34f,    -90f, 40f, 4f,    48f),
            // 4. sweeping out to sea, looking back at the island lit by the sun
            (-150f, 40f, 24f,    -40f, 10f, 14f,   46f),
            (-210f, 95f, 12f,    -10f, 0f, 12f,    42f),
            (-215f, 150f, 6f,    50f, -20f, 18f,   38f),
            (-190f, 165f, 4f,    80f, -40f, 16f,   36f),
        };
        var positions = new Vector3[keys.Length];
        var targets = new Vector3[keys.Length];
        var fovs = new float[keys.Length];
        for (int k = 0; k < keys.Length; k++)
        {
            positions[k] = ToWorld(keys[k].a, keys[k].b, keys[k].y);
            targets[k] = ToWorld(keys[k].ta, keys[k].tb, keys[k].ty);
            fovs[k] = keys[k].fov;
        }
        var rig = new GameObject(Primitives.CameraBody(), positions[0]) { Name = "Cinematic camera", Color = new Color4(0.2f, 0.22f, 0.26f, 1f), Collision = CollisionMode.None, CastShadow = false };
        rig.AddScript(new CinematicCamera { Positions = positions, Targets = targets, Fovs = fovs, Duration = 96f });
        scene.Add(rig);

        BuildUi(window.GameUi);
    }

    private static void BuildUi(UiCanvas ui)
    {
        const float bar = 70f;
        ui.Add(new UiElement(UiKind.Panel) { Name = "BarTop", Anchor = UiAnchor.Top, Size = new Vector2(4000f, bar), Background = Color4.Black, CornerRadius = 0f });
        ui.Add(new UiElement(UiKind.Panel) { Name = "BarBottom", Anchor = UiAnchor.Bottom, Size = new Vector2(4000f, bar), Background = Color4.Black, CornerRadius = 0f });
        ui.Add(new UiElement(UiKind.Label) { Name = "Title", Text = "GOLDEN HOUR", Anchor = UiAnchor.Center, Offset = new Vector2(0f, -14f), Size = new Vector2(900f, 60f), FontSize = 46f, Bold = true, Align = GT67.Gui.TextAlign.Center, TextColor = new Color4(1f, 0.93f, 0.8f, 0f) });
        ui.Add(new UiElement(UiKind.Label) { Name = "Subtitle", Text = "a GT67 cinematic", Anchor = UiAnchor.Center, Offset = new Vector2(0f, 30f), Size = new Vector2(900f, 30f), FontSize = 18f, Align = GT67.Gui.TextAlign.Center, TextColor = new Color4(1f, 0.93f, 0.8f, 0f) });
        ui.Add(new UiElement(UiKind.Panel) { Name = "Fade", Anchor = UiAnchor.Stretch, Background = Color4.Black, CornerRadius = 0f });
    }
}

/// <summary>Flies the camera along a smooth path, looking at a second path, with a little handheld shake and automatic focus.</summary>
public sealed class CinematicCamera : Script
{
    public Vector3[] Positions = Array.Empty<Vector3>();
    public Vector3[] Targets = Array.Empty<Vector3>();
    public float[] Fovs = Array.Empty<float>();

    /// <summary>Seconds for the whole flight; it fades out and starts again.</summary>
    public float Duration = 90f;

    /// <summary>How far the camera stays above the ground and the waves (m).</summary>
    public float Clearance = 1.6f;

    /// <summary>Where in the flight (seconds) it starts; "--cine-time" sets it.</summary>
    public static float StartTime;

    private float _focus = -1f;
    private UiElement? _fade, _title, _subtitle;

    public override void OnStart()
    {
        Self.Visible = false;
        Game.FreeCamera = false;
        Game.Camera.Far = 4000f;
        _fade = Game.Ui.Find("Fade");
        _title = Game.Ui.Find("Title");
        _subtitle = Game.Ui.Find("Subtitle");
    }

    private static Vector3 Spline(Vector3[] p, float u)
    {
        int n = p.Length;
        float f = Math.Clamp(u, 0f, 1f) * (n - 1);
        int i = Math.Min((int)f, n - 2);
        float t = f - i;
        var p0 = p[Math.Max(i - 1, 0)];
        var p1 = p[i];
        var p2 = p[i + 1];
        var p3 = p[Math.Min(i + 2, n - 1)];
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private static float Fade(float t, float duration)
    {
        float inn = Math.Clamp(t / 3f, 0f, 1f), outt = Math.Clamp((duration - t) / 3.5f, 0f, 1f);
        return MathF.Min(inn, outt);
    }

    public override void OnLateUpdate(float deltaTime)
    {
        if (Positions.Length < 2 || Game.ViewDetached) return;

        float time = (Game.Time + StartTime) % Duration;
        float u = time / Duration;
        u = u * u * (3f - 2f * u) * 0.35f + u * 0.65f;   // eases in and out of the flight a little

        var position = Spline(Positions, u);
        var target = Spline(Targets, u);
        float fov = Spline(Fovs.Select(f => new Vector3(f, 0f, 0f)).ToArray(), u).X;

        // handheld: slow drifting offsets, never more than a few centimetres
        float s = Game.Time;
        position += new Vector3(MathF.Sin(s * 0.9f) * 0.05f + MathF.Sin(s * 2.3f) * 0.02f, MathF.Sin(s * 1.1f + 1f) * 0.05f, MathF.Cos(s * 0.7f) * 0.05f);
        target += new Vector3(MathF.Sin(s * 0.6f) * 0.35f, MathF.Sin(s * 0.8f + 2f) * 0.25f, MathF.Cos(s * 0.5f) * 0.35f);

        float floor = MathF.Max(Game.GroundHeight(position.X, position.Z, -100f), Game.WaterAt(position)?.GetHeightAt(position.X, position.Z) ?? 0f);
        position.Y = MathF.Max(position.Y, floor + Clearance);

        var camera = Game.Camera;
        camera.Position = position;
        camera.LookAt(target);
        camera.Fov = fov;
        Self.SetPosition(position.X, position.Y, position.Z);

        // focus on whatever the camera looks at
        float dist = (target - position).Length;
        _focus = _focus < 0f ? dist : _focus + (dist - _focus) * MathF.Min(1f, Game.UnscaledDeltaTime * 1.5f);
        if (Game.GetEffect<DepthOfFieldEffect>() is { } dof)
        {
            dof.FocusTarget = null;
            dof.FocusDistance = _focus;
            dof.FocusRange = MathF.Max(20f, _focus * 0.9f);
        }

        // fade in/out, title card
        float fade = Fade(time, Duration);
        if (_fade != null) _fade.Background = new Color4(0f, 0f, 0f, 1f - fade);
        float card = Math.Clamp((time - 2.5f) / 1.5f, 0f, 1f) * Math.Clamp((9f - time) / 1.5f, 0f, 1f);
        if (_title != null) _title.TextColor = new Color4(1f, 0.93f, 0.8f, card);
        if (_subtitle != null) _subtitle.TextColor = new Color4(1f, 0.93f, 0.8f, card * 0.85f);
    }
}
