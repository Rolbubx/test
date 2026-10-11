using GT67.Core;
using GT67.Gui;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Editor;

public sealed partial class MapEditor
{
    private enum BrushKind { Raise, Lower, Smooth, Flatten, Noise, Paint, Grass }

    private float _grassDensity = 40f;
    private readonly Random _grassRandom = new();

    private static readonly (string name, Color4 color)[] PaintSwatches =
    {
        ("Grass", new Color4(0.36f, 0.5f, 0.24f, 1f)), ("Dirt", new Color4(0.45f, 0.33f, 0.2f, 1f)),
        ("Rock", new Color4(0.5f, 0.5f, 0.52f, 1f)), ("Sand", new Color4(0.82f, 0.72f, 0.5f, 1f)),
        ("Snow", new Color4(0.95f, 0.96f, 1f, 1f)),
    };

    private BrushKind _brush = BrushKind.Raise;
    private float _brushRadius = 6f, _brushStrength = 6f, _paintStrength = 3f;
    private Color4 _paintColor = PaintSwatches[1].color;
    private float _newTerrainSize = 80f;
    private int _newTerrainResolution = 160;
    private bool _stroking;
    private float _flattenHeight;
    private Terrain? _strokeTerrain;

    private void HandleTerrain(float deltaTime, bool overUi, bool leftDown)
    {
        if (_brush == BrushKind.Grass)
        {
            HandleGrass(deltaTime, overUi, leftDown);
            return;
        }
        var terrain = ActiveTerrain();
        if (terrain == null)
        {
            _ring.Hide();
            return;
        }

        var ray = _window.GetMouseRay();
        bool hit = !overUi && terrain.Raycast(ray, out _);
        if (!hit || !terrain.Raycast(ray, out var point))
        {
            _ring.Hide();
            if (_stroking && !leftDown) EndStroke();
            return;
        }

        _ring.Show(terrain, point, _brushRadius);

        if (leftDown)
        {
            if (!_stroking)
            {
                _stroking = true;
                _strokeTerrain = terrain;
                _flattenHeight = point.Y;
            }

            bool invert = _window.KeyboardState.IsKeyDown(Keys.LeftShift);
            switch (_brush)
            {
                case BrushKind.Raise:
                    terrain.Sculpt(invert ? TerrainBrush.Lower : TerrainBrush.Raise, point, _brushRadius, _brushStrength, deltaTime);
                    break;
                case BrushKind.Lower:
                    terrain.Sculpt(invert ? TerrainBrush.Raise : TerrainBrush.Lower, point, _brushRadius, _brushStrength, deltaTime);
                    break;
                case BrushKind.Smooth:
                    terrain.Sculpt(TerrainBrush.Smooth, point, _brushRadius, _brushStrength, deltaTime);
                    break;
                case BrushKind.Flatten:
                    terrain.Sculpt(TerrainBrush.Flatten, point, _brushRadius, _brushStrength, deltaTime, _flattenHeight);
                    break;
                case BrushKind.Noise:
                    terrain.Sculpt(TerrainBrush.Noise, point, _brushRadius, _brushStrength, deltaTime);
                    break;
                case BrushKind.Paint:
                    terrain.Paint(point, _brushRadius, _paintColor, _paintStrength, deltaTime);
                    break;
            }
            terrain.Apply();
            // Grass stays on the ground while it is sculpted.
            if (_brush != BrushKind.Paint) Scene.Grass?.Reground(point.Xz, _brushRadius, (x, z) => terrain.GetHeight(x, z));
        }
        else if (_stroking)
        {
            EndStroke();
        }
    }

    /// <summary>
    /// The Grass brush: paints blades up to the chosen density on whatever ground is under the mouse (terrain or any
    /// solid object); Shift erases. No grass grows under water.
    /// </summary>
    private void HandleGrass(float deltaTime, bool overUi, bool leftDown)
    {
        var ray = _window.GetMouseRay();
        Vector3 point;
        if (overUi) { _ring.Hide(); return; }
        if (ActiveTerrain() is { } terrain && terrain.Raycast(ray, out var onTerrain)) point = onTerrain;
        else if (Scene.Raycast(ray.Origin, ray.Direction, out float distance) != null) point = ray.GetPoint(distance);
        else { _ring.Hide(); return; }

        _ring.Show((x, z) => GrassGround(x, z) ?? point.Y, point, _brushRadius);
        if (!leftDown) return;

        var grass = Scene.Grass ??= new GrassLayer();
        bool erase = _window.KeyboardState.IsKeyDown(Keys.LeftShift) || _window.KeyboardState.IsKeyDown(Keys.RightShift);
        if (erase) grass.Erase(point.Xz, _brushRadius, Math.Clamp(deltaTime * 4f, 0f, 1f), _grassRandom);
        else
        {
            // On a terrain the height lookup is cheap; on other objects every blade needs a ray, so fewer per frame.
            int perFrame = TerrainUnder(point) != null ? 600 : 80;
            grass.Paint(point.Xz, _brushRadius, _grassDensity, GrassGround, _grassRandom, perFrame);
        }
    }

