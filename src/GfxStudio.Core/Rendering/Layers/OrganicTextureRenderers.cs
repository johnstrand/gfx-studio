using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Noise;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Rendering.Layers.Patterns;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers;

/// <summary>
/// Renders procedural wood grain (<c>type = wood</c>) using perturbed concentric growth rings and axial fiber noise.
/// </summary>
public sealed class WoodLayerRenderer : ILayerRenderer
{
    private static readonly ColorRamp DefaultWoodRamp = new([
        new ColorStop(0.0f, new ColorRgba(102, 58, 28, 255)),
        new ColorStop(0.6f, new ColorRgba(168, 108, 60, 255)),
        new ColorStop(1.0f, new ColorRgba(214, 158, 98, 255))
    ]);

    public string LayerType => "wood";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var rings = Math.Max(0.5, BricksLayerRenderer.ResolveDouble(context, "rings", "frequency", defaultValue: 8.0));
        var turbulence = Math.Max(0.0, BricksLayerRenderer.ResolveDouble(context, "turbulence", "warp_strength", defaultValue: 1.5));
        var grain = Math.Clamp(BricksLayerRenderer.ResolveDouble(context, "grain", null, defaultValue: 0.25), 0.0, 1.0);
        var scale = Math.Max(0.1, BricksLayerRenderer.ResolveDouble(context, "scale", null, defaultValue: 3.0));
        var angleDeg = BricksLayerRenderer.ResolveDouble(context, "angle", "rotation", defaultValue: 0.0);

        var ramp = DefaultWoodRamp;
        if (context.LayerProperties.TryGetProperty("color", out var colVal) && colVal is GfxColorRamp cr)
        {
            ramp = cr.Ramp;
        }

        var noise = new PerlinNoise(context.Random);
        var fbm = new FractalBrownianMotion(noise, octaves: 4, persistence: 0.5, lacunarity: 2.0);
        var grainNoise = new PerlinNoise(context.Random);

        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelBuffer = new byte[width * height * 4];
        var rad = angleDeg * (Math.PI / 180.0);
        var cosA = Math.Cos(rad);
        var sinA = Math.Sin(rad);
        var invW = width > 0 ? 1.0 / width : 0.0;
        var invH = height > 0 ? 1.0 / height : 0.0;

        for (var y = 0; y < height; y++)
        {
            var ny = (y * invH) - 0.5;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var nx = (x * invW) - 0.5;
                var rx = (nx * cosA) - (ny * sinA);
                var ry = (nx * sinA) + (ny * cosA);

                var dist = Math.Sqrt((rx * rx) + (ry * ry * 0.35)) * rings;
                var turb = (fbm.Sample2D((rx + 0.5) * scale, (ry + 0.5) * scale) - 0.5) * 2.0 * turbulence;
                var ringVal = dist + turb;
                var frac = ringVal - Math.Floor(ringVal);
                var ringWave = 0.5 - (0.5 * Math.Cos(frac * Math.Tau));

                // Fine stretched wood fiber grain along the axis
                var fiber = grainNoise.Sample2D((rx + 0.5) * scale * 24.0, (ry + 0.5) * scale * 3.0) * grain;
                var t = (float)Math.Clamp((ringWave * (1.0 - (grain * 0.5))) + (fiber * 0.5) + 0.1, 0.0, 1.0);

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), ramp.Sample(t));
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }
}

/// <summary>
/// Renders procedural marble veins (<c>type = marble</c>) using sine-wrapped turbulent fBm.
/// </summary>
public sealed class MarbleLayerRenderer : ILayerRenderer
{
    private static readonly ColorRamp DefaultMarbleRamp = new([
        new ColorStop(0.0f, new ColorRgba(28, 33, 43, 255)),
        new ColorStop(0.35f, new ColorRgba(145, 152, 165, 255)),
        new ColorStop(1.0f, new ColorRgba(248, 249, 252, 255))
    ]);

