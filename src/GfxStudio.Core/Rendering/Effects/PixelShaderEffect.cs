using System.Globalization;
using System.Runtime.InteropServices;
using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Noise;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Randomness;
using GfxStudio.Core.Randomness.Seeding;
using GfxStudio.Core.Rendering.Layers;
using GfxStudio.Core.Scripting;
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Function;
using Jint.Native.Object;
using Jint.Runtime;
using SkiaSharp;

namespace GfxStudio.Core.Rendering.Effects;

/// <summary>
/// Programmable per-pixel shader effect and layer generator (<c>shader</c> / <c>pixel_shader</c>).
/// Supports both JavaScript per-pixel functions <c>(u, v, ctx, x, y)</c> returning <c>[r, g, b]</c>,
/// <c>[r, g, b, a]</c>, <c>{ r, g, b }</c>, <c>{ r, g, b, a }</c>, or a scalar in <c>[0.0, 1.0]</c>,
/// as well as hardware/SIMD-accelerated SkSL fragment shaders via <see cref="SKRuntimeEffect"/>.
/// </summary>
public sealed class PixelShaderEffect(string effectType = "shader") : ILayerEffect
{
    public string EffectType { get; } = effectType;

    public void Apply(SKBitmap target, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        using var ownedSampleSource = context.SampleSource is null ? target.Copy() : null;
        var sampleSource = context.SampleSource ?? ownedSampleSource!;

        // 1. Check for SkSL shader source (`sksl = "..."`)
        if (context.Properties.TryGetProperty("sksl", out var skslVal))
        {
            if (skslVal is GfxString skslStr && !string.IsNullOrWhiteSpace(skslStr.Value))
            {
                ExecuteSkslShader(target, sampleSource, skslStr.Value, context);
                return;
            }

            context.AddDiagnostic("SHD001", $"Property 'sksl' on layer '{context.LayerId}' must be a non-empty SkSL string.");
            return;
        }

        // 2. Resolve JavaScript or custom C# per-pixel callable
        var colorRamp = ResolveOptionalColorRamp(context);
        if (!TryResolveShaderCallable(context, out var jsHost, out var jsFunction, out var customCallable))
        {
            return;
        }

        if (jsHost is not null && jsFunction is not null)
        {
            ExecuteJavaScriptPixelShader(target, sampleSource, jsHost, jsFunction, colorRamp, context);
        }
        else if (customCallable is not null)
        {
            ExecuteCustomCallablePixelShader(target, sampleSource, customCallable, colorRamp, context);
        }
    }

    private static bool TryResolveShaderCallable(
        EffectContext context,
        out JsScriptHost? jsHost,
        out Function? jsFunction,
        out IGfxCallable? customCallable)
    {
        jsHost = null;
        jsFunction = null;
        customCallable = null;

        GfxValue? fnVal = null;
        string[] candidateProps = ["fn", "shader", "pixel_fn", "function", "callback", "code"];
        foreach (var prop in candidateProps)
        {
            if (context.Properties.TryGetProperty(prop, out var v))
            {
                fnVal = v;
                break;
            }
        }

        if (fnVal is null)
        {
            context.AddDiagnostic(
                "SHD001",
                $"Shader on layer '{context.LayerId}' requires a 'fn', 'code', or 'sksl' property.");
            return false;
        }

        if (fnVal is GfxFunctionRef fnRef)
        {
            if (fnRef.Callable is JsCallable jsCall && jsCall.Host.TryGetExportedFunction(jsCall.Name, out var exportedFn))
            {
                jsHost = jsCall.Host;
                jsFunction = exportedFn;
                return true;
            }

            customCallable = fnRef.Callable;
            return true;
        }

        var rawText = fnVal switch
        {
            GfxSymbol sym => sym.Identifier,
            GfxString str => str.Value,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(rawText))
        {
            context.AddDiagnostic(
                "SHD001",
                $"Shader 'fn' property on layer '{context.LayerId}' must be a function reference or inline JavaScript function string.");
            return false;
        }

        // Check if rawText matches a registered function in EvaluationContext
        if (context.EvaluationContext is not null && context.EvaluationContext.TryGetFunction(rawText, out var registeredCallable))
        {
            if (registeredCallable is JsCallable jsCall && jsCall.Host.TryGetExportedFunction(jsCall.Name, out var exportedFn))
            {
                jsHost = jsCall.Host;
                jsFunction = exportedFn;
                return true;
            }

            customCallable = registeredCallable;
            return true;
        }

        // Otherwise compile rawText as an inline JavaScript function or arrow expression
        try
        {
            var inlineScript = BuildInlineShaderModuleSource(rawText);
            var host = new JsScriptHost(inlineScript, context.Span.FilePath ?? "<inline_shader>", context.EvaluationContext);
            if (host.TryGetExportedFunction("pixelShader", out var compiledFn))
            {
                jsHost = host;
                jsFunction = compiledFn;
                return true;
            }

            context.AddDiagnostic(
                "SHD002",
                $"Inline shader code on layer '{context.LayerId}' did not evaluate to a callable JavaScript function.");
            return false;
        }
        catch (GfxException ex)
        {
            foreach (var diag in ex.Diagnostics)
            {
                context.AddDiagnostic("SHD002", diag.Message);
            }

            return false;
        }
    }

