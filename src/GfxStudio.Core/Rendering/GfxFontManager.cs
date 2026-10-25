using SkiaSharp;

namespace GfxStudio.Core.Rendering;

/// <summary>Resolves user fonts and provides the embedded, platform-independent fallback font.</summary>
public sealed class GfxFontManager
{
    public const string FallbackFontResourceName = "GfxStudio.Core.Resources.Fonts.Roboto.ttf";

    public SKTypeface LoadTypeface(string? fontPath, string? fontFamily, string sourceFilePath, SKFontStyle? fontStyle = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);

        if (!string.IsNullOrWhiteSpace(fontPath))
        {
            var baseDirectory = Path.GetDirectoryName(sourceFilePath);
            var resolvedPath = Path.IsPathRooted(fontPath)
                ? fontPath
                : Path.Combine(string.IsNullOrEmpty(baseDirectory) ? Environment.CurrentDirectory : baseDirectory, fontPath);
            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException($"Font file '{fontPath}' was not found.", resolvedPath);
            }

            return SKTypeface.FromFile(resolvedPath)
                ?? throw new InvalidDataException($"Font file '{fontPath}' could not be loaded.");
        }

        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            var matchedTypeface = fontStyle is not null
                ? SKFontManager.Default.MatchFamily(fontFamily, fontStyle)
                : SKFontManager.Default.MatchFamily(fontFamily);
            if (matchedTypeface is not null)
            {
                return matchedTypeface;
            }
        }

        using var stream = typeof(GfxFontManager).Assembly.GetManifestResourceStream(FallbackFontResourceName)
            ?? throw new InvalidOperationException($"Embedded fallback font '{FallbackFontResourceName}' was not found.");
        return SKTypeface.FromStream(stream)
            ?? throw new InvalidDataException("The embedded fallback font could not be loaded.");
    }
}