    public string LayerType => "marble";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var veins = Math.Max(0.1, BricksLayerRenderer.ResolveDouble(context, "veins", "frequency", defaultValue: 3.5));
        var turbulence = Math.Max(0.0, BricksLayerRenderer.ResolveDouble(context, "turbulence", "warp_strength", defaultValue: 5.0));
        var scale = Math.Max(0.1, BricksLayerRenderer.ResolveDouble(context, "scale", null, defaultValue: 3.0));
        var octaves = Math.Clamp((int)BricksLayerRenderer.ResolveDouble(context, "octaves", null, defaultValue: 5.0), 1, 10);
        var sharpness = Math.Max(0.1, BricksLayerRenderer.ResolveDouble(context, "sharpness", null, defaultValue: 1.5));
        var angleDeg = BricksLayerRenderer.ResolveDouble(context, "angle", "rotation", defaultValue: 25.0);
        var phase = BricksLayerRenderer.ResolveDouble(context, "phase", null, defaultValue: 0.0);

        var ramp = DefaultMarbleRamp;
        if (context.LayerProperties.TryGetProperty("color", out var colVal) && colVal is GfxColorRamp cr)
        {
            ramp = cr.Ramp;
        }

        var noise = new PerlinNoise(context.Random);
        var fbm = new FractalBrownianMotion(noise, octaves, persistence: 0.55, lacunarity: 2.0);

        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelBuffer = new byte[width * height * 4];
        var rad = angleDeg * (Math.PI / 180.0);
        var cosA = Math.Cos(rad);
        var sinA = Math.Sin(rad);
        var invW = width > 0 ? 1.0 / width : 0.0;
        var invH = height > 0 ? 1.0 / height : 0.0;

        for (var y = 0; y < height; y++)
        {
            var ny = y * invH;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var nx = x * invW;
                var proj = (nx * cosA) + (ny * sinA);
                var turb = fbm.Sample3D(nx * scale, ny * scale, phase);
                var rawWave = 0.5 + (0.5 * Math.Sin(((proj * veins) + (turb * turbulence)) * Math.PI));
                var t = (float)Math.Clamp(Math.Pow(rawWave, 1.0 / sharpness), 0.0, 1.0);

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), ramp.Sample(t));
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }
}

/// <summary>
/// Renders underwater refracted light networks (<c>type = caustics</c>) via interfering ridged cellular/noise fields.
/// </summary>
public sealed class CausticsLayerRenderer : ILayerRenderer
{
    private static readonly ColorRamp DefaultCausticsRamp = new([
        new ColorStop(0.0f, new ColorRgba(6, 36, 76, 255)),
        new ColorStop(0.65f, new ColorRgba(24, 140, 210, 255)),
        new ColorStop(1.0f, new ColorRgba(225, 252, 255, 255))
    ]);

    public string LayerType => "caustics";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var scale = Math.Max(0.5, BricksLayerRenderer.ResolveDouble(context, "scale", "frequency", defaultValue: 4.0));
        var sharpness = Math.Max(0.5, BricksLayerRenderer.ResolveDouble(context, "sharpness", null, defaultValue: 2.5));
        var phase = BricksLayerRenderer.ResolveDouble(context, "phase", null, defaultValue: 0.0);

        var ramp = DefaultCausticsRamp;
        if (context.LayerProperties.TryGetProperty("color", out var colVal) && colVal is GfxColorRamp cr)
        {
            ramp = cr.Ramp;
        }

        var worley1 = new WorleyNoise(context.Random);
        var worley2 = new WorleyNoise(context.Random);
        var warpNoise = new PerlinNoise(context.Random);

        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelBuffer = new byte[width * height * 4];
        var invW = width > 0 ? scale / width : 0.0;
        var invH = height > 0 ? scale / height : 0.0;
        var shift = phase * 1.5;

