using FluentAssertions;
using GfxStudio.Core.Output;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Tests.Golden;

/// <summary>
/// Compares rendered bitmaps against committed golden baseline images, generating highlighted visual diffs on mismatch.
/// </summary>
public static class GoldenImageAssert
{
    private static readonly string BaselinesDirectory = FindBaselinesDirectory();
    private static readonly string DiffsDirectory = Path.Combine(FindRepositoryRoot(), "TestResults", "GoldenDiffs");
    private static readonly SingleImageOutputWriter ImageWriter = new();

    public static void MatchesBaseline(SKBitmap actual, string baselineName, double maxChannelDelta = 0.0)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineName);

        Directory.CreateDirectory(BaselinesDirectory);
        var baselinePath = Path.Combine(BaselinesDirectory, $"{baselineName}.png");
        var shouldUpdate = string.Equals(Environment.GetEnvironmentVariable("GFX_UPDATE_GOLDEN"), "1", StringComparison.OrdinalIgnoreCase);

        if (shouldUpdate || !File.Exists(baselinePath))
        {
            ImageWriter.Write(actual, baselinePath);
            return;
        }

        using var expectedStream = File.OpenRead(baselinePath);
        using var expectedCodec = SKCodec.Create(expectedStream)
            ?? throw new InvalidOperationException($"Could not load golden baseline image '{baselinePath}'.");
        using var expected = new SKBitmap(expectedCodec.Info.Width, expectedCodec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        expectedCodec.GetPixels(expected.Info, expected.GetPixels());

        if (actual.Width != expected.Width || actual.Height != expected.Height)
        {
            WriteDiffArtifacts(actual, expected, baselineName, null);
            throw new AssertionFailedException($"Golden baseline dimension mismatch for '{baselineName}'. Expected {expected.Width}x{expected.Height}, got {actual.Width}x{actual.Height}.");
        }

        var width = actual.Width;
        var height = actual.Height;
        var differingPixels = 0;
        var maxObservedDelta = 0;
        SKBitmap? diffBitmap = null;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var actPx = actual.GetPixel(x, y);
                var expPx = expected.GetPixel(x, y);

                var deltaR = Math.Abs(actPx.Red - expPx.Red);
                var deltaG = Math.Abs(actPx.Green - expPx.Green);
                var deltaB = Math.Abs(actPx.Blue - expPx.Blue);
                var deltaA = Math.Abs(actPx.Alpha - expPx.Alpha);
                var maxPixelDelta = Math.Max(Math.Max(deltaR, deltaG), Math.Max(deltaB, deltaA));

                if (maxPixelDelta > maxChannelDelta)
                {
                    differingPixels++;
                    if (maxPixelDelta > maxObservedDelta)
                    {
                        maxObservedDelta = maxPixelDelta;
                    }

                    diffBitmap ??= CreateDiffBase(expected);
                    // Highlight mismatch in bright magenta
                    diffBitmap.SetPixel(x, y, new SKColor(255, 0, 255, 255));
                }
            }
        }

        if (differingPixels > 0)
        {
            diffBitmap ??= CreateDiffBase(expected);
            WriteDiffArtifacts(actual, diffBitmap, baselineName, differingPixels);
            diffBitmap.Dispose();
            throw new AssertionFailedException(
                $"Golden baseline mismatch for '{baselineName}': {differingPixels} pixel(s) differed (max channel delta: {maxObservedDelta}, allowed: {maxChannelDelta}). Diff saved to '{DiffsDirectory}'.");
        }

        diffBitmap?.Dispose();
    }

    public static void MatchesBaselineGif(byte[] actualGifBytes, string baselineName)
    {
        ArgumentNullException.ThrowIfNull(actualGifBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineName);

        Directory.CreateDirectory(BaselinesDirectory);
        var baselinePath = Path.Combine(BaselinesDirectory, $"{baselineName}.gif");
        var shouldUpdate = string.Equals(Environment.GetEnvironmentVariable("GFX_UPDATE_GOLDEN"), "1", StringComparison.OrdinalIgnoreCase);

        if (shouldUpdate || !File.Exists(baselinePath))
        {
            File.WriteAllBytes(baselinePath, actualGifBytes);
        }

        using var actualStream = new MemoryStream(actualGifBytes);
        using var actualCodec = SKCodec.Create(actualStream)
            ?? throw new InvalidOperationException("Could not decode actual GIF.");

        using var expectedStream = File.OpenRead(baselinePath);
        using var expectedCodec = SKCodec.Create(expectedStream)
            ?? throw new InvalidOperationException("Could not decode baseline GIF.");

        actualCodec.FrameCount.Should().Be(expectedCodec.FrameCount, $"Frame count for '{baselineName}' must match baseline.");

        for (var i = 0; i < actualCodec.FrameCount; i++)
        {
            using var actualFrame = new SKBitmap(actualCodec.Info.Width, actualCodec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            actualCodec.GetPixels(actualFrame.Info, actualFrame.GetPixels(), new SKCodecOptions(i));

            MatchesBaseline(actualFrame, $"{baselineName}_frame_{i:00}");
        }
    }

    private static SKBitmap CreateDiffBase(SKBitmap expected)
    {
        var diff = new SKBitmap(expected.Width, expected.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var p = expected.GetPixel(x, y);
                // Dim down existing pixel as grayscale backdrop
                var lum = (byte)(((p.Red * 2) + (p.Green * 5) + p.Blue) / 8);
                diff.SetPixel(x, y, new SKColor((byte)(lum / 3), (byte)(lum / 3), (byte)(lum / 3), 255));
            }
        }

        return diff;
    }

    private static void WriteDiffArtifacts(SKBitmap actual, SKBitmap? diff, string baselineName, int? differingPixels)
    {
        Directory.CreateDirectory(DiffsDirectory);
        var actualPath = Path.Combine(DiffsDirectory, $"{baselineName}.actual.png");
        ImageWriter.Write(actual, actualPath);

        if (diff is not null)
        {
            var diffPath = Path.Combine(DiffsDirectory, $"{baselineName}.diff.png");
            ImageWriter.Write(diff, diffPath);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GfxStudio.sln")))
            {
                return dir.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string FindBaselinesDirectory() =>
        Path.Combine(FindRepositoryRoot(), "tests", "GfxStudio.Tests", "Golden", "Baselines");
}

public class AssertionFailedException(string message) : Exception(message);
