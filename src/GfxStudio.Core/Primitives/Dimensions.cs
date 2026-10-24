namespace GfxStudio.Core.Primitives;

/// <summary>Represents a positive width and height in pixels.</summary>
public readonly record struct Dimensions
{
    public Dimensions(int Width, int Height)
    {
        if (Width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), Width, "Width must be positive.");
        }

        if (Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Height), Height, "Height must be positive.");
        }

        this.Width = Width;
        this.Height = Height;
    }

    public int Width { get; }

    public int Height { get; }
}