    /// <summary>The ground height where a blade could grow at (x, z), or null (nothing there, or under water).</summary>
    private float? GrassGround(float x, float z)
    {
        float? ground = null;
        if (TerrainUnder(new Vector3(x, 0f, z)) is { } terrain) ground = terrain.GetHeight(x, z);
        else
        {
            var from = new Vector3(x, 500f, z);
            if (Scene.Raycast(from, -Vector3.UnitY, out float distance) != null) ground = from.Y - distance;
        }
        if (ground is { } y && Scene.WaterAt(new Vector3(x, y, z)) is { } water && water.Level > y - 0.02f) return null;
        return ground;
    }

    private void EndStroke()
    {
        _stroking = false;
        _strokeTerrain?.FinishEdit(); // rebuild collision once, not every frame
        _strokeTerrain = null;
    }

    private Terrain? ActiveTerrain() =>
        Scene.Terrains.FirstOrDefault(t => t.Object == Selected) ?? Scene.Terrains.FirstOrDefault();

    // ---- panel -----------------------------------------------------------------------------------

    /// <summary>Grass: the brush (works on any ground, terrain or not), how the blades look and move.</summary>
    private void DrawGrassSection()
    {
        var g = G;
        g.SeparatorText("Grass");
        bool painting = Tool == EditorTool.Terrain && _brush == BrushKind.Grass;
        if (g.IconTextButton(IconKind.Terrain, painting ? "Painting grass" : "Paint grass", 0f, painting ? ButtonStyle.Primary : ButtonStyle.Normal))
        {
            Tool = EditorTool.Terrain;
            _brush = BrushKind.Grass;
        }
        g.TextWrapped("Left-drag on any ground to grow grass up to the density below; hold Shift to remove it. " +
                      "It never grows under water, and follows the terrain when you sculpt.", dim: true);
        g.Slider("Brush radius (m)", _brushRadius, 0.5f, 40f, v => _brushRadius = v, "0.0");
        g.Slider("Density (blades/m²)", _grassDensity, 2f, 150f, v => _grassDensity = v, "0");

        if (Scene.Grass is not { } grass) return;
        g.TextDim($"{grass.Count:N0} blades");
        g.Check("Visible", grass.Enabled, v => grass.Enabled = v);
        g.Slider("Height (m)", grass.Height, 0.05f, 2f, v => grass.Height = v);
        g.Slider("Blade width (m)", grass.Width, 0.01f, 0.25f, v => grass.Width = v, "0.000");
        g.Rgb("Root colour", grass.BaseColor, v => grass.BaseColor = v);
        g.Rgb("Tip colour", grass.TipColor, v => grass.TipColor = v);
        g.Slider("Wind strength", grass.WindStrength, 0f, 1.5f, v => grass.WindStrength = v);
        g.Slider("Wind speed", grass.WindSpeed, 0f, 6f, v => grass.WindSpeed = v);
        g.Slider("Draw distance (m)", grass.MaxDistance, 10f, 300f, v => grass.MaxDistance = v, "0");
        g.Slider("Flowers", grass.Flowers, 0f, 0.2f, v => grass.Flowers = v, "0.000");
        g.Slider("Player bend radius", grass.PlayerBendRadius, 0f, 3f, v => grass.PlayerBendRadius = v);
        if (g.IconTextButton(IconKind.Trash, "Remove all grass", 0f, ButtonStyle.Danger))
        {
            grass.Clear();
            Say("Removed all the grass.");
        }
    }

