using GfxStudio.Core.Output.Gif;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Output;

/// <summary>
/// Writes an animated GIF (<c>output.mode = animation_gif</c>) from a sequence of rendered <see cref="SKBitmap"/> frames.
/// </summary>
public sealed class AnimationGifOutputWriter
{
    public byte[] Encode(
        IReadOnlyList<SKBitmap> frames,
        double fps = 15.0,
        int? frameDelayMs = null,
        int loopCount = 0,
        bool dither = true)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var delayCentiseconds = GfxGifEncoder.ComputeDelayCentiseconds(fps, frameDelayMs);
        return GfxGifEncoder.Encode(frames, delayCentiseconds, loopCount, dither);
    }

    public GeneratedArtifact Write(
        IReadOnlyList<SKBitmap> frames,
        string fullPath,
        Dimensions dimensions,
        double fps = 15.0,
        int? frameDelayMs = null,
        int loopCount = 0,
        bool dither = true)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required to write an animated GIF.", nameof(frames));
        }

        var normalizedPath = Path.GetFullPath(fullPath);
        var bytes = Encode(frames, fps, frameDelayMs, loopCount, dither);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedPath)!);
        File.WriteAllBytes(normalizedPath, bytes);

        var representativeBitmap = frames[0].Copy()
            ?? throw new InvalidOperationException("Could not copy representative frame bitmap.");
        return new GeneratedArtifact(normalizedPath, representativeBitmap, bytes, dimensions);
    }
}