        for (var y = 0; y < height; y++)
        {
            var ny = y * invH;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var nx = x * invW;
                var wx = warpNoise.Sample3D(nx * 0.8, ny * 0.8, phase) * 0.35;
                var wy = warpNoise.Sample3D((nx * 0.8) + 5.2, (ny * 0.8) + 1.3, phase) * 0.35;

                var f2MinusF1A = worley1.Sample2D(nx + wx + shift, ny + wy + (shift * 0.5), feature: WorleyFeature.F2MinusF1);
                var f2MinusF1B = worley2.Sample2D((nx * 1.25) - wx - (shift * 0.7), (ny * 1.25) - wy + shift, feature: WorleyFeature.F2MinusF1);

                // Ridge creases where f2 - f1 is small
                var ridgeA = 1.0 - Math.Clamp(f2MinusF1A * 1.4, 0.0, 1.0);
                var ridgeB = 1.0 - Math.Clamp(f2MinusF1B * 1.4, 0.0, 1.0);
                var combined = Math.Clamp((ridgeA * 0.6) + (ridgeB * 0.6), 0.0, 1.0);
                var t = (float)Math.Clamp(Math.Pow(combined, sharpness), 0.0, 1.0);

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), ramp.Sample(t));
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }
}

/// <summary>
/// Renders swirling divergence-free vector flow ribbons (<c>type = flow_field</c> / <c>curl_noise</c>)
/// by advecting coordinates along the 2D curl of an fBm potential field.
/// </summary>
public sealed class FlowFieldLayerRenderer(string layerType = "flow_field") : ILayerRenderer
{
    private static readonly ColorRamp DefaultFlowRamp = new([
        new ColorStop(0.0f, new ColorRgba(15, 23, 42, 255)),
        new ColorStop(0.5f, new ColorRgba(56, 189, 248, 255)),
        new ColorStop(1.0f, new ColorRgba(244, 114, 182, 255))
    ]);

    public string LayerType { get; } = layerType;

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var scale = Math.Max(0.2, BricksLayerRenderer.ResolveDouble(context, "scale", null, defaultValue: 3.0));
        var octaves = Math.Clamp((int)BricksLayerRenderer.ResolveDouble(context, "octaves", null, defaultValue: 4.0), 1, 8);
        var steps = Math.Clamp((int)BricksLayerRenderer.ResolveDouble(context, "steps", "iterations", defaultValue: 6.0), 1, 32);
        var stepSize = BricksLayerRenderer.ResolveDouble(context, "step_size", "strength", defaultValue: 0.12);
        var phase = BricksLayerRenderer.ResolveDouble(context, "phase", null, defaultValue: 0.0);

        var ramp = DefaultFlowRamp;
        if (context.LayerProperties.TryGetProperty("color", out var colVal) && colVal is GfxColorRamp cr)
        {
            ramp = cr.Ramp;
        }

        var potentialNoise = new PerlinNoise(context.Random);
        var potentialFbm = new FractalBrownianMotion(potentialNoise, octaves, 0.5, 2.0);
        var colorNoise = new PerlinNoise(context.Random);
        var colorFbm = new FractalBrownianMotion(colorNoise, octaves, 0.5, 2.0);

        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelBuffer = new byte[width * height * 4];
        var invW = width > 0 ? scale / width : 0.0;
        var invH = height > 0 ? scale / height : 0.0;
        const double eps = 0.01;
        const double inv2Eps = 1.0 / (2.0 * eps);

        for (var y = 0; y < height; y++)
        {
            var baseY = y * invH;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var px = x * invW;
                var py = baseY;

                // Advect (px, py) along the 2D curl vector (dPsi/dy, -dPsi/dx)
                for (var s = 0; s < steps; s++)
                {
                    var dPsiDy = (potentialFbm.Sample3D(px, py + eps, phase) - potentialFbm.Sample3D(px, py - eps, phase)) * inv2Eps;
                    var dPsiDx = (potentialFbm.Sample3D(px + eps, py, phase) - potentialFbm.Sample3D(px - eps, py, phase)) * inv2Eps;
                    px += dPsiDy * stepSize;
                    py -= dPsiDx * stepSize;
                }

                var val = colorFbm.Sample3D(px, py, phase);
                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), ramp.Sample(val));
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }
}

