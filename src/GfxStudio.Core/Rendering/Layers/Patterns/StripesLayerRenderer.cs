using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Patterns;

/// <summary>
/// Renders a procedural striped pattern (<c>type = stripes</c>) with configurable width, angle, and colors/color ramp.
/// </summary>
public sealed class StripesLayerRenderer : ILayerRenderer
{
    public string LayerType => "stripes";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var stripeWidth = ResolvePositiveDouble(context, "stripe_width", "width", defaultValue: 8.0);
        var angleDeg = ResolveDouble(context, "angle", defaultValue: 45.0);
        var color1 = CheckerboardLayerRenderer.ResolveColor(context, "color1", ColorRgba.White);
        var color2 = CheckerboardLayerRenderer.ResolveColor(context, "color2", ColorRgba.Black);

        ColorRamp? ramp = null;
        if (context.LayerProperties.TryGetProperty("color", out var colorVal))
        {
            if (colorVal is GfxColorRamp gfxRamp)
            {
                ramp = gfxRamp.Ramp;
            }
            else if (colorVal is GfxColor singleColor)
            {
                color1 = singleColor.Color;
            }
            else
            {
                context.AddDiagnostic("RND013", $"Property 'color' on stripes layer '{context.LayerId}' must be a color or color_ramp.");
            }
        }

        var angleRad = angleDeg * (Math.PI / 180.0);
        var cosA = Math.Cos(angleRad);
        var sinA = Math.Sin(angleRad);
        var period = stripeWidth * 2.0;

        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelBuffer = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var proj = (x * cosA) + (y * sinA);
                ColorRgba sampled;
                if (ramp is not null)
                {
                    var wrapped = ((proj % period) + period) % period;
                    var t = (float)(wrapped / period);
                    sampled = ramp.Sample(t);
                }
                else
                {
                    var stripeIndex = (int)Math.Floor((proj + 1e-9) / stripeWidth);
                    sampled = (stripeIndex & 1) == 0 ? color1 : color2;
                }

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), sampled);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }

    internal static double ResolveDouble(LayerRenderContext context, string propName, double defaultValue)
    {
        if (!context.LayerProperties.TryGetProperty(propName, out var val))
        {
            return defaultValue;
        }

        if (val is GfxNumber num)
        {
            return num.Value;
        }

        context.AddDiagnostic("RND014", $"Property '{propName}' on layer '{context.LayerId}' must be a number.");
        return defaultValue;
    }

    internal static double ResolvePositiveDouble(
        LayerRenderContext context,
        string primaryName,
        string? aliasName,
        double defaultValue)
    {
        if (context.LayerProperties.TryGetProperty(primaryName, out var val)
            || (aliasName is not null && context.LayerProperties.TryGetProperty(aliasName, out val)))
        {
            if (val is GfxNumber num && num.Value > 0.0)
            {
                return num.Value;
            }

            context.AddDiagnostic("RND014", $"Property '{primaryName}' on layer '{context.LayerId}' must be a positive number.");
        }

        return defaultValue;
    }
}
