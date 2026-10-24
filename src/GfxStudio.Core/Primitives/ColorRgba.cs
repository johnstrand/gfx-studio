using SkiaSharp;

namespace GfxStudio.Core.Primitives;

/// <summary>Represents an 8-bit RGBA color.</summary>
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A = byte.MaxValue)
{
    public static ColorRgba Black { get; } = new(0, 0, 0);

    public static ColorRgba White { get; } = new(byte.MaxValue, byte.MaxValue, byte.MaxValue);

    /// <summary>Gets the red channel normalized to the range [0, 1].</summary>
    public float NormalizedR => R / (float)byte.MaxValue;

    /// <summary>Gets the green channel normalized to the range [0, 1].</summary>
    public float NormalizedG => G / (float)byte.MaxValue;

    /// <summary>Gets the blue channel normalized to the range [0, 1].</summary>
    public float NormalizedB => B / (float)byte.MaxValue;

    /// <summary>Gets the alpha channel normalized to the range [0, 1].</summary>
    public float NormalizedA => A / (float)byte.MaxValue;

    public SKColor ToSKColor() => new(R, G, B, A);

    public static ColorRgba FromSKColor(SKColor color) => new(color.Red, color.Green, color.Blue, color.Alpha);
}
