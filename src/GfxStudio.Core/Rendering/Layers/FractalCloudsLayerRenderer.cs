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

    private readonly int _defaultOctaves;

    public FractalCloudsLayerRenderer(string layerType = "fractal_clouds", int defaultOctaves = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerType);
        LayerType = layerType;
        _defaultOctaves = Math.Clamp(defaultOctaves, 1, 12);
    }

    public string LayerType { get; }

    public IReadOnlyCollection<string> SupportedProperties =>
    [
        "color",
        "scale",
        "frequency",
        "octaves",
        "persistence",
        "gain",
        "lacunarity",
        "phase",
        "z",
        "loop_seamless",
        "warp_strength",
        "warp_scale"
    ];

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var colorRamp = ResolveColorRamp(context);
        if (colorRamp is null)
        {
            return bitmap;
        }

        var scale = ResolveScaleDouble(context, defaultValue: 4.0);
        var octaves = ResolveInt(context, "octaves", defaultValue: _defaultOctaves);
        var persistence = ResolveDouble(context, "persistence", "gain", defaultValue: 0.5);
        var lacunarity = ResolvePositiveDouble(context, "lacunarity", null, defaultValue: 2.0);
        var phase = ResolveDouble(context, "phase", "z", defaultValue: 0.0);
        var loopSeamless = ResolveBool(context, "loop_seamless", defaultValue: false);
        var warpStrength = ResolveDouble(context, "warp_strength", null, defaultValue: 0.0);
        var warpScale = ResolvePositiveDouble(context, "warp_scale", null, defaultValue: scale);

        var noise = new PerlinNoise(context.Random);
        var fbm = new FractalBrownianMotion(noise, octaves, persistence, lacunarity);
        PerlinNoise? warpNoiseX = null;
        PerlinNoise? warpNoiseY = null;
        if (Math.Abs(warpStrength) > 0.0)
        {
            warpNoiseX = new PerlinNoise(context.Random);
            warpNoiseY = new PerlinNoise(context.Random);
        }

        var width = bitmap.Width;
        var height = bitmap.Height;
        var invWidth = width > 0 ? scale / width : 0.0;
        var invHeight = height > 0 ? scale / height : 0.0;
        var invWarpW = width > 0 ? warpScale / width : 0.0;
        var invWarpH = height > 0 ? warpScale / height : 0.0;

        var pixelBuffer = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var baseNy = y * invHeight;
            var wy = y * invWarpH;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var nx = x * invWidth;
                var ny = baseNy;

                if (warpNoiseX is not null && warpNoiseY is not null)
                {
                    var wx = x * invWarpW;
                    nx += warpStrength * warpNoiseX.Sample3D(wx, wy, phase);
                    ny += warpStrength * warpNoiseY.Sample3D(wx, wy, phase);
                }

                var value = loopSeamless
                    ? fbm.SampleSeamlessLoop(nx, ny, phase)
                    : fbm.Sample3D(nx, ny, phase);

                var sampled = colorRamp.Sample(value);
                var pixelIndex = rowOffset + (x * 4);

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, pixelIndex, sampled);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);

        return bitmap;
    }

    private static double ResolveScaleDouble(LayerRenderContext context, double defaultValue)
    {
        // Note: if 'scale' is an object `{ x = ..., y = ... }`, it is a 2D transform scale, not noise frequency.
        if (context.LayerProperties.TryGetProperty("scale", out var scaleVal) && scaleVal is GfxObject)
        {
            if (context.LayerProperties.TryGetProperty("frequency", out _))
            {
                return ResolvePositiveDouble(context, "frequency", null, defaultValue);
            }

            return defaultValue;
        }

        return ResolvePositiveDouble(context, "scale", "frequency", defaultValue);
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
