using SkiaSharp;

namespace GT67.Gui;

public enum IconKind
{
    None, Play, Stop, Pause, Gear, Compile, Select, Move, Rotate, Scale, Terrain, Light, Water, Cube, Sphere, Folder, File,
    Trash, Eye, EyeOff, Plus, Minus, Close, Check, ChevronRight, ChevronDown, ChevronUp, Search, Layout, Code, Camera,
    Cloud, Image, Text, Button, Slider, Checkbox, TextBox, Panel, ProgressBar, Copy, ArrowUp, ArrowDown, Refresh,
}

/// <summary>Vector icons drawn with Skia paths in a square (so they scale and stay sharp at any size).</summary>
public static class Icons
{
    /// <summary>Draws <paramref name="kind"/> inside <paramref name="rect"/> (uses the smaller side, centred).</summary>
    public static void Draw(SKCanvas canvas, IconKind kind, UiRect rect, SKColor color, float strokeWidth = 1.6f)
    {
        if (kind == IconKind.None) return;

        float size = MathF.Min(rect.Width, rect.Height);
        float ox = rect.CenterX - size * 0.5f, oy = rect.CenterY - size * 0.5f;
        SKPoint P(float x, float y) => new(ox + x * size, oy + y * size);

        using var stroke = new SKPaint
        {
            IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };
        using var fill = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Fill };

        void Line(float x1, float y1, float x2, float y2) => canvas.DrawLine(P(x1, y1), P(x2, y2), stroke);
        void Circle(float x, float y, float r, SKPaint paint) => canvas.DrawCircle(P(x, y), r * size, paint);

        SKPath Poly(bool close, params float[] xy)
        {
            var path = new SKPath();
            path.MoveTo(P(xy[0], xy[1]));
            for (int i = 2; i < xy.Length; i += 2) path.LineTo(P(xy[i], xy[i + 1]));
            if (close) path.Close();
            return path;
        }

