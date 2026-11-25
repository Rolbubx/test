using SkiaSharp;

namespace SolciteEngine.Gui;

public sealed partial class Gui
{
    /// <summary>
    /// Draws the SolciteEngine start-up screen over the whole <paramref name="area"/>: dark background, the three-facet logo
    /// (the top facet in solar orange), the name, a status line and a thin progress bar. <paramref name="fade"/> is 0..1 (1 = opaque).
    /// </summary>
    public void DrawSplash(UiRect area, string status, float progress, float fade = 1f)
    {
        var canvas = Canvas;
        byte A(int v) => (byte)Math.Clamp((int)(v * fade), 0, 255);
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = new SKColor(23, 24, 28, A(255));
        canvas.DrawRect(area.ToSk(), _paint);

        float s = MathF.Min(area.Width, area.Height) * 0.0021f;   // logo scale: the mark is 144 units tall
        float cx = area.CenterX, cy = area.CenterY - 46f * s - 10f;
        SKPath Poly(params float[] p)
        {
            var path = new SKPath();
            path.MoveTo(cx + p[0] * s, cy + p[1] * s);
            for (int i = 2; i < p.Length; i += 2) path.LineTo(cx + p[i] * s, cy + p[i + 1] * s);
            path.Close();
            return path;
        }
        using (var top = Poly(0, -72, 62, -36, 0, -4, -62, -36)) { _paint.Color = new SKColor(255, 138, 31, A(255)); canvas.DrawPath(top, _paint); }
        using (var left = Poly(-66, -28, -4, 6, -4, 76, -66, 40)) { _paint.Color = new SKColor(242, 243, 245, A(204)); canvas.DrawPath(left, _paint); }
        using (var right = Poly(66, -28, 66, 40, 4, 76, 4, 6)) { _paint.Color = new SKColor(242, 243, 245, A(128)); canvas.DrawPath(right, _paint); }

        float textTop = cy + 92f * s;
        DrawText(canvas, "S O L C I T E", new UiRect(area.X, textTop, area.Width, 40f), new SKColor(242, 243, 245, A(255)), TextAlign.Center, 30f, FontWeight.Bold);
        DrawText(canvas, "E N G I N E", new UiRect(area.X, textTop + 34f, area.Width, 24f), new SKColor(154, 160, 170, A(255)), TextAlign.Center, 14f, FontWeight.Medium);

        float barW = MathF.Min(360f, area.Width - 64f), barY = area.Bottom - 70f;
        var bar = new UiRect(area.CenterX - barW * 0.5f, barY, barW, 3f);
        _paint.Color = new SKColor(52, 57, 68, A(255));
        canvas.DrawRect(bar.ToSk(), _paint);
        _paint.Color = new SKColor(255, 138, 31, A(255));
        canvas.DrawRect(new UiRect(bar.X, bar.Y, bar.Width * Math.Clamp(progress, 0f, 1f), bar.Height).ToSk(), _paint);
        DrawText(canvas, status, new UiRect(bar.X, barY + 10f, barW, 20f), new SKColor(139, 146, 163, A(255)), TextAlign.Center, 12f);
    }
}
