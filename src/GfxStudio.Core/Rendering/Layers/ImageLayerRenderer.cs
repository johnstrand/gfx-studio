using GfxStudio.Core.Evaluation;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers;

/// <summary>
/// Renders an external bitmap asset (<c>type = image</c>) with cropping, fitting (<c>stretch</c>, <c>contain</c>,
/// <c>cover</c>, <c>tile</c>, <c>none</c>), and resampling (<c>nearest</c>, <c>bilinear</c>, <c>bicubic</c>).
/// </summary>
public sealed class ImageLayerRenderer : ILayerRenderer
{
    private readonly GfxImageCache _cache;
    private readonly bool _allowPathTraversal;

    public ImageLayerRenderer(GfxImageCache? cache = null, bool allowPathTraversal = true)
    {
        _cache = cache ?? GfxImageCache.Shared;
        _allowPathTraversal = allowPathTraversal;
    }

    public string LayerType => "image";

    public GfxImageCache Cache => _cache;

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var destBitmap = context.CreateLayerBitmap();
        var sourcePath = ResolveSourcePath(context);
        if (sourcePath is null)
        {
            return destBitmap;
        }

        using var loadedBitmap = _cache.GetOrLoad(sourcePath);
        if (loadedBitmap is null)
        {
            context.AddDiagnostic("RND030", $"Could not load or decode image file '{sourcePath}' on layer '{context.LayerId}'.");
            return destBitmap;
        }

        using var sourceBitmap = ExtractCrop(context, loadedBitmap);
        if (sourceBitmap is null)
        {
            return destBitmap;
        }

        var fitMode = ResolveSymbol(context, "fit", "stretch");
        var resampleMode = ResolveSymbol(context, "resample", "bicubic");