/// <summary>
/// Renders a deterministic procedural star field (<c>type = starfield</c>) with variable star sizes,
/// soft glow halos, and animatable twinkle.
/// </summary>
public sealed class StarfieldLayerRenderer : ILayerRenderer
{
    public string LayerType => "starfield";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var width = bitmap.Width;
        var height = bitmap.Height;
        var defaultCount = Math.Clamp((width * height) / 48, 20, 2000);
        var count = Math.Clamp((int)BricksLayerRenderer.ResolveDouble(context, "count", "stars", defaultValue: defaultCount), 1, 20000);
        var minRadius = Math.Max(0.3, BricksLayerRenderer.ResolveDouble(context, "min_size", "min_radius", defaultValue: 0.5));
        var maxRadius = Math.Max(minRadius, BricksLayerRenderer.ResolveDouble(context, "max_size", "max_radius", defaultValue: 2.2));
        var glow = Math.Clamp(BricksLayerRenderer.ResolveDouble(context, "glow", null, defaultValue: 0.6), 0.0, 2.0);
        var twinkle = Math.Clamp(BricksLayerRenderer.ResolveDouble(context, "twinkle", null, defaultValue: 0.3), 0.0, 1.0);
        var phase = BricksLayerRenderer.ResolveDouble(context, "phase", null, defaultValue: 0.0);

        var bgColor = CheckerboardLayerRenderer.ResolveColor(context, "background", new ColorRgba(6, 9, 20, 255));
        var baseStarColor = CheckerboardLayerRenderer.ResolveColor(context, "color", ColorRgba.White);
        ColorRamp? ramp = null;
        if (context.LayerProperties.TryGetProperty("color", out var colVal) && colVal is GfxColorRamp cr)
        {
            ramp = cr.Ramp;
        }

        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(bgColor.ToSKColor());

        using var corePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var glowPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        for (var i = 0; i < count; i++)
        {
            var sx = (float)(context.Random.NextDouble() * width);
            var sy = (float)(context.Random.NextDouble() * height);
            var sizeT = Math.Pow(context.Random.NextDouble(), 2.2); // More small stars, fewer bright giants
            var radius = (float)(minRadius + ((maxRadius - minRadius) * sizeT));
            var colorT = (float)context.Random.NextDouble();
            var twinklePhase = context.Random.NextDouble() * Math.Tau;

            var brightness = 0.65 + (0.35 * sizeT);
            if (twinkle > 0.0)
            {
                var wave = 0.5 + (0.5 * Math.Sin((phase * Math.Tau) + twinklePhase));
                brightness *= (1.0 - (twinkle * 0.6)) + (wave * twinkle * 0.6);
            }

            var starCol = ramp is not null ? ramp.Sample(colorT) : baseStarColor;
            var alpha = (byte)Math.Clamp(Math.Round(starCol.A * brightness), 0, 255);

            if (glow > 0.0 && radius > 0.9f)
            {
                var glowAlpha = (byte)Math.Clamp(Math.Round(alpha * 0.25 * glow), 0, 255);
                glowPaint.Color = new SKColor(starCol.R, starCol.G, starCol.B, glowAlpha);
                canvas.DrawCircle(sx, sy, radius * (float)(1.8 + glow), glowPaint);
            }

            corePaint.Color = new SKColor(starCol.R, starCol.G, starCol.B, alpha);
            canvas.DrawCircle(sx, sy, radius, corePaint);
        }

        canvas.Flush();
        return bitmap;
    }
}
