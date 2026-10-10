using OpenTK.Mathematics;
using SkiaSharp;

namespace GT67.Gui;

/// <summary>A rectangle in UI space (origin top-left, y grows downwards).</summary>
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public float CenterX => X + Width * 0.5f;
    public float CenterY => Y + Height * 0.5f;
    public Vector2 Center => new(CenterX, CenterY);
    public Vector2 Position => new(X, Y);
    public Vector2 Size => new(Width, Height);

    public bool Contains(Vector2 p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    /// <summary>Shrinks the rectangle by the given amounts on each side.</summary>
    public UiRect Deflate(float dx, float dy) => new(X + dx, Y + dy, MathF.Max(0f, Width - 2f * dx), MathF.Max(0f, Height - 2f * dy));

    public UiRect Deflate(float all) => Deflate(all, all);

    public UiRect Inflate(float all) => new(X - all, Y - all, Width + 2f * all, Height + 2f * all);

    public UiRect Offset(float dx, float dy) => new(X + dx, Y + dy, Width, Height);

    public UiRect WithHeight(float height) => this with { Height = height };
    public UiRect WithWidth(float width) => this with { Width = width };

    /// <summary>The overlapping part of two rectangles (empty if they do not touch).</summary>
    public static UiRect Intersect(UiRect a, UiRect b)
    {
        float x = MathF.Max(a.X, b.X), y = MathF.Max(a.Y, b.Y);
        float r = MathF.Min(a.Right, b.Right), bt = MathF.Min(a.Bottom, b.Bottom);
        return new UiRect(x, y, MathF.Max(0f, r - x), MathF.Max(0f, bt - y));
    }

    public SKRect ToSk() => new(X, Y, Right, Bottom);
}