    private static string BuildInlineShaderModuleSource(string rawCode)
    {
        var trimmed = rawCode.Trim();
        // If it looks like a function declaration, arrow function, or parenthesized function expression:
        if (trimmed.Contains("=>", StringComparison.Ordinal)
            || trimmed.StartsWith("function", StringComparison.Ordinal))
        {
            return $"exports.pixelShader = function(u, v, ctx, x, y) {{ return ({trimmed})(u, v, ctx, x, y); }};";
        }

        // Otherwise treat as a function body or return expression
        if (trimmed.Contains("return ", StringComparison.Ordinal) || trimmed.Contains(';'))
        {
            return $"exports.pixelShader = function(u, v, ctx, x, y) {{ {trimmed} }};";
        }

        return $"exports.pixelShader = function(u, v, ctx, x, y) {{ return ({trimmed}); }};";
    }

    private static void ExecuteJavaScriptPixelShader(
        SKBitmap target,
        SKBitmap sampleSource,
        JsScriptHost host,
        Function jsFunction,
        ColorRamp? colorRamp,
        EffectContext context)
    {
        var engine = host.Engine;
        var width = target.Width;
        var height = target.Height;
        var pixelBuffer = new byte[width * height * 4];

        var layerSeed = context.Random.NextUInt64();
        var perlin = new PerlinNoise(new GfxRandom(SeedMixer.Mix(layerSeed ^ 0x9E3779B97F4A7C15UL)));
        var fbmDefault = new FractalBrownianMotion(perlin, octaves: 4, persistence: 0.5, lacunarity: 2.0);
        var worley = new WorleyNoise(new GfxRandom(SeedMixer.Mix(layerSeed ^ 0xBF58476D1CE4E5B9UL)));

        var evalCtx = context.EvaluationContext;
        var run = evalCtx?.Run ?? new RunContext(0, 1, layerSeed);
        var frame = evalCtx?.Frame ?? new FrameContext(0, 1, 15.0);

        // Bind deterministic noise & sampling intrinsics
        var noiseFn = (Func<double, double, JsValue, double>)((nx, ny, nzVal) =>
        {
            var nz = nzVal is not null && nzVal.IsNumber() ? nzVal.AsNumber() : 0.0;
            return Math.Clamp((perlin.Sample3D(nx, ny, nz) * 0.7) + 0.5, 0.0, 1.0);
        });

        var signedNoiseFn = (Func<double, double, JsValue, double>)((nx, ny, nzVal) =>
        {
            var nz = nzVal is not null && nzVal.IsNumber() ? nzVal.AsNumber() : 0.0;
            return Math.Clamp(perlin.Sample3D(nx, ny, nz), -1.0, 1.0);
        });

        var fbmFn = (Func<double, double, JsValue, JsValue, double>)((nx, ny, nzVal, octVal) =>
        {
            var nz = nzVal is not null && nzVal.IsNumber() ? nzVal.AsNumber() : 0.0;
            if (octVal is not null && octVal.IsNumber())
            {
                var oct = Math.Clamp((int)Math.Round(octVal.AsNumber()), 1, 12);
                if (oct != 4)
                {
                    var customFbm = new FractalBrownianMotion(perlin, oct, 0.5, 2.0);
                    return customFbm.Sample3D(nx, ny, nz);
                }
            }

            return fbmDefault.Sample3D(nx, ny, nz);
        });

        var voronoiFn = (Func<double, double, JsValue, double>)((nx, ny, jitterVal) =>
        {
            var jitter = jitterVal is not null && jitterVal.IsNumber() ? jitterVal.AsNumber() : 1.0;
            return worley.Sample2D(nx, ny, jitter);
        });

        var hashFn = (Func<double, double, double>)((hx, hy) =>
        {
            var ix = unchecked((ulong)BitConverter.DoubleToInt64Bits(hx));
            var iy = unchecked((ulong)BitConverter.DoubleToInt64Bits(hy));
            var h = SeedMixer.Mix(layerSeed ^ (ix * 0x517CC1B727220A95UL) ^ (iy * 0x85EBCA6B27D4EB4FUL));
            return (h >> 11) * (1.0 / (1UL << 53));
        });

        var sampleFn = (Func<double, double, JsValue, ObjectInstance>)((su, sv, wrapVal) =>
        {
            var wrap = wrapVal is not null && wrapVal.IsBoolean() && wrapVal.AsBoolean();
            return SampleNormalizedBitmapToJsObject(engine, sampleSource, su, sv, wrap);
        });

        var sampleLayerFn = (Func<string, double, double, JsValue, ObjectInstance>)((layerName, su, sv, wrapVal) =>
        {
            var wrap = wrapVal is not null && wrapVal.IsBoolean() && wrapVal.AsBoolean();
            var otherLayer = context.ResolveLayerBitmap?.Invoke(layerName);
            if (otherLayer is null)
            {
                return CreateRgbaJsObject(engine, 0.0, 0.0, 0.0, 0.0);
            }

            return SampleNormalizedBitmapToJsObject(engine, otherLayer, su, sv, wrap);
        });

        engine.SetValue("noise", noiseFn);
        engine.SetValue("signedNoise", signedNoiseFn);
        engine.SetValue("fbm", fbmFn);
        engine.SetValue("voronoi", voronoiFn);
        engine.SetValue("hash", hashFn);
        engine.SetValue("sample", sampleFn);
        engine.SetValue("sampleLayer", sampleLayerFn);

        var paramsJs = ResolveShaderParamsJs(engine, context);

        // Pre-allocate reusable pixel & ctx objects so the inner loop allocates zero wrapper dictionaries
        var pixelObj = CreateRgbaJsObject(engine, 0.0, 0.0, 0.0, 0.0);
        var ctxObj = engine.Intrinsics.Object.Construct(Arguments.Empty);
        ctxObj.Set("width", new JsNumber(width));
        ctxObj.Set("height", new JsNumber(height));
        ctxObj.Set("t", new JsNumber(frame.Progress));
        ctxObj.Set("progress", new JsNumber(frame.Progress));
        ctxObj.Set("time", new JsNumber(frame.Time));
        ctxObj.Set("seed", new JsNumber((double)(layerSeed & 0x1FFFFFFFFFFFFFUL)));
        ctxObj.Set("pixel", pixelObj);
        ctxObj.Set("params", paramsJs);
        ctxObj.Set("uniforms", paramsJs);
        ctxObj.Set("noise", engine.GetValue("noise"));
        ctxObj.Set("signedNoise", engine.GetValue("signedNoise"));
        ctxObj.Set("fbm", engine.GetValue("fbm"));
        ctxObj.Set("voronoi", engine.GetValue("voronoi"));
        ctxObj.Set("hash", engine.GetValue("hash"));
        ctxObj.Set("sample", engine.GetValue("sample"));
        ctxObj.Set("sampleLayer", engine.GetValue("sampleLayer"));
        ctxObj.Set("run", JsValue.FromObject(engine, new Dictionary<string, object>
        {
            ["number"] = run.Number,
            ["index"] = run.Number,
            ["count"] = run.Count,
            ["seed"] = (double)(run.Seed & 0x1FFFFFFFFFFFFFUL)
        }));
        ctxObj.Set("frame", JsValue.FromObject(engine, new Dictionary<string, object>
        {
            ["number"] = frame.Number,
            ["index"] = frame.Number,
            ["count"] = frame.Count,
            ["time"] = frame.Time,
            ["progress"] = frame.Progress,
            ["t"] = frame.Progress
        }));

        engine.SetValue("ctx", ctxObj);

        var callArgs = new JsValue[5];
        callArgs[2] = ctxObj;

        var invW = width > 1 ? 1.0 / (width - 1.0) : 0.0;
        var invH = height > 1 ? 1.0 / (height - 1.0) : 0.0;

        try
        {
            for (var y = 0; y < height; y++)
            {
                // Reset statement/timeout constraints per scanline so high-res canvases never trip false-positive loop limits
                engine.Constraints.Reset();

                var v = width == 1 && height == 1 ? 0.5 : y * invH;
                var jsV = new JsNumber(v);
                var jsY = new JsNumber(y);
                ctxObj.Set("v", jsV);
                ctxObj.Set("y", jsY);
                callArgs[1] = jsV;
                callArgs[4] = jsY;

                var rowOffset = y * width * 4;

                for (var x = 0; x < width; x++)
                {
                    var u = width == 1 && height == 1 ? 0.5 : x * invW;
                    var jsU = new JsNumber(u);
                    var jsX = new JsNumber(x);
                    ctxObj.Set("u", jsU);
                    ctxObj.Set("x", jsX);
                    callArgs[0] = jsU;
                    callArgs[3] = jsX;

                    var srcPx = sampleSource.GetPixel(x, y);
                    pixelObj.Set("r", new JsNumber(srcPx.Red / 255.0));
                    pixelObj.Set("g", new JsNumber(srcPx.Green / 255.0));
                    pixelObj.Set("b", new JsNumber(srcPx.Blue / 255.0));
                    pixelObj.Set("a", new JsNumber(srcPx.Alpha / 255.0));

                    var result = jsFunction.Call(JsValue.Undefined, callArgs);
                    if (!TryUnpackShaderColor(result, colorRamp, out var outColor))
                    {
                        context.AddDiagnostic(
                            "SHD003",
                            $"Pixel shader on layer '{context.LayerId}' returned an invalid value at ({x}, {y}). Expected [r, g, b], [r, g, b, a], {{ r, g, b }}, {{ r, g, b, a }}, or a scalar number in 0.0..1.0.");
                        return;
                    }

                    WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), outColor);
                }
            }
        }
        catch (JavaScriptException jsEx)
        {
            context.AddDiagnostic("SHD002", $"JavaScript shader runtime error on layer '{context.LayerId}': {jsEx.Message}");
            return;
        }
        catch (StatementsCountOverflowException)
        {
            context.AddDiagnostic("SHD002", $"JavaScript shader on layer '{context.LayerId}' exceeded maximum statement count (infinite loop detected).");
            return;
        }
        catch (RecursionDepthOverflowException)
        {
            context.AddDiagnostic("SHD002", $"JavaScript shader on layer '{context.LayerId}' exceeded maximum recursion depth.");
            return;
        }
        catch (TimeoutException)
        {
            context.AddDiagnostic("SHD002", $"JavaScript shader on layer '{context.LayerId}' timed out.");
            return;
        }

        Marshal.Copy(pixelBuffer, 0, target.GetPixels(), pixelBuffer.Length);
    }

    private static void ExecuteCustomCallablePixelShader(
        SKBitmap target,
        SKBitmap sampleSource,
        IGfxCallable callable,
        ColorRamp? colorRamp,
        EffectContext context)
    {
        var width = target.Width;
        var height = target.Height;
        var pixelBuffer = new byte[width * height * 4];
        var evalCtx = context.EvaluationContext ?? new EvaluationContext();
        var invW = width > 1 ? 1.0 / (width - 1.0) : 0.0;
        var invH = height > 1 ? 1.0 / (height - 1.0) : 0.0;

        for (var y = 0; y < height; y++)
        {
            var v = y * invH;
            var rowOffset = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var u = x * invW;
                var result = callable.Invoke(
                    [new GfxNumber(u), new GfxNumber(v), new GfxNumber(x), new GfxNumber(y)],
                    evalCtx,
                    context.Span);

                if (!TryUnpackGfxShaderColor(result, colorRamp, out var outColor))
                {
                    context.AddDiagnostic(
                        "SHD003",
                        $"Pixel shader on layer '{context.LayerId}' returned an invalid value at ({x}, {y}).");
                    return;
                }

                WorleyLayerRenderer.WritePremulPixel(pixelBuffer, rowOffset + (x * 4), outColor);
            }
        }

        Marshal.Copy(pixelBuffer, 0, target.GetPixels(), pixelBuffer.Length);
    }

    private static void ExecuteSkslShader(
        SKBitmap target,
        SKBitmap sampleSource,
        string skslSource,
        EffectContext context)
    {
        // 1. Validate SkSL syntax and types using Skia's native SkSL compiler
        using var effect = SKRuntimeEffect.Create(skslSource, out var errorText);
        if (effect is null)
        {
            context.AddDiagnostic("SHD004", $"SkSL compilation failed on layer '{context.LayerId}': {errorText}");
            return;
        }

        // 2. Transpile validated SkSL to our deterministic JS shader runtime (avoiding Skia 2.88 SkVM JIT AVX illegal instruction crash)
        var jsSource = TranspileValidatedSkslToJs(skslSource);
        try
        {
            var host = new JsScriptHost(jsSource, context.Span.FilePath ?? "<sksl_shader>", context.EvaluationContext);
            if (!host.TryGetExportedFunction("pixelShader", out var compiledFn))
            {
                context.AddDiagnostic("SHD004", $"SkSL shader on layer '{context.LayerId}' must define a 'main' entry point.");
                return;
            }

            BindSkslUniformGlobals(host.Engine, target.Width, target.Height, context);
            var colorRamp = ResolveOptionalColorRamp(context);
            ExecuteJavaScriptPixelShader(target, sampleSource, host, compiledFn, colorRamp, context);
        }
        catch (GfxException ex)
        {
            foreach (var diag in ex.Diagnostics)
            {
                context.AddDiagnostic("SHD004", diag.Message);
            }
        }
    }

    private static string TranspileValidatedSkslToJs(string sksl)
    {
        // Strip uniform declarations (they are bound as globals on the JS engine)
        var body = System.Text.RegularExpressions.Regex.Replace(
            sksl,
            @"\buniform\s+(?:shader|[a-zA-Z0-9_]+)\s+[a-zA-Z_][a-zA-Z0-9_]*\s*;",
            string.Empty);

        // Replace `main(float2 fragCoord)` / `main(vec2 p)` signature
        var mainMatch = System.Text.RegularExpressions.Regex.Match(
            body,
            @"\b(?:half4|float4|vec4)\s+main\s*\(\s*(?:float2|vec2|half2)\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\)\s*\{");
        var coordVar = mainMatch.Success ? mainMatch.Groups[1].Value : "fragCoord";
        if (mainMatch.Success)
        {
            body = body[..mainMatch.Index]
                + $"exports.pixelShader = function(u, v, ctx, x, y) {{ const {coordVar} = __vec2(x, y); "
                + body[(mainMatch.Index + mainMatch.Length)..];
        }

        // Replace `coordVar / u_resolution` shorthand with `__vec2(u, v)`
        body = System.Text.RegularExpressions.Regex.Replace(
            body,
            $@"\b{System.Text.RegularExpressions.Regex.Escape(coordVar)}\s*/\s*u_resolution\b",
            "__vec2(u, v)");

        // Replace C/GLSL typed variable declarations (`float2 uv =`, `float d =`, etc.) with `let <name> =`
        body = System.Text.RegularExpressions.Regex.Replace(
            body,
            @"\b(?:float2|float3|float4|half2|half3|half4|vec2|vec3|vec4|float|half|int|bool)\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*=",
            "let $1 =");

        // Replace vector constructors with JS helper calls
        body = System.Text.RegularExpressions.Regex.Replace(body, @"\b(?:float2|half2|vec2)\s*\(", "__vec2(");
        body = System.Text.RegularExpressions.Regex.Replace(body, @"\b(?:float3|half3|vec3)\s*\(", "__vec3(");
        body = System.Text.RegularExpressions.Regex.Replace(body, @"\b(?:float4|half4|vec4)\s*\(", "__vec4(");

        const string preamble = """
            function __vec2(x, y) {
                if (y === undefined) y = x;
                const a = [x, y];
                a.x = x; a.y = y; a.r = x; a.g = y;
                return a;
            }
            function __vec3(x, y, z) {
                if (y === undefined) { y = x; z = x; }
                const a = [x, y, z];
                a.x = x; a.y = y; a.z = z; a.r = x; a.g = y; a.b = z;
                return a;
            }
            function __vec4(r, g, b, w) {
                if (g === undefined) { g = r; b = r; w = r; }
                else if (Array.isArray(r) && r.length === 3 && g !== undefined) { w = g; b = r[2]; g = r[1]; r = r[0]; }
                const a = [r, g, b, w];
                a.x = r; a.y = g; a.z = b; a.w = w;
                a.r = r; a.g = g; a.b = b; a.a = w;
                return a;
            }
            """;

        return preamble + "\n" + body;
    }

    private static void BindSkslUniformGlobals(Engine engine, int width, int height, EffectContext context)
    {
        var evalCtx = context.EvaluationContext;
        var frame = evalCtx?.Frame ?? new FrameContext(0, 1, 15.0);
        var layerSeed = context.Random.NextUInt64();

        var vec2Fn = engine.GetValue("__vec2").AsFunctionInstance();
        var vec4Fn = engine.GetValue("__vec4").AsFunctionInstance();

        engine.SetValue("u_resolution", vec2Fn.Call(JsValue.Undefined, [new JsNumber(width), new JsNumber(height)]));
        engine.SetValue("u_time", new JsNumber(frame.Time));
        engine.SetValue("u_progress", new JsNumber(frame.Progress));
        engine.SetValue("u_t", new JsNumber(frame.Progress));
        engine.SetValue("u_seed", new JsNumber((double)(layerSeed % 100000UL)));

        if ((context.Properties.TryGetProperty("params", out var paramsVal) || context.Properties.TryGetProperty("uniforms", out paramsVal))
            && paramsVal is GfxObject paramsObj)
        {
            foreach (var (key, val) in paramsObj.Properties)
            {
                switch (val)
                {
                    case GfxNumber num:
                        engine.SetValue(key, new JsNumber(num.Value));
                        break;
                    case GfxColor col:
                        engine.SetValue(key, vec4Fn.Call(JsValue.Undefined, [
                            new JsNumber(col.Color.R / 255.0),
                            new JsNumber(col.Color.G / 255.0),
                            new JsNumber(col.Color.B / 255.0),
                            new JsNumber(col.Color.A / 255.0)
                        ]));
                        break;
                    case GfxDimensions dims:
                        engine.SetValue(key, vec2Fn.Call(JsValue.Undefined, [
                            new JsNumber(dims.Dimensions.Width),
                            new JsNumber(dims.Dimensions.Height)
                        ]));
                        break;
                    default:
                        if (val.TryCoerceToPoint2D(out var pt))
                        {
                            engine.SetValue(key, vec2Fn.Call(JsValue.Undefined, [new JsNumber(pt.X), new JsNumber(pt.Y)]));
                        }

                        break;
                }
            }
        }
    }

    private static bool TryUnpackShaderColor(JsValue result, ColorRamp? colorRamp, out ColorRgba color)
    {
        color = default;
        if (result is null || result.IsUndefined() || result.IsNull())
        {
            return false;
        }

        if (result.IsNumber())
        {
            var scalar = (float)Math.Clamp(result.AsNumber(), 0.0, 1.0);
            if (colorRamp is not null)
            {
                color = colorRamp.Sample(scalar);
            }
            else
            {
                var b = (byte)Math.Round(scalar * 255.0f);
                color = new ColorRgba(b, b, b, 255);
            }

            return true;
        }

        if (result is ArrayInstance arr)
        {
            var len = (int)arr.Get("length").AsNumber();
            if (len < 3)
            {
                return false;
            }

            var v0 = arr.Get("0");
            var v1 = arr.Get("1");
            var v2 = arr.Get("2");
            if (!v0.IsNumber() || !v1.IsNumber() || !v2.IsNumber())
            {
                return false;
            }

            var r = ClampUnitToByte(v0.AsNumber());
            var g = ClampUnitToByte(v1.AsNumber());
            var b = ClampUnitToByte(v2.AsNumber());
            byte a = 255;
            if (len >= 4)
            {
                var v3 = arr.Get("3");
                if (!v3.IsNumber())
                {
                    return false;
                }

                a = ClampUnitToByte(v3.AsNumber());
            }

            color = new ColorRgba(r, g, b, a);
            return true;
        }

        if (result is ObjectInstance obj)
        {
            var rVal = obj.Get("r");
            var gVal = obj.Get("g");
            var bVal = obj.Get("b");
            if (!rVal.IsNumber() || !gVal.IsNumber() || !bVal.IsNumber())
            {
                return false;
            }

            var r = ClampUnitToByte(rVal.AsNumber());
            var g = ClampUnitToByte(gVal.AsNumber());
            var b = ClampUnitToByte(bVal.AsNumber());
            byte a = 255;
            var aVal = obj.Get("a");
            if (!aVal.IsUndefined() && !aVal.IsNull())
            {
                if (!aVal.IsNumber())
                {
                    return false;
                }

                a = ClampUnitToByte(aVal.AsNumber());
            }

            color = new ColorRgba(r, g, b, a);
            return true;
        }

        return false;
    }

    private static bool TryUnpackGfxShaderColor(GfxValue result, ColorRamp? colorRamp, out ColorRgba color)
    {
        color = default;
        switch (result)
        {
            case GfxNumber num:
            {
                var scalar = (float)Math.Clamp(num.Value, 0.0, 1.0);
                if (colorRamp is not null)
                {
                    color = colorRamp.Sample(scalar);
                }
                else
                {
                    var b = (byte)Math.Round(scalar * 255.0f);
                    color = new ColorRgba(b, b, b, 255);
                }

                return true;
            }

            case GfxArray arr when arr.Items.Count >= 3:
            {
                if (arr.Items[0] is not GfxNumber rNum
                    || arr.Items[1] is not GfxNumber gNum
                    || arr.Items[2] is not GfxNumber bNum)
                {
                    return false;
                }

                var a = byte.MaxValue;
                if (arr.Items.Count >= 4)
                {
                    if (arr.Items[3] is not GfxNumber aNum)
                    {
                        return false;
                    }

                    a = ClampUnitToByte(aNum.Value);
                }

                color = new ColorRgba(
                    ClampUnitToByte(rNum.Value),
                    ClampUnitToByte(gNum.Value),
                    ClampUnitToByte(bNum.Value),
                    a);
                return true;
            }

            case GfxObject obj:
            {
                if (!obj.TryGetProperty("r", out var rVal) || rVal is not GfxNumber rNum
                    || !obj.TryGetProperty("g", out var gVal) || gVal is not GfxNumber gNum
                    || !obj.TryGetProperty("b", out var bVal) || bVal is not GfxNumber bNum)
                {
                    return false;
                }

                var a = byte.MaxValue;
                if (obj.TryGetProperty("a", out var aVal))
                {
                    if (aVal is not GfxNumber aNum)
                    {
                        return false;
                    }

                    a = ClampUnitToByte(aNum.Value);
                }

                color = new ColorRgba(
                    ClampUnitToByte(rNum.Value),
                    ClampUnitToByte(gNum.Value),
                    ClampUnitToByte(bNum.Value),
                    a);
                return true;
            }

            case GfxColor gfxColor:
                color = gfxColor.Color;
                return true;

            default:
                return false;
        }
    }

    private static byte ClampUnitToByte(double unitValue)
    {
        if (double.IsNaN(unitValue))
        {
            return 0;
        }

        var clamped = Math.Clamp(unitValue, 0.0, 1.0);
        return (byte)Math.Round(clamped * 255.0);
    }

    private static ObjectInstance SampleNormalizedBitmapToJsObject(
        Engine engine,
        SKBitmap bitmap,
        double u,
        double v,
        bool wrap)
    {
        var sx = u * Math.Max(0, bitmap.Width - 1);
        var sy = v * Math.Max(0, bitmap.Height - 1);
        var px = WarpEffect.SampleBilinear(bitmap, sx, sy, wrap);
        return CreateRgbaJsObject(engine, px.Red / 255.0, px.Green / 255.0, px.Blue / 255.0, px.Alpha / 255.0);
    }

    private static ObjectInstance CreateRgbaJsObject(Engine engine, double r, double g, double b, double a)
    {
        var obj = engine.Intrinsics.Object.Construct(Arguments.Empty);
        obj.Set("r", new JsNumber(r));
        obj.Set("g", new JsNumber(g));
        obj.Set("b", new JsNumber(b));
        obj.Set("a", new JsNumber(a));
        return obj;
    }

    private static JsValue ResolveShaderParamsJs(Engine engine, EffectContext context)
    {
        if ((context.Properties.TryGetProperty("params", out var paramsVal)
            || context.Properties.TryGetProperty("uniforms", out paramsVal))
            && paramsVal is GfxObject paramsObj)
        {
            return ConvertGfxValueToShaderJs(engine, paramsObj);
        }

        return engine.Intrinsics.Object.Construct(Arguments.Empty);
    }

    private static JsValue ConvertGfxValueToShaderJs(Engine engine, GfxValue val)
    {
        return val switch
        {
            GfxNumber num => new JsNumber(num.Value),
            GfxString str => new JsString(str.Value),
            GfxBoolean b => b.Value ? JsBoolean.True : JsBoolean.False,
            GfxSymbol sym => new JsString(sym.Identifier),
            GfxColor col => CreateRgbaJsObject(
                engine,
                col.Color.R / 255.0,
                col.Color.G / 255.0,
                col.Color.B / 255.0,
                col.Color.A / 255.0),
            GfxDimensions dims => JsValue.FromObject(engine, new Dictionary<string, object>
            {
                ["width"] = dims.Dimensions.Width,
                ["height"] = dims.Dimensions.Height
            }),
            GfxArray arr => JsValue.FromObject(engine, arr.Items.Select(i => ConvertGfxValueToShaderJs(engine, i)).ToArray()),
            GfxObject obj => ConvertGfxObjectToShaderJs(engine, obj),
            _ => JsValue.Undefined
        };
    }

    private static JsValue ConvertGfxObjectToShaderJs(Engine engine, GfxObject obj)
    {
        var jsObj = engine.Intrinsics.Object.Construct(Arguments.Empty);
        foreach (var (k, v) in obj.Properties)
        {
            jsObj.Set(k, ConvertGfxValueToShaderJs(engine, v));
        }

        return jsObj;
    }

    private static ColorRamp? ResolveOptionalColorRamp(EffectContext context)
    {
        if (context.Properties.TryGetProperty("color", out var colorVal))
        {
            return colorVal switch
            {
                GfxColorRamp ramp => ramp.Ramp,
                GfxColor single => new ColorRamp([new ColorStop(0f, ColorRgba.Black), new ColorStop(1f, single.Color)]),
                _ => null
            };
        }

        return null;
    }
}
