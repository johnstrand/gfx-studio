using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Output;

/// <summary>
/// Writes an animation frame sequence (<c>output.mode = animation_frames</c>) to individual image files.
/// </summary>
public sealed class AnimationFramesOutputWriter
{
    private readonly SingleImageOutputWriter _imageWriter;

    public AnimationFramesOutputWriter(SingleImageOutputWriter? imageWriter = null)
    {
        _imageWriter = imageWriter ?? new SingleImageOutputWriter();
    }

    public static IReadOnlyList<string> ResolveFramePaths(IReadOnlyList<string> evaluatedFullPaths)
    {
        ArgumentNullException.ThrowIfNull(evaluatedFullPaths);
        if (evaluatedFullPaths.Count <= 1)
        {
            return evaluatedFullPaths;
        }

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var distinctCount = evaluatedFullPaths.Distinct(pathComparer).Count();
        if (distinctCount == evaluatedFullPaths.Count)
        {
            return evaluatedFullPaths;
        }

        // If a static filename was specified (e.g. "frame.png"), append _{frameIndex:0000} before the extension.
        var resolved = new string[evaluatedFullPaths.Count];
        for (var i = 0; i < evaluatedFullPaths.Count; i++)
        {
            var path = evaluatedFullPaths[i];
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".png";
            }

            resolved[i] = Path.Combine(directory, $"{stem}_{i:0000}{extension}");
        }

        return resolved;
    }

    public IReadOnlyList<GeneratedArtifact> WriteFrames(
        IReadOnlyList<SKBitmap> frames,
        IReadOnlyList<string> fullPaths,
        Dimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(fullPaths);
        if (frames.Count != fullPaths.Count)
        {
            throw new ArgumentException("Frames and file paths must have the same count.");
        }

        var resolvedPaths = ResolveFramePaths(fullPaths);
        var artifacts = new List<GeneratedArtifact>(frames.Count);
        try
        {
            for (var i = 0; i < frames.Count; i++)
            {
                var bitmap = frames[i];
                var targetPath = resolvedPaths[i];
                var bytes = _imageWriter.Encode(bitmap, targetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllBytes(targetPath, bytes);
                artifacts.Add(new GeneratedArtifact(targetPath, bitmap, bytes, dimensions));
            }

            return artifacts.AsReadOnly();
        }
        catch
        {
            foreach (var artifact in artifacts)
            {
                artifact.Dispose();
            }

            throw;
        }
    }
}
