using System.Runtime.InteropServices;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Rendering.Layers.Patterns;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Layers;

/// <summary>
/// Renders deterministic Gray-Scott reaction-diffusion Turing patterns (<c>type = reaction_diffusion</c> / <c>turing</c>)
/// with presets (<c>coral</c>, <c>spots</c>, <c>fingerprints</c>, <c>maze</c>, <c>mitosis</c>, <c>worms</c>).
/// </summary>
public sealed class ReactionDiffusionLayerRenderer(string layerType = "reaction_diffusion") : ILayerRenderer
{
    private static readonly ColorRamp DefaultRamp = new([
        new ColorStop(0.0f, new ColorRgba(15, 23, 42, 255)),
        new ColorStop(0.5f, new ColorRgba(16, 185, 129, 255)),
        new ColorStop(1.0f, new ColorRgba(236, 253, 245, 255))
    ]);

    public string LayerType { get; } = layerType;

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bitmap = context.CreateLayerBitmap();
        var preset = BricksLayerRenderer.ResolveString(context, "preset", "style", defaultValue: "coral").ToLowerInvariant();
        var (defaultFeed, defaultKill) = GetPresetParameters(preset);

        var feed = Math.Clamp(BricksLayerRenderer.ResolveDouble(context, "feed", "f", defaultValue: defaultFeed), 0.005, 0.12);
        var kill = Math.Clamp(BricksLayerRenderer.ResolveDouble(context, "kill", "k", defaultValue: defaultKill), 0.01, 0.10);
        var du = Math.Clamp(BricksLayerRenderer.ResolveDouble(context, "du", null, defaultValue: 1.0), 0.05, 2.0);
        var dv = Math.Clamp(BricksLayerRenderer.ResolveDouble(context, "dv", null, defaultValue: 0.5), 0.05, 2.0);
        var iterations = Math.Clamp((int)BricksLayerRenderer.ResolveDouble(context, "iterations", "steps", defaultValue: 350.0), 10, 4000);
        var seedPattern = BricksLayerRenderer.ResolveString(context, "seed_pattern", "seed_mode", defaultValue: "spots").ToLowerInvariant();
        var scale = Math.Clamp(BricksLayerRenderer.ResolveDouble(context, "scale", null, defaultValue: 1.0), 0.5, 8.0);

        ColorRamp? ramp = DefaultRamp;
        ColorRgba? singleColor = null;
        var bgColor = new ColorRgba(15, 23, 42, 255);
        if (context.LayerProperties.TryGetProperty("background", out var bgVal) && bgVal is GfxColor bgC)
        {
            bgColor = bgC.Color;
        }

        if (context.LayerProperties.TryGetProperty("color", out var colVal))
        {
            if (colVal is GfxColorRamp cr)
            {
                ramp = cr.Ramp;
            }
            else if (colVal is GfxColor c)
            {
                singleColor = c.Color;
                ramp = null;
            }
        }

        var width = bitmap.Width;
        var height = bitmap.Height;
        var simW = Math.Clamp((int)Math.Round(width / scale), 16, 256);
        var simH = Math.Clamp((int)Math.Round(height / scale), 16, 256);

        var u = new float[simW * simH];
        var v = new float[simW * simH];
        var nextU = new float[simW * simH];
        var nextV = new float[simW * simH];

        Array.Fill(u, 1.0f);
        SeedInitialChemicalV(u, v, simW, simH, seedPattern, context);

        var f = (float)feed;
        var k = (float)kill;
        var fPlusK = f + k;
        var diffU = (float)du;
        var diffV = (float)dv;

        for (var step = 0; step < iterations; step++)
        {
            for (var y = 0; y < simH; y++)
            {
                var ym1 = ((y - 1 + simH) % simH) * simW;
                var y0 = y * simW;
                var yp1 = ((y + 1) % simH) * simW;

                for (var x = 0; x < simW; x++)
                {
                    var xm1 = (x - 1 + simW) % simW;
                    var xp1 = (x + 1) % simW;
                    var idx = y0 + x;

                    var curU = u[idx];
                    var curV = v[idx];

                    // 9-point isotropic Laplacian stencil
                    var lapU =
                        (0.05f * (u[ym1 + xm1] + u[ym1 + xp1] + u[yp1 + xm1] + u[yp1 + xp1])) +
                        (0.20f * (u[ym1 + x] + u[yp1 + x] + u[y0 + xm1] + u[y0 + xp1])) -
                        curU;

                    var lapV =
                        (0.05f * (v[ym1 + xm1] + v[ym1 + xp1] + v[yp1 + xm1] + v[yp1 + xp1])) +
                        (0.20f * (v[ym1 + x] + v[yp1 + x] + v[y0 + xm1] + v[y0 + xp1])) -
                        curV;

                    var uvv = curU * curV * curV;
                    nextU[idx] = Math.Clamp(curU + ((diffU * lapU) - uvv + (f * (1.0f - curU))), 0.0f, 1.0f);
                    nextV[idx] = Math.Clamp(curV + ((diffV * lapV) + uvv - (fPlusK * curV)), 0.0f, 1.0f);
                }
            }

            (u, nextU) = (nextU, u);
            (v, nextV) = (nextV, v);
        }

