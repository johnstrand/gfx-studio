using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers.Patterns;

/// <summary>
/// Renders periodic wave patterns (<c>type = waves</c>) including directional sine/square/sawtooth waves,
/// radial ripples, multi-source wave interference, and classic demoscene plasma.
/// </summary>
public sealed class WavesLayerRenderer : ILayerRenderer
{
    public string LayerType => "waves";

    public IReadOnlyCollection<string> SupportedProperties =>
        ["style", "wave_type", "frequency", "scale", "wavelength", "angle", "rotation", "phase", "sources", "count", "color", "background", "pattern_offset", "phase_offset"];

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var style = BricksLayerRenderer.ResolveString(context, "style", "wave_type", defaultValue: "sine").ToLowerInvariant();
        var frequency = Math.Max(0.1, BricksLayerRenderer.ResolveDouble(context, "frequency", "scale", defaultValue: 4.0));
        if (!context.LayerProperties.Properties.ContainsKey("frequency")
            && !context.LayerProperties.Properties.ContainsKey("scale")
            && context.LayerProperties.Properties.ContainsKey("wavelength"))
        {
            var wl = Math.Max(1.0, BricksLayerRenderer.ResolveDouble(context, "wavelength", null, defaultValue: 16.0));
            frequency = Math.Max(0.1, Math.Min(bitmap.Width, bitmap.Height) / wl);
        }
        var angleDeg = BricksLayerRenderer.ResolveDouble(context, "angle", "rotation", defaultValue: 0.0);
        var phase = BricksLayerRenderer.ResolveDouble(context, "phase", null, defaultValue: 0.0);
        var sourceCount = Math.Clamp((int)BricksLayerRenderer.ResolveDouble(context, "sources", "count", defaultValue: 3.0), 1, 16);
        var (offsetX, offsetY) = StripesLayerRenderer.ResolvePatternOffset(context);

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

        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelBuffer = new byte[width * height * 4];

        // Generate deterministic emitter positions for interference mode
        var emitters = new (double X, double Y)[sourceCount];
        for (var i = 0; i < sourceCount; i++)
        {
            emitters[i] = (
                (0.15 + (0.7 * context.Random.NextDouble())) * width,
                (0.15 + (0.7 * context.Random.NextDouble())) * height
            );
        }

        var rad = angleDeg * (Math.PI / 180.0);
        var cosA = Math.Cos(rad);
        var sinA = Math.Sin(rad);
        var invDim = 1.0 / Math.Max(1.0, Math.Min(width, height));

        for (var y = 0; y < height; y++)
        {
            var sy = y - offsetY;
            var ny = (sy + 0.5) * invDim;
            var cy = ((sy + 0.5) - (height * 0.5)) * invDim;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var sx = x - offsetX;
                var nx = (sx + 0.5) * invDim;
                var cx = ((sx + 0.5) - (width * 0.5)) * invDim;

                double t;
                switch (style)
                {
                    case "radial" or "concentric" or "ripple":
                    {
                        var dist = Math.Sqrt((cx * cx) + (cy * cy));
                        t = 0.5 + (0.5 * Math.Sin(((dist * frequency) - phase) * Math.Tau));
                        break;
                    }

                    case "interference":
                    {
                        var sum = 0.0;
                        for (var i = 0; i < sourceCount; i++)
                        {
                            var dx = ((sx + 0.5) - emitters[i].X) * invDim;
                            var dy = ((sy + 0.5) - emitters[i].Y) * invDim;
                            var dist = Math.Sqrt((dx * dx) + (dy * dy));
                            sum += Math.Sin(((dist * frequency) - phase) * Math.Tau);
                        }

                        t = 0.5 + (0.5 * (sum / sourceCount));
                        break;
                    }

                    case "plasma":
                    {
                        var v1 = Math.Sin(((nx * frequency) + phase) * Math.Tau);
                        var v2 = Math.Sin(((ny * frequency) - phase) * Math.Tau);
                        var v3 = Math.Sin((((nx + ny) * frequency * 0.7) + phase) * Math.Tau);
                        var dist = Math.Sqrt((cx * cx) + (cy * cy));
                        var v4 = Math.Sin(((dist * frequency * 1.2) - phase) * Math.Tau);
                        t = 0.5 + (0.125 * (v1 + v2 + v3 + v4));
                        break;
                    }

                    case "square":
                    {
                        var proj = (nx * cosA) + (ny * sinA);
                        var s = Math.Sin(((proj * frequency) + phase) * Math.Tau);
                        t = s >= 0.0 ? 1.0 : 0.0;
                        break;
                    }

                    case "sawtooth" or "saw":
                    {
                        var proj = ((nx * cosA) + (ny * sinA)) * frequency + phase;
                        t = proj - Math.Floor(proj);
                        break;
                    }

                    case "triangle":
                    {
                        var proj = ((nx * cosA) + (ny * sinA)) * frequency + phase;
                        var frac = proj - Math.Floor(proj);
                        t = 1.0 - Math.Abs((2.0 * frac) - 1.0);
                        break;
                    }

                    default: // "sine"
                    {
                        var proj = (nx * cosA) + (ny * sinA);
                        t = 0.5 + (0.5 * Math.Sin(((proj * frequency) + phase) * Math.Tau));
                        break;
                    }
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
