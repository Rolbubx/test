using GT67.Core;
using GT67.Gui;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Editor;

public sealed partial class MapEditor
{
    private enum BrushKind { Raise, Lower, Smooth, Flatten, Noise, Paint }

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
        }
        else if (_stroking)
        {
            EndStroke();
        }
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

    private void DrawTerrainPanel()
    {
        var g = G;
        var terrain = ActiveTerrain();
        if (Tool != EditorTool.Terrain)
        {
            if (g.IconTextButton(IconKind.Terrain, "Start sculpting")) Tool = EditorTool.Terrain;
            g.TextDim("Or press T. Left-drag on the terrain to paint with the brush.");
        }

        if (terrain == null)
        {
            g.TextWrapped("There is no terrain yet. Create one to sculpt and paint.", dim: true);
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

        g.SeparatorText("New terrain");
        g.Drag("Size (m)", _newTerrainSize, 1f, v => _newTerrainSize = v, 10f, 1000f);
        int resolution = _newTerrainResolution;
        if (g.SliderInt("Resolution", ref resolution, 32, 384)) _newTerrainResolution = resolution;
        g.TextDim($"{(_newTerrainResolution + 1) * (_newTerrainResolution + 1):N0} vertices");
        if (g.Button(Scene.Terrains.Count > 0 ? "Replace terrain" : "Create terrain", 0f, ButtonStyle.Primary))
        {
            foreach (var old in Scene.Terrains.ToList())
            {
                Scene.Remove(old);
                old.Dispose();
            }
            var fresh = new Terrain(_newTerrainSize, _newTerrainSize, _newTerrainResolution);
            Scene.Add(fresh);
            Selected = fresh.Object;
            Say($"Created a {_newTerrainSize:0} m terrain.");
        }
    }

    private void DrawSwatch(string name, Color4 color)
    {
        var g = G;
        if (g.ColorSwatch(name, color, 38f, 24f, _paintColor == color)) _paintColor = color;
        g.Tooltip(name);
    }
}