        // Normalize V to [0, 1] for crisp visual contrast across presets
        var minV = float.MaxValue;
        var maxV = float.MinValue;
        for (var i = 0; i < v.Length; i++)
        {
            if (v[i] < minV) minV = v[i];
            if (v[i] > maxV) maxV = v[i];
        }

        var rangeV = Math.Max(0.05f, maxV - minV);
        var pixelBuffer = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var sy = (y + 0.5) * simH / height - 0.5;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var sx = (x + 0.5) * simW / width - 0.5;
                var val = SampleBilinearWrapped(v, simW, simH, sx, sy);
                var norm = Math.Clamp((val - minV) / rangeV, 0.0f, 1.0f);

                var chosen = ramp is not null
                    ? ramp.Sample(norm)
                    : TruchetLayerRenderer.LerpColor(bgColor, singleColor!.Value, norm);

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), chosen);
            }
        }

        Marshal.Copy(pixelBuffer, 0, bitmap.GetPixels(), pixelBuffer.Length);
        return bitmap;
    }

    private static (double Feed, double Kill) GetPresetParameters(string preset) => preset switch
    {
        "spots" or "leopard" => (0.030, 0.062),
        "fingerprints" or "maze" or "labyrinth" => (0.029, 0.057),
        "mitosis" or "cells" => (0.0367, 0.0649),
        "worms" or "stripes" => (0.058, 0.065),
        "solitons" => (0.030, 0.060),
        _ => (0.0545, 0.062) // "coral"
    };

    private static void SeedInitialChemicalV(float[] u, float[] v, int simW, int simH, string seedPattern, LayerRenderContext context)
    {
        if (seedPattern == "center")
        {
            var cx = simW / 2;
            var cy = simH / 2;
            var r = Math.Max(3, Math.Min(simW, simH) / 6);
            SeedDisc(u, v, simW, simH, cx, cy, r, context);
        }
        else if (seedPattern == "random")
        {
            for (var i = 0; i < v.Length; i++)
            {
                if (context.Random.NextDouble() < 0.18)
                {
                    u[i] = 0.5f;
                    v[i] = 0.25f + (float)(context.Random.NextDouble() * 0.25);
                }
            }
        }
        else
        {
            // Default "spots": scatter multiple deterministic nucleations across the grid
            var spotCount = Math.Clamp((simW * simH) / 140, 8, 64);
            var maxR = Math.Max(2, Math.Min(simW, simH) / 12);
            for (var s = 0; s < spotCount; s++)
            {
                var cx = context.Random.NextInt(0, simW);
                var cy = context.Random.NextInt(0, simH);
                var r = context.Random.NextInt(2, maxR + 1);
                SeedDisc(u, v, simW, simH, cx, cy, r, context);
            }
        }
    }

    private static void SeedDisc(float[] u, float[] v, int simW, int simH, int cx, int cy, int r, LayerRenderContext context)
    {
        var rSq = r * r;
        for (var dy = -r; dy <= r; dy++)
        {
            var py = (cy + dy + simH) % simH;
            for (var dx = -r; dx <= r; dx++)
            {
                if ((dx * dx) + (dy * dy) <= rSq)
                {
                    var px = (cx + dx + simW) % simW;
                    var idx = (py * simW) + px;
                    u[idx] = 0.50f + (float)((context.Random.NextDouble() - 0.5) * 0.04);
                    v[idx] = 0.25f + (float)((context.Random.NextDouble() - 0.5) * 0.04);
                }
            }
        }
    }

    private static float SampleBilinearWrapped(float[] grid, int w, int h, double sx, double sy)
    {
        var x0 = (int)Math.Floor(sx);
        var y0 = (int)Math.Floor(sy);
        var tx = (float)(sx - x0);
        var ty = (float)(sy - y0);

        var wx0 = ((x0 % w) + w) % w;
        var wx1 = (wx0 + 1) % w;
        var wy0 = ((y0 % h) + h) % h;
        var wy1 = (wy0 + 1) % h;

        var v00 = grid[(wy0 * w) + wx0];
        var v10 = grid[(wy0 * w) + wx1];
        var v01 = grid[(wy1 * w) + wx0];
        var v11 = grid[(wy1 * w) + wx1];

        var top = v00 + ((v10 - v00) * tx);
        var bot = v01 + ((v11 - v01) * tx);
        return top + ((bot - top) * ty);
    }
}
