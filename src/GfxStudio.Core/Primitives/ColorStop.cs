namespace GfxStudio.Core.Primitives;

/// <summary>A color and its normalized position in a color ramp.</summary>
public readonly record struct ColorStop
{
    public ColorStop(float Position, ColorRgba Color)
    {
        if (!float.IsFinite(Position) || Position < 0f || Position > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(Position), Position, "Position must be finite and between 0 and 1.");
        }

        this.Position = Position;
        this.Color = Color;
    }

    public float Position { get; }

    public ColorRgba Color { get; }
}