        switch (kind)
        {
            case IconKind.Play:
                using (var p = Poly(true, 0.3f, 0.18f, 0.3f, 0.82f, 0.82f, 0.5f)) canvas.DrawPath(p, fill);
                break;
            case IconKind.Stop:
                canvas.DrawRoundRect(new SKRect(P(0.24f, 0.24f).X, P(0.24f, 0.24f).Y, P(0.76f, 0.76f).X, P(0.76f, 0.76f).Y), 0.08f * size, 0.08f * size, fill);
                break;
            case IconKind.Pause:
                canvas.DrawRect(new SKRect(P(0.28f, 0.2f).X, P(0.28f, 0.2f).Y, P(0.42f, 0.8f).X, P(0.42f, 0.8f).Y), fill);
                canvas.DrawRect(new SKRect(P(0.58f, 0.2f).X, P(0.58f, 0.2f).Y, P(0.72f, 0.8f).X, P(0.72f, 0.8f).Y), fill);
                break;
            case IconKind.Gear:
                Circle(0.5f, 0.5f, 0.17f, stroke);
                for (int i = 0; i < 8; i++)
                {
                    float a = MathF.PI * 2f * i / 8f;
                    Line(0.5f + MathF.Cos(a) * 0.29f, 0.5f + MathF.Sin(a) * 0.29f, 0.5f + MathF.Cos(a) * 0.4f, 0.5f + MathF.Sin(a) * 0.4f);
                }
                Circle(0.5f, 0.5f, 0.29f, stroke);
                break;
            case IconKind.Compile:
                using (var p = Poly(true, 0.58f, 0.1f, 0.26f, 0.56f, 0.47f, 0.56f, 0.4f, 0.9f, 0.76f, 0.4f, 0.54f, 0.4f)) canvas.DrawPath(p, fill);
                break;
            case IconKind.Select:
                using (var p = Poly(true, 0.28f, 0.16f, 0.28f, 0.82f, 0.45f, 0.66f, 0.57f, 0.89f, 0.67f, 0.84f, 0.55f, 0.62f, 0.76f, 0.6f)) canvas.DrawPath(p, fill);
                break;
            case IconKind.Move:
                Line(0.5f, 0.12f, 0.5f, 0.88f); Line(0.12f, 0.5f, 0.88f, 0.5f);
                Line(0.5f, 0.12f, 0.4f, 0.24f); Line(0.5f, 0.12f, 0.6f, 0.24f);
                Line(0.5f, 0.88f, 0.4f, 0.76f); Line(0.5f, 0.88f, 0.6f, 0.76f);
                Line(0.12f, 0.5f, 0.24f, 0.4f); Line(0.12f, 0.5f, 0.24f, 0.6f);
                Line(0.88f, 0.5f, 0.76f, 0.4f); Line(0.88f, 0.5f, 0.76f, 0.6f);
                break;
            case IconKind.Rotate:
                using (var p = new SKPath())
                {
                    p.AddArc(new SKRect(P(0.18f, 0.18f).X, P(0.18f, 0.18f).Y, P(0.82f, 0.82f).X, P(0.82f, 0.82f).Y), -60f, 280f);
                    canvas.DrawPath(p, stroke);
                }
                Line(0.66f, 0.22f, 0.8f, 0.2f); Line(0.8f, 0.2f, 0.78f, 0.34f);
                break;
            case IconKind.Scale:
                canvas.DrawRoundRect(new SKRect(P(0.16f, 0.4f).X, P(0.16f, 0.4f).Y, P(0.6f, 0.84f).X, P(0.6f, 0.84f).Y), 0.05f * size, 0.05f * size, stroke);
                Line(0.46f, 0.54f, 0.84f, 0.16f);
                Line(0.58f, 0.16f, 0.84f, 0.16f); Line(0.84f, 0.16f, 0.84f, 0.42f);
                break;
            case IconKind.Terrain:
                using (var p = Poly(true, 0.1f, 0.78f, 0.38f, 0.28f, 0.55f, 0.55f, 0.68f, 0.38f, 0.9f, 0.78f)) canvas.DrawPath(p, stroke);
                break;
            case IconKind.Light:
                Circle(0.5f, 0.5f, 0.17f, stroke);
                for (int i = 0; i < 8; i++)
                {
                    float a = MathF.PI * 2f * i / 8f;
                    Line(0.5f + MathF.Cos(a) * 0.28f, 0.5f + MathF.Sin(a) * 0.28f, 0.5f + MathF.Cos(a) * 0.4f, 0.5f + MathF.Sin(a) * 0.4f);
                }
                break;
            case IconKind.Water:
                using (var p = new SKPath())
                {
                    p.MoveTo(P(0.5f, 0.1f));
                    p.CubicTo(P(0.5f, 0.1f), P(0.2f, 0.45f), P(0.2f, 0.62f));
                    p.CubicTo(P(0.2f, 0.8f), P(0.34f, 0.9f), P(0.5f, 0.9f));
                    p.CubicTo(P(0.66f, 0.9f), P(0.8f, 0.8f), P(0.8f, 0.62f));
                    p.CubicTo(P(0.8f, 0.45f), P(0.5f, 0.1f), P(0.5f, 0.1f));
                    canvas.DrawPath(p, stroke);
                }
                break;
            case IconKind.Cube:
                using (var p = Poly(true, 0.5f, 0.1f, 0.84f, 0.3f, 0.84f, 0.7f, 0.5f, 0.9f, 0.16f, 0.7f, 0.16f, 0.3f)) canvas.DrawPath(p, stroke);
                Line(0.16f, 0.3f, 0.5f, 0.5f); Line(0.84f, 0.3f, 0.5f, 0.5f); Line(0.5f, 0.5f, 0.5f, 0.9f);
                break;
            case IconKind.Sphere:
                Circle(0.5f, 0.5f, 0.36f, stroke);
                using (var p = new SKPath())
                {
                    p.AddOval(new SKRect(P(0.14f, 0.38f).X, P(0.14f, 0.38f).Y, P(0.86f, 0.62f).X, P(0.86f, 0.62f).Y));
                    canvas.DrawPath(p, stroke);
                }
                break;
            case IconKind.Folder:
                using (var p = Poly(true, 0.1f, 0.24f, 0.4f, 0.24f, 0.48f, 0.34f, 0.9f, 0.34f, 0.9f, 0.8f, 0.1f, 0.8f)) canvas.DrawPath(p, stroke);
                break;
            case IconKind.File:
                using (var p = Poly(true, 0.22f, 0.1f, 0.58f, 0.1f, 0.8f, 0.32f, 0.8f, 0.9f, 0.22f, 0.9f)) canvas.DrawPath(p, stroke);
                Line(0.58f, 0.1f, 0.58f, 0.32f); Line(0.58f, 0.32f, 0.8f, 0.32f);
                break;
            case IconKind.Trash:
                Line(0.18f, 0.27f, 0.82f, 0.27f); Line(0.4f, 0.27f, 0.4f, 0.16f); Line(0.4f, 0.16f, 0.6f, 0.16f); Line(0.6f, 0.16f, 0.6f, 0.27f);
                using (var p = Poly(false, 0.26f, 0.27f, 0.3f, 0.86f, 0.7f, 0.86f, 0.74f, 0.27f)) canvas.DrawPath(p, stroke);
                Line(0.44f, 0.4f, 0.44f, 0.74f); Line(0.56f, 0.4f, 0.56f, 0.74f);
                break;
            case IconKind.Eye:
            case IconKind.EyeOff:
                using (var p = new SKPath())
                {
                    p.MoveTo(P(0.08f, 0.5f));
                    p.QuadTo(P(0.5f, 0.12f), P(0.92f, 0.5f));
                    p.QuadTo(P(0.5f, 0.88f), P(0.08f, 0.5f));
                    canvas.DrawPath(p, stroke);
                }
                Circle(0.5f, 0.5f, 0.12f, kind == IconKind.Eye ? fill : stroke);
                if (kind == IconKind.EyeOff) Line(0.2f, 0.82f, 0.8f, 0.18f);
                break;
            case IconKind.Plus: Line(0.5f, 0.2f, 0.5f, 0.8f); Line(0.2f, 0.5f, 0.8f, 0.5f); break;
            case IconKind.Minus: Line(0.2f, 0.5f, 0.8f, 0.5f); break;
            case IconKind.Close: Line(0.24f, 0.24f, 0.76f, 0.76f); Line(0.76f, 0.24f, 0.24f, 0.76f); break;
            case IconKind.Check: Line(0.2f, 0.52f, 0.42f, 0.74f); Line(0.42f, 0.74f, 0.8f, 0.28f); break;
            case IconKind.ChevronRight: Line(0.36f, 0.2f, 0.64f, 0.5f); Line(0.64f, 0.5f, 0.36f, 0.8f); break;
            case IconKind.ChevronDown: Line(0.2f, 0.36f, 0.5f, 0.64f); Line(0.5f, 0.64f, 0.8f, 0.36f); break;
            case IconKind.ChevronUp: Line(0.2f, 0.64f, 0.5f, 0.36f); Line(0.5f, 0.36f, 0.8f, 0.64f); break;
            case IconKind.ArrowUp: Line(0.5f, 0.84f, 0.5f, 0.2f); Line(0.5f, 0.2f, 0.28f, 0.42f); Line(0.5f, 0.2f, 0.72f, 0.42f); break;
            case IconKind.ArrowDown: Line(0.5f, 0.16f, 0.5f, 0.8f); Line(0.5f, 0.8f, 0.28f, 0.58f); Line(0.5f, 0.8f, 0.72f, 0.58f); break;
            case IconKind.Search:
                Circle(0.44f, 0.44f, 0.24f, stroke); Line(0.62f, 0.62f, 0.84f, 0.84f);
                break;
            case IconKind.Layout:
                canvas.DrawRoundRect(new SKRect(P(0.12f, 0.16f).X, P(0.12f, 0.16f).Y, P(0.88f, 0.84f).X, P(0.88f, 0.84f).Y), 0.07f * size, 0.07f * size, stroke);
                Line(0.12f, 0.36f, 0.88f, 0.36f); Line(0.38f, 0.36f, 0.38f, 0.84f);
                break;
            case IconKind.Code:
                Line(0.34f, 0.26f, 0.12f, 0.5f); Line(0.12f, 0.5f, 0.34f, 0.74f);
                Line(0.66f, 0.26f, 0.88f, 0.5f); Line(0.88f, 0.5f, 0.66f, 0.74f);
                Line(0.56f, 0.2f, 0.44f, 0.8f);
                break;
            case IconKind.Camera:
                canvas.DrawRoundRect(new SKRect(P(0.1f, 0.28f).X, P(0.1f, 0.28f).Y, P(0.9f, 0.8f).X, P(0.9f, 0.8f).Y), 0.08f * size, 0.08f * size, stroke);
                Circle(0.5f, 0.54f, 0.14f, stroke); Line(0.36f, 0.28f, 0.42f, 0.2f); Line(0.42f, 0.2f, 0.58f, 0.2f); Line(0.58f, 0.2f, 0.64f, 0.28f);
                break;
            case IconKind.Cloud:
                using (var p = new SKPath())
                {
                    p.MoveTo(P(0.26f, 0.72f));
                    p.CubicTo(P(0.06f, 0.72f), P(0.06f, 0.44f), P(0.28f, 0.44f));
                    p.CubicTo(P(0.3f, 0.2f), P(0.7f, 0.16f), P(0.74f, 0.44f));
                    p.CubicTo(P(0.94f, 0.44f), P(0.94f, 0.72f), P(0.74f, 0.72f));
                    p.Close();
                    canvas.DrawPath(p, stroke);
                }
                break;
            case IconKind.Image:
                canvas.DrawRoundRect(new SKRect(P(0.12f, 0.18f).X, P(0.12f, 0.18f).Y, P(0.88f, 0.82f).X, P(0.82f, 0.82f).Y), 0.06f * size, 0.06f * size, stroke);
                using (var p = Poly(false, 0.12f, 0.72f, 0.38f, 0.46f, 0.58f, 0.64f, 0.7f, 0.54f, 0.88f, 0.74f)) canvas.DrawPath(p, stroke);
                Circle(0.7f, 0.36f, 0.06f, fill);
                break;
            case IconKind.Text:
                Line(0.22f, 0.22f, 0.78f, 0.22f); Line(0.5f, 0.22f, 0.5f, 0.82f);
                break;
            case IconKind.Button:
                canvas.DrawRoundRect(new SKRect(P(0.1f, 0.3f).X, P(0.1f, 0.3f).Y, P(0.9f, 0.7f).X, P(0.9f, 0.7f).Y), 0.12f * size, 0.12f * size, stroke);
                Line(0.3f, 0.5f, 0.7f, 0.5f);
                break;
            case IconKind.Slider:
                Line(0.1f, 0.5f, 0.9f, 0.5f); Circle(0.62f, 0.5f, 0.12f, fill);
                break;
            case IconKind.Checkbox:
                canvas.DrawRoundRect(new SKRect(P(0.18f, 0.18f).X, P(0.18f, 0.18f).Y, P(0.82f, 0.82f).X, P(0.82f, 0.82f).Y), 0.1f * size, 0.1f * size, stroke);
                Line(0.32f, 0.52f, 0.46f, 0.66f); Line(0.46f, 0.66f, 0.7f, 0.36f);
                break;
            case IconKind.TextBox:
                canvas.DrawRoundRect(new SKRect(P(0.08f, 0.3f).X, P(0.08f, 0.3f).Y, P(0.92f, 0.7f).X, P(0.92f, 0.7f).Y), 0.08f * size, 0.08f * size, stroke);
                Line(0.26f, 0.4f, 0.26f, 0.6f);
                break;
            case IconKind.Panel:
                canvas.DrawRoundRect(new SKRect(P(0.12f, 0.16f).X, P(0.12f, 0.16f).Y, P(0.88f, 0.84f).X, P(0.88f, 0.84f).Y), 0.08f * size, 0.08f * size, stroke);
                break;
            case IconKind.ProgressBar:
                canvas.DrawRoundRect(new SKRect(P(0.08f, 0.38f).X, P(0.08f, 0.38f).Y, P(0.92f, 0.62f).X, P(0.92f, 0.62f).Y), 0.1f * size, 0.1f * size, stroke);
                canvas.DrawRoundRect(new SKRect(P(0.08f, 0.38f).X, P(0.08f, 0.38f).Y, P(0.56f, 0.62f).X, P(0.56f, 0.62f).Y), 0.1f * size, 0.1f * size, fill);
                break;
            case IconKind.Copy:
                canvas.DrawRoundRect(new SKRect(P(0.3f, 0.3f).X, P(0.3f, 0.3f).Y, P(0.84f, 0.88f).X, P(0.84f, 0.88f).Y), 0.06f * size, 0.06f * size, stroke);
                using (var p = Poly(false, 0.2f, 0.7f, 0.16f, 0.7f, 0.16f, 0.12f, 0.64f, 0.12f, 0.64f, 0.2f)) canvas.DrawPath(p, stroke);
                break;
            case IconKind.Refresh:
                using (var p = new SKPath())
                {
                    p.AddArc(new SKRect(P(0.18f, 0.18f).X, P(0.18f, 0.18f).Y, P(0.82f, 0.82f).X, P(0.82f, 0.82f).Y), 20f, 300f);
                    canvas.DrawPath(p, stroke);
                }
                Line(0.8f, 0.2f, 0.8f, 0.4f); Line(0.8f, 0.4f, 0.6f, 0.4f);
                break;
        }
    }
}
