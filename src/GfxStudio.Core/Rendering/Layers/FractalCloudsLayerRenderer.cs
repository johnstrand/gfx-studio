using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Noise;
using GfxStudio.Core.Primitives;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers;

/// <summary>
/// Renders a procedural multi-octave fractal cloud texture (<c>type = fractal_clouds</c>)
/// mapped through a <see cref="ColorRamp"/>.
/// </summary>
public sealed class FractalCloudsLayerRenderer : ILayerRenderer
{
    private static readonly ColorRamp DefaultColorRamp = new([
        new ColorStop(0.0f, ColorRgba.White),
        new ColorStop(1.0f, ColorRgba.Black)
    ]);

    public string LayerType => "fractal_clouds";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var colorRamp = ResolveColorRamp(context);
        if (colorRamp is null)
        {
            return bitmap;
        }

        var scale = ResolvePositiveDouble(context, "scale", "frequency", defaultValue: 4.0);
        var octaves = ResolveInt(context, "octaves", defaultValue: 5);
        var persistence = ResolveDouble(context, "persistence", "gain", defaultValue: 0.5);
        var lacunarity = ResolvePositiveDouble(context, "lacunarity", null, defaultValue: 2.0);
        var phase = ResolveDouble(context, "phase", "z", defaultValue: 0.0);
        var loopSeamless = ResolveBool(context, "loop_seamless", defaultValue: false);

        var noise = new PerlinNoise(context.Random);
        var fbm = new FractalBrownianMotion(noise, octaves, persistence, lacunarity);

        var width = bitmap.Width;
        var height = bitmap.Height;
        var invWidth = width > 0 ? scale / width : 0.0;
        var invHeight = height > 0 ? scale / height : 0.0;

        var pixelBuffer = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var ny = y * invHeight;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var nx = x * invWidth;
                var value = loopSeamless
                    ? fbm.SampleSeamlessLoop(nx, ny, phase)
                    : fbm.Sample3D(nx, ny, phase);

                var sampled = colorRamp.Sample(value);
                var pixelIndex = rowOffset + (x * 4);

                if (sampled.A == byte.MaxValue)
                {
                    pixelBuffer[pixelIndex] = sampled.R;
                    pixelBuffer[pixelIndex + 1] = sampled.G;
                    pixelBuffer[pixelIndex + 2] = sampled.B;
                    pixelBuffer[pixelIndex + 3] = byte.MaxValue;
                }
                else if (sampled.A == 0)
                {
                    pixelBuffer[pixelIndex] = 0;
                    pixelBuffer[pixelIndex + 1] = 0;
                    pixelBuffer[pixelIndex + 2] = 0;
                    pixelBuffer[pixelIndex + 3] = 0;
                }
                else
                {
                    // Premultiply RGB by alpha for SKAlphaType.Premul
                    var alphaFactor = sampled.A / 255.0f;
                    pixelBuffer[pixelIndex] = (byte)Math.Round(sampled.R * alphaFactor);
                    pixelBuffer[pixelIndex + 1] = (byte)Math.Round(sampled.G * alphaFactor);
                    pixelBuffer[pixelIndex + 2] = (byte)Math.Round(sampled.B * alphaFactor);
                    pixelBuffer[pixelIndex + 3] = sampled.A;
                }
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);

        return bitmap;
    }

    private static ColorRamp? ResolveColorRamp(LayerRenderContext context)
    {
        if (!context.LayerProperties.TryGetProperty("color", out var colorVal))
        {
            return DefaultColorRamp;
        }

        return colorVal switch
        {
            GfxColorRamp ramp => ramp.Ramp,
            GfxColor singleColor => new ColorRamp([
                new ColorStop(0.0f, ColorRgba.Black),
                new ColorStop(1.0f, singleColor.Color)
            ]),
            _ => ReportInvalidColor(context)
        };
    }

    private static ColorRamp? ReportInvalidColor(LayerRenderContext context)
    {
        context.AddDiagnostic(
            "RND013",
            $"Property 'color' on fractal_clouds layer '{context.LayerId}' must be a color_ramp or color.");
        return null;
    }

    private static int ResolveInt(LayerRenderContext context, string propName, int defaultValue)
    {
        if (!context.LayerProperties.TryGetProperty(propName, out var val))
        {
            return defaultValue;
        }

        if (val is GfxNumber num)
        {
            return num.AsInt32();
        }

        context.AddDiagnostic("RND014", $"Property '{propName}' on layer '{context.LayerId}' must be a number.");
        return defaultValue;
    }

    private static double ResolveDouble(LayerRenderContext context, string primaryName, string? aliasName, double defaultValue)
    {
        if (context.LayerProperties.TryGetProperty(primaryName, out var val)
            || (aliasName is not null && context.LayerProperties.TryGetProperty(aliasName, out val)))
        {
            if (val is GfxNumber num)
            {
                return num.Value;
            }

            context.AddDiagnostic("RND014", $"Property '{primaryName}' on layer '{context.LayerId}' must be a number.");
        }

        return defaultValue;
    }

    private static double ResolvePositiveDouble(LayerRenderContext context, string primaryName, string? aliasName, double defaultValue)
    {
        var value = ResolveDouble(context, primaryName, aliasName, defaultValue);
        if (value <= 0.0)
        {
            context.AddDiagnostic("RND014", $"Property '{primaryName}' on layer '{context.LayerId}' must be greater than zero.");
            return defaultValue;
        }

        return value;
    }

    private static bool ResolveBool(LayerRenderContext context, string propName, bool defaultValue)
    {
        if (!context.LayerProperties.TryGetProperty(propName, out var val))
        {
            return defaultValue;
        }

        if (val is GfxBoolean b)
        {
            return b.Value;
        }

        context.AddDiagnostic("RND014", $"Property '{propName}' on layer '{context.LayerId}' must be a boolean.");
        return defaultValue;
    }
}