        RenderFittedImage(context, sourceBitmap, destBitmap, fitMode, resampleMode);
        return destBitmap;
    }

    private string? ResolveSourcePath(LayerRenderContext context)
    {
        if (!context.LayerProperties.TryGetProperty("source", out var val)
            && !context.LayerProperties.TryGetProperty("path", out val))
        {
            context.AddDiagnostic("RND030", $"Image layer '{context.LayerId}' is missing required 'source' property.");
            return null;
        }

        if (val is not GfxString str || string.IsNullOrWhiteSpace(str.Value))
        {
            context.AddDiagnostic("RND030", $"Property 'source' on image layer '{context.LayerId}' must be a non-empty string.");
            return null;
        }

        var documentFilePath = context.LayerSpan.FilePath;
        if (string.IsNullOrWhiteSpace(documentFilePath) || documentFilePath == "<memory>")
        {
            documentFilePath = context.EvaluationContext.Document?.Span.FilePath;
        }

        var baseDirectory = string.IsNullOrWhiteSpace(documentFilePath) || documentFilePath == "<memory>"
            ? Environment.CurrentDirectory
            : Path.GetDirectoryName(Path.GetFullPath(documentFilePath)) ?? Environment.CurrentDirectory;

        var fullBaseDir = Path.GetFullPath(baseDirectory);
        var resolvedPath = Path.GetFullPath(Path.Combine(fullBaseDir, str.Value));

        if (!_allowPathTraversal)
        {
            var baseWithSep = fullBaseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!resolvedPath.StartsWith(baseWithSep, comparison) && !string.Equals(resolvedPath, fullBaseDir, comparison))
            {
                context.AddDiagnostic("RND031", $"Image path '{str.Value}' escapes the base directory '{fullBaseDir}'.");
                return null;
            }
        }

        return resolvedPath;
    }

    private static SKBitmap? ExtractCrop(LayerRenderContext context, SKBitmap loaded)
    {
        if (!context.LayerProperties.TryGetProperty("crop", out var cropVal))
        {
            return loaded.Copy();
        }

        if (cropVal is not GfxObject cropObj
            || !cropObj.TryGetProperty("x", out var xVal) || xVal is not GfxNumber xNum
            || !cropObj.TryGetProperty("y", out var yVal) || yVal is not GfxNumber yNum
            || !cropObj.TryGetProperty("width", out var wVal) || wVal is not GfxNumber wNum
            || !cropObj.TryGetProperty("height", out var hVal) || hVal is not GfxNumber hNum)
        {
            context.AddDiagnostic("RND032", $"Property 'crop' on image layer '{context.LayerId}' must be '{{ x, y, width, height }}'.");
            return null;
        }

        var cx = Math.Clamp(xNum.AsInt32(), 0, loaded.Width);
        var cy = Math.Clamp(yNum.AsInt32(), 0, loaded.Height);
        var cw = Math.Clamp(wNum.AsInt32(), 0, loaded.Width - cx);
        var ch = Math.Clamp(hNum.AsInt32(), 0, loaded.Height - cy);
        if (cw <= 0 || ch <= 0)
        {
            context.AddDiagnostic("RND032", $"Crop rectangle on image layer '{context.LayerId}' has non-positive area.");
            return null;
        }

        var cropped = new SKBitmap(new SKImageInfo(cw, ch, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < ch; y++)
        {
            for (var x = 0; x < cw; x++)
            {
                cropped.SetPixel(x, y, loaded.GetPixel(cx + x, cy + y));
            }
        }

        return cropped;
    }

    private static void RenderFittedImage(
        LayerRenderContext context,
        SKBitmap source,
        SKBitmap dest,
        string fitMode,
        string resampleMode)
    {
        var destW = dest.Width;
        var destH = dest.Height;
        var srcW = source.Width;
        var srcH = source.Height;
        if (destW <= 0 || destH <= 0 || srcW <= 0 || srcH <= 0)
        {
            return;
        }

        var normalizedFit = fitMode.ToLowerInvariant();
        if (normalizedFit == "tile")
        {
            RenderTiled(context, source, dest);
            return;
        }

        SKRect targetRect;
        switch (normalizedFit)
        {
            case "stretch":
                targetRect = new SKRect(0, 0, destW, destH);
                break;

            case "contain":
            {
                var scale = Math.Min(destW / (float)srcW, destH / (float)srcH);
                var drawW = srcW * scale;
                var drawH = srcH * scale;
                var offsetX = (destW - drawW) * 0.5f;
                var offsetY = (destH - drawH) * 0.5f;
                targetRect = new SKRect(offsetX, offsetY, offsetX + drawW, offsetY + drawH);
                break;
            }

            case "cover":
            {
                var scale = Math.Max(destW / (float)srcW, destH / (float)srcH);
                var drawW = srcW * scale;
                var drawH = srcH * scale;
                var offsetX = (destW - drawW) * 0.5f;
                var offsetY = (destH - drawH) * 0.5f;
                targetRect = new SKRect(offsetX, offsetY, offsetX + drawW, offsetY + drawH);
                break;
            }

            case "none":
                targetRect = new SKRect(0, 0, srcW, srcH);
                break;

            default:
                context.AddDiagnostic("RND033", $"Unknown 'fit' mode '{fitMode}' on image layer '{context.LayerId}'.");
                targetRect = new SKRect(0, 0, destW, destH);
                break;
        }

        var normalizedResample = resampleMode.ToLowerInvariant();
        if (normalizedResample == "nearest")
        {
            RenderNearest(source, dest, targetRect);
            return;
        }

        var filterQuality = normalizedResample switch
        {
            "bilinear" => SKFilterQuality.Low,
            "bicubic" => SKFilterQuality.High,
            _ => ReportInvalidResample(context, resampleMode)
        };

        using var canvas = new SKCanvas(dest);
        using var paint = new SKPaint
        {
            FilterQuality = filterQuality,
            IsAntialias = true
        };
        canvas.DrawBitmap(source, targetRect, paint);
        canvas.Flush();
    }

    private static void RenderNearest(SKBitmap source, SKBitmap dest, SKRect targetRect)
    {
        var destW = dest.Width;
        var destH = dest.Height;
        var srcW = source.Width;
        var srcH = source.Height;
        var targetW = targetRect.Width;
        var targetH = targetRect.Height;
        if (targetW <= 0 || targetH <= 0)
        {
            return;
        }

        for (var y = 0; y < destH; y++)
        {
            var py = y + 0.5f;
            if (py < targetRect.Top || py >= targetRect.Bottom)
            {
                continue;
            }

            var v = (py - targetRect.Top) / targetH;
            var sy = Math.Clamp((int)Math.Floor(v * srcH), 0, srcH - 1);

            for (var x = 0; x < destW; x++)
            {
                var px = x + 0.5f;
                if (px < targetRect.Left || px >= targetRect.Right)
                {
                    continue;
                }

                var u = (px - targetRect.Left) / targetW;
                var sx = Math.Clamp((int)Math.Floor(u * srcW), 0, srcW - 1);
                dest.SetPixel(x, y, source.GetPixel(sx, sy));
            }
        }
    }

    private static void RenderTiled(LayerRenderContext context, SKBitmap source, SKBitmap dest)
    {
        var offsetX = 0;
        var offsetY = 0;
        if (context.LayerProperties.TryGetProperty("tile_offset", out var offsetVal))
        {
            if (offsetVal.TryCoerceToPoint2D(out var pt))
            {
                offsetX = (int)Math.Round(pt.X);
                offsetY = (int)Math.Round(pt.Y);
            }
            else
            {
                context.AddDiagnostic("RND034", $"Invalid 'tile_offset' on image layer '{context.LayerId}'.");
            }
        }

        var srcW = source.Width;
        var srcH = source.Height;
        var destW = dest.Width;
        var destH = dest.Height;

        for (var y = 0; y < destH; y++)
        {
            var sy = (((y - offsetY) % srcH) + srcH) % srcH;
            for (var x = 0; x < destW; x++)
            {
                var sx = (((x - offsetX) % srcW) + srcW) % srcW;
                dest.SetPixel(x, y, source.GetPixel(sx, sy));
            }
        }
    }

    private static SKFilterQuality ReportInvalidResample(LayerRenderContext context, string resampleMode)
    {
        context.AddDiagnostic("RND035", $"Unknown 'resample' mode '{resampleMode}' on image layer '{context.LayerId}'.");
        return SKFilterQuality.High;
    }

    private static string ResolveSymbol(LayerRenderContext context, string propName, string defaultValue)
    {
        if (!context.LayerProperties.TryGetProperty(propName, out var val))
        {
            return defaultValue;
        }

        return val switch
        {
            GfxSymbol s => s.Identifier,
            GfxString str => str.Value,
            _ => defaultValue
        };
    }
}
