using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Patterns;

/// <summary>
/// Renders Archimedean, logarithmic, or vortex spiral patterns (<c>type = spiral</c>).
/// </summary>
public sealed class SpiralLayerRenderer : ILayerRenderer
{
    public string LayerType => "spiral";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var style = BricksLayerRenderer.ResolveString(context, "style", null, defaultValue: "archimedean").ToLowerInvariant();
        var arms = Math.Clamp(BricksLayerRenderer.ResolveDouble(context, "arms", "branches", defaultValue: 2.0), 1.0, 64.0);
        var tightness = BricksLayerRenderer.ResolveDouble(context, "tightness", "twist", defaultValue: 3.0);
        var phase = BricksLayerRenderer.ResolveDouble(context, "phase", null, defaultValue: 0.0);

        var width = bitmap.Width;
        var height = bitmap.Height;
        var center = new Point2D(width * 0.5f, height * 0.5f);
        if (context.LayerProperties.TryGetProperty("center", out var cVal) && cVal.TryCoerceToPoint2D(out var pt))
        {
            center = pt;
        }

        ColorRamp? ramp = null;
        var fgColor = ColorRgba.White;
        var bgColor = ColorRgba.Black;
        if (context.LayerProperties.TryGetProperty("color", out var colVal))
        {
            if (colVal is GfxColorRamp cr)
            {
                ramp = cr.Ramp;
            }
            else if (colVal is GfxColor c)
            {
                fgColor = c.Color;
            }
        }

        if (context.LayerProperties.TryGetProperty("background", out var bgVal) && bgVal is GfxColor bgC)
        {
            bgColor = bgC.Color;
        }

        var pixelBuffer = new byte[width * height * 4];
        var maxRadius = Math.Max(1.0, Math.Sqrt((width * width) + (height * height)) * 0.5);

        for (var y = 0; y < height; y++)
        {
            var dy = (y + 0.5) - center.Y;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var dx = (x + 0.5) - center.X;
                var r = Math.Sqrt((dx * dx) + (dy * dy)) / maxRadius;
                var theta = Math.Atan2(dy, dx); // -PI..PI

                double spiralVal;
                if (style == "logarithmic")
                {
                    var logR = Math.Log(Math.Max(1e-4, r) * 10.0 + 0.1);
                    spiralVal = (theta * arms) + (logR * tightness * Math.Tau) - (phase * Math.Tau);
                }
                else
                {
                    spiralVal = (theta * arms) + (r * tightness * Math.Tau) - (phase * Math.Tau);
                }

                var t = 0.5 + (0.5 * Math.Sin(spiralVal));
                if (style == "vortex")
                {
                    var radialFalloff = Math.Clamp(1.0 - (r * 0.85), 0.0, 1.0);
                    t *= radialFalloff;
                }

                var sampleT = (float)Math.Clamp(t, 0.0, 1.0);
                var chosen = ramp is not null
                    ? ramp.Sample(sampleT)
                    : TruchetLayerRenderer.LerpColor(bgColor, fgColor, sampleT);

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), chosen);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }
}
