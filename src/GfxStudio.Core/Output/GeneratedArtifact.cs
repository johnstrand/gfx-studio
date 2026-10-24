using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Output;

/// <summary>An encoded image artifact and its rendered pixel bitmap.</summary>
public sealed class GeneratedArtifact(
    string filePath,
    SKBitmap bitmap,
    byte[] encodedBytes,
    Dimensions dimensions) : IDisposable
{
    public string FilePath { get; } = filePath;

    public SKBitmap Bitmap { get; } = bitmap;

    public byte[] EncodedBytes { get; } = encodedBytes;

    public Dimensions Dimensions { get; } = dimensions;

    public void Dispose() => Bitmap.Dispose();
}