    /// <summary>The terrain tools, in the Inspector: while the Terrain brush (T) is active or a terrain is selected.</summary>
    private void DrawTerrainPanel()
    {
        var g = G;
        var terrain = ActiveTerrain();
        g.Heading("Terrain");
        if (Tool == EditorTool.Terrain)
        {
            if (g.IconTextButton(IconKind.Check, "Done", 100f)) Tool = EditorTool.Select;
            g.Tooltip("Back to the Select tool (Q).");
            g.SameLine();
            g.TextDim("Left-drag in the view to use the brush.");
        }
        else if (terrain != null)
        {
            if (g.IconTextButton(IconKind.Terrain, "Sculpt and paint (T)", 0f, ButtonStyle.Primary)) Tool = EditorTool.Terrain;
        }

        if (terrain == null)
        {
            g.TextWrapped("This scene has no terrain yet. Create one, then sculpt it, paint it and grow grass on it.", dim: true);
            DrawNewTerrain();
        }
        else
        {
            g.TextDim($"{terrain.Width:0} x {terrain.Depth:0} m, {terrain.Resolution} x {terrain.Resolution} cells");
            g.SeparatorText("Brush");
            var kinds = Enum.GetValues<BrushKind>();
            float cell = (g.AvailableWidth - 2f * g.Theme.ItemSpacing) / 3f;
            for (int i = 0; i < kinds.Length; i++)
            {
                if (i % 3 != 0) g.SameLine();
                if (g.Button(kinds[i].ToString(), cell, _brush == kinds[i] ? ButtonStyle.Primary : ButtonStyle.Normal))
                    _brush = kinds[i];
            }

            g.Spacing(2f);
            g.Slider("Radius (m)", _brushRadius, 0.5f, 40f, v => _brushRadius = v, "0.0");
            if (_brush == BrushKind.Paint)
            {
                g.Slider("Paint strength", _paintStrength, 0.2f, 10f, v => _paintStrength = v, "0.0");
                g.Text("Palette");
                foreach (var (name, color) in PaintSwatches)
                {
                    DrawSwatch(name, color);
                    g.SameLine();
                }
                g.NewLine();
                g.Rgb("Colour", _paintColor, v => _paintColor = v);
            }
            else if (_brush == BrushKind.Grass)
            {
                g.TextWrapped("Grass settings are below.", dim: true);
            }
            else
            {
                g.Slider("Strength", _brushStrength, 0.2f, 30f, v => _brushStrength = v, "0.0");
                g.TextWrapped("Hold Shift to invert Raise / Lower. Flatten levels to where you click.", dim: true);
            }

            g.SeparatorText("Terrain");
            g.Drag("Height offset", terrain.Object.Position.Y, 0.05f, v =>
            {
                terrain.Object.Position = terrain.Object.Position with { Y = v };
                terrain.FinishEdit();
            });
        }

        DrawGrassSection();

        if (terrain != null && g.CollapsingHeader("Replace with a new terrain", defaultOpen: false)) DrawNewTerrain();
    }

    private void DrawNewTerrain()
    {
        var g = G;
        g.SeparatorText("New terrain");
        g.Drag("Size (m)", _newTerrainSize, 1f, v => _newTerrainSize = v, 10f, 1000f);
        int resolution = _newTerrainResolution;
        if (g.SliderInt("Resolution", ref resolution, 32, 384)) _newTerrainResolution = resolution;
        g.TextDim($"{(_newTerrainResolution + 1) * (_newTerrainResolution + 1):N0} vertices");
        if (g.IconTextButton(IconKind.Terrain, Scene.Terrains.Count > 0 ? "Replace terrain" : "Create terrain", 0f, ButtonStyle.Primary))
            CreateTerrain();
    }

    /// <summary>A new flat terrain (replacing the scene's old one), selected and ready for the brush.</summary>
    private void CreateTerrain()
    {
        foreach (var old in Scene.Terrains.ToList())
        {
            Scene.Remove(old);
            old.Dispose();
        }
        var fresh = new Terrain(_newTerrainSize, _newTerrainSize, _newTerrainResolution) { Object = { Name = "Terrain" } };
        Scene.Add(fresh);
        Selected = fresh.Object;
        Tool = EditorTool.Terrain;
        Say($"Created a {_newTerrainSize:0} m terrain: left-drag in the view to sculpt it (brushes in the Inspector).");
    }

    private void DrawSwatch(string name, Color4 color)
    {
        var g = G;
        if (g.ColorSwatch(name, color, 38f, 24f, _paintColor == color)) _paintColor = color;
        g.Tooltip(name);
    }
}
