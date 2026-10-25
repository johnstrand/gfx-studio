using System.Globalization;
using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Parsing.Ast;
using GfxStudio.Core.Primitives;
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Function;
using Jint.Native.Object;
using Jint.Runtime;

namespace GfxStudio.Core.Scripting;

/// <summary>
/// Executes external JavaScript modules via Jint, providing sandboxing, deterministic randomness,
/// and bidirectional marshaling between Jint and .gfx data structures.
/// </summary>
public sealed class JsScriptHost
{
    private readonly Engine _engine;
    private readonly Dictionary<string, Func<IReadOnlyList<JsValue>, EvaluationContext, JsValue>> _exports = new(StringComparer.Ordinal);

    public JsScriptHost(string scriptContent, string scriptPath, EvaluationContext? initialContext = null)
    {
        ArgumentNullException.ThrowIfNull(scriptContent);
        ScriptPath = scriptPath;

        _engine = new Engine(options =>
        {
            options.LimitRecursion(64);
            options.TimeoutInterval(TimeSpan.FromSeconds(2));
            options.MaxStatements(100_000);
            options.Culture(CultureInfo.InvariantCulture);
        });

        InitializeGlobalEnvironment(_engine, initialContext);
        try
        {
            LoadScript(scriptContent, scriptPath);
        }
        catch (JavaScriptException jsEx)
        {
            var line = jsEx.Location.Start.Line;
            var col = jsEx.Location.Start.Column;
            var loc = new SourceLocation(line, col, 0);
            var span = new SourceSpan(ScriptPath, loc, loc);
            var stack = string.IsNullOrWhiteSpace(jsEx.JavaScriptStackTrace) ? string.Empty : $"\n{jsEx.JavaScriptStackTrace}";
            throw new GfxException(new GfxDiagnostic("JS002", $"JavaScript error in '{Path.GetFileName(ScriptPath)}' at {line}:{col}: {jsEx.Message}{stack}", span, DiagnosticSeverity.Error));
        }
        catch (Exception ex)
        {
            var loc = new SourceLocation(1, 1, 0);
            var span = new SourceSpan(ScriptPath, loc, loc);
            throw new GfxException(new GfxDiagnostic("JS002", $"JavaScript error in '{Path.GetFileName(ScriptPath)}': {ex.Message}", span, DiagnosticSeverity.Error));
        }
    }

    public string ScriptPath { get; }

    public IReadOnlyCollection<string> ExportedFunctionNames => _exports.Keys;

    public bool HasExport(string name) => _exports.ContainsKey(name);

    public GfxValue InvokeExport(string name, IReadOnlyList<GfxValue> arguments, EvaluationContext context, SourceSpan callSpan)
    {
        if (!_exports.TryGetValue(name, out var invoker))
        {
            throw new GfxException(new GfxDiagnostic("JS001", $"Exported JavaScript function '{name}' does not exist in '{ScriptPath}'.", callSpan, DiagnosticSeverity.Error));
        }

        try
        {
            // Bind current pass random to Math.random
            _engine.SetValue("__getRandom", (Func<double>)(() => context.Random.NextDouble()));
            var jsCtx = CreateJsContext(_engine, context);
            _engine.SetValue("ctx", jsCtx);

            var jsArgs = new List<JsValue>(arguments.Count + 1);
            for (var i = 0; i < arguments.Count; i++)
            {
                jsArgs.Add(GfxJsValueConverter.ToJsValue(_engine, arguments[i]));
            }

            // Always append ctx as the trailing parameter
            jsArgs.Add(jsCtx);

            var jsResult = invoker(jsArgs, context);
            return GfxJsValueConverter.ToGfxValue(jsResult);
        }
        catch (JavaScriptException jsEx)
        {
            var line = jsEx.Location.Start.Line;
            var col = jsEx.Location.Start.Column;
            var loc = new SourceLocation(line, col, 0);
            var span = new SourceSpan(ScriptPath, loc, loc);
            var stack = string.IsNullOrWhiteSpace(jsEx.JavaScriptStackTrace) ? string.Empty : $"\n{jsEx.JavaScriptStackTrace}";
            throw new GfxException(new GfxDiagnostic("JS002", $"JavaScript error in '{Path.GetFileName(ScriptPath)}' at {line}:{col}: {jsEx.Message}{stack}", span, DiagnosticSeverity.Error));
        }
        catch (TimeoutException)
        {
            throw new GfxException(new GfxDiagnostic("JS003", $"JavaScript execution timed out in '{Path.GetFileName(ScriptPath)}'.", callSpan, DiagnosticSeverity.Error));
        }
        catch (StatementsCountOverflowException)
        {
            throw new GfxException(new GfxDiagnostic("JS004", $"JavaScript exceeded maximum statement count (infinite loop detected) in '{Path.GetFileName(ScriptPath)}'.", callSpan, DiagnosticSeverity.Error));
        }
        catch (RecursionDepthOverflowException)
        {
            throw new GfxException(new GfxDiagnostic("JS005", $"JavaScript exceeded recursion limit in '{Path.GetFileName(ScriptPath)}'.", callSpan, DiagnosticSeverity.Error));
        }
    }

    private void LoadScript(string scriptContent, string scriptPath)
    {
        // Try loading as an ES module first if it has export keyword
        var hasExportKeyword = scriptContent.Contains("export ");
        if (hasExportKeyword)
        {
            var specifier = "./" + Path.GetFileName(scriptPath);
            _engine.Modules.Add(specifier, scriptContent);
            var moduleNamespace = _engine.Modules.Import(specifier);
            foreach (var key in moduleNamespace.GetOwnPropertyKeys())
            {
                var val = moduleNamespace.Get(key);
                if (val is Function fn)
                {
                    var fnName = key.ToString();
                    _exports[fnName] = (args, ctx) => fn.Call(JsValue.Undefined, args.ToArray());
                }
            }

            if (_exports.Count > 0)
            {
                return;
            }
        }

        // Support CommonJS module.exports or top-level function declarations
        _engine.Execute("var module = { exports: {} }; var exports = module.exports;");
        _engine.Execute(scriptContent);

        // Check module.exports
        var moduleObj = _engine.GetValue("module").AsObject();
        var exportsVal = moduleObj.Get("exports");
        if (exportsVal is ObjectInstance expObj)
        {
            foreach (var key in expObj.GetOwnProperties())
            {
                var val = expObj.Get(key.Key);
                if (val is Function fn)
                {
                    var fnName = key.Key.ToString();
                    _exports[fnName] = (args, ctx) => fn.Call(JsValue.Undefined, args.ToArray());
                }
            }
        }

        // Also inspect global scope for any callable functions
        var globalObj = _engine.GetValue("globalThis").AsObject();
        foreach (var key in globalObj.GetOwnProperties())
        {
            var keyStr = key.Key.ToString();
            if (keyStr.StartsWith("__") || keyStr is "module" or "exports" or "ctx" or "Math" or "Object" or "Array" or "String" or "Number" or "Boolean" or "Function" or "JSON" or "Date" or "RegExp" or "Error" or "parseInt" or "parseFloat" or "isNaN" or "isFinite" or "globalThis" or "Infinity" or "NaN" or "undefined")
            {
                continue;
            }

            var val = globalObj.Get(key.Key);
            if (val is Function fn && !_exports.ContainsKey(keyStr))
            {
                _exports[keyStr] = (args, ctx) => fn.Call(JsValue.Undefined, args.ToArray());
            }
        }
    }

    private static void InitializeGlobalEnvironment(Engine engine, EvaluationContext? context)
    {
        engine.Execute("Math.random = function() { return typeof __getRandom === 'function' ? __getRandom() : 0; };");

        // Helper functions
        engine.SetValue("rgb", (Func<double, double, double, object>)((r, g, b) =>
            new Dictionary<string, object>
            {
                ["r"] = (int)Math.Clamp(r, 0, 255),
                ["g"] = (int)Math.Clamp(g, 0, 255),
                ["b"] = (int)Math.Clamp(b, 0, 255),
                ["a"] = 255
            }));

        engine.SetValue("rgba", (Func<double, double, double, double, object>)((r, g, b, a) =>
        {
            var alpha = a <= 1.0 ? Math.Round(a * 255.0) : a;
            return new Dictionary<string, object>
            {
                ["r"] = (int)Math.Clamp(r, 0, 255),
                ["g"] = (int)Math.Clamp(g, 0, 255),
                ["b"] = (int)Math.Clamp(b, 0, 255),
                ["a"] = (int)Math.Clamp(alpha, 0, 255)
            };
        }));

        engine.SetValue("hsl", (Func<double, double, double, object>)((h, s, l) =>
        {
            var (r, g, b) = HslToRgb(h, s, l);
            return new Dictionary<string, object>
            {
                ["r"] = r,
                ["g"] = g,
                ["b"] = b,
                ["a"] = 255
            };
        }));

        engine.SetValue("__createColorRamp", (Func<JsValue, object>)(argsVal =>
        {
            var stops = new List<ColorStop>();
            if (argsVal is ArrayInstance arr)
            {
                var len = (int)arr.Get("length").AsNumber();
                for (var i = 0; i < len; i++)
                {
                    var item = arr.Get(i.ToString(CultureInfo.InvariantCulture));
                    var pos = len > 1 ? (float)i / (len - 1) : 0f;
                    var (r, g, b, a) = ExtractColorRgba(item);
                    stops.Add(new ColorStop(pos, new ColorRgba(r, g, b, a)));
                }
            }

            if (stops.Count < 2)
            {
                stops.Add(new ColorStop(0f, ColorRgba.Black));
                stops.Add(new ColorStop(1f, ColorRgba.White));
            }

            return new ColorRamp(stops);
        }));
        engine.Execute("function color_ramp(...args) { return __createColorRamp(args); };");

        engine.SetValue("hex", (Func<string, object>)(hexStr =>
        {
            if (string.IsNullOrWhiteSpace(hexStr) || !hexStr.StartsWith('#'))
            {
                return new Dictionary<string, object>();
            }

            var clean = hexStr.TrimStart('#');
            if (clean.Length == 6)
            {
                var r = (int)byte.Parse(clean.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var g = (int)byte.Parse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var b = (int)byte.Parse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return new Dictionary<string, object>
                {
                    ["r"] = r,
                    ["g"] = g,
                    ["b"] = b,
                    ["a"] = 255
                };
            }

            return new Dictionary<string, object>();
        }));

        // NAMED_COLORS
        engine.SetValue("NAMED_COLORS", new Dictionary<string, object>
        {
            ["WHITE"] = new Dictionary<string, object> { ["r"] = 255, ["g"] = 255, ["b"] = 255, ["a"] = 255 },
            ["BLACK"] = new Dictionary<string, object> { ["r"] = 0, ["g"] = 0, ["b"] = 0, ["a"] = 255 },
            ["RED"] = new Dictionary<string, object> { ["r"] = 255, ["g"] = 0, ["b"] = 0, ["a"] = 255 },
            ["GREEN"] = new Dictionary<string, object> { ["r"] = 0, ["g"] = 255, ["b"] = 0, ["a"] = 255 },
            ["BLUE"] = new Dictionary<string, object> { ["r"] = 0, ["g"] = 0, ["b"] = 255, ["a"] = 255 },
            ["YELLOW"] = new Dictionary<string, object> { ["r"] = 255, ["g"] = 255, ["b"] = 0, ["a"] = 255 },
            ["TRANSPARENT"] = new Dictionary<string, object> { ["r"] = 0, ["g"] = 0, ["b"] = 0, ["a"] = 0 }
        });

        // ALIGN
        engine.SetValue("ALIGN", new Dictionary<string, object>
        {
            ["CENTER"] = "CENTER",
            ["LEFT"] = "LEFT",
            ["RIGHT"] = "RIGHT",
            ["TOP"] = "TOP",
            ["BOTTOM"] = "BOTTOM"
        });
    }

    private static JsValue CreateJsContext(Engine engine, EvaluationContext context)
    {
        var dict = new Dictionary<string, object>
        {
            ["run"] = new Dictionary<string, object>
            {
                ["number"] = context.Run.Number,
                ["count"] = context.Run.Count,
                ["seed"] = context.Run.Seed
            },
            ["frame"] = new Dictionary<string, object>
            {
                ["number"] = context.Frame.Number,
                ["count"] = context.Frame.Count,
                ["time"] = context.Frame.Time,
                ["progress"] = context.Frame.Progress,
                ["t"] = context.Frame.Progress
            },
            ["layer"] = new Dictionary<string, object>
            {
                ["id"] = context.CurrentLayerId ?? string.Empty,
                ["size"] = new Dictionary<string, object>
                {
                    ["width"] = context.CurrentLayerDimensions.Width,
                    ["height"] = context.CurrentLayerDimensions.Height
                }
            },
            ["output"] = new Dictionary<string, object>
            {
                ["dimensions"] = new Dictionary<string, object>
                {
                    ["width"] = context.OutputDimensions.Width,
                    ["height"] = context.OutputDimensions.Height
                }
            },
            ["random"] = (Func<JsValue, JsValue, double>)((min, max) =>
            {
                if (min is not null && !min.IsUndefined() && max is not null && !max.IsUndefined())
                {
                    return context.Random.NextDouble(min.AsNumber(), max.AsNumber());
                }

                return context.Random.NextDouble();
            }),
            ["randomInt"] = (Func<int, int, int>)((min, max) => context.Random.NextInt(min, max)),
            ["randomChoice"] = (Func<JsValue, JsValue>)(arr =>
            {
                if (arr is ArrayInstance a)
                {
                    var len = (int)a.Get("length").AsNumber();
                    if (len > 0)
                    {
                        var idx = context.Random.NextInt(0, len);
                        return a.Get(idx.ToString(CultureInfo.InvariantCulture));
                    }
                }

                return JsValue.Undefined;
            }),
            ["rgb"] = engine.GetValue("rgb"),
            ["rgba"] = engine.GetValue("rgba"),
            ["hsl"] = engine.GetValue("hsl"),
            ["hex"] = engine.GetValue("hex"),
            ["color_ramp"] = engine.GetValue("color_ramp"),
            ["NAMED_COLORS"] = engine.GetValue("NAMED_COLORS"),
            ["ALIGN"] = engine.GetValue("ALIGN")
        };

        return JsValue.FromObject(engine, dict);
    }

    private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        h = (h % 360.0 + 360.0) % 360.0;
        if (s > 1.0) s /= 100.0;
        if (l > 1.0) l /= 100.0;
        s = Math.Clamp(s, 0.0, 1.0);
        l = Math.Clamp(l, 0.0, 1.0);

        var c = (1.0 - Math.Abs((2.0 * l) - 1.0)) * s;
        var x = c * (1.0 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
        var m = l - (c / 2.0);

        double r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = 0; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        var r = (byte)Math.Clamp(Math.Round((r1 + m) * 255.0), 0, 255);
        var g = (byte)Math.Clamp(Math.Round((g1 + m) * 255.0), 0, 255);
        var b = (byte)Math.Clamp(Math.Round((b1 + m) * 255.0), 0, 255);
        return (r, g, b);
    }

    private static (byte R, byte G, byte B, byte A) ExtractColorRgba(JsValue val)
    {
        if (val is ObjectInstance o)
        {
            if (o is Jint.Runtime.Interop.ObjectWrapper wrapper && wrapper.Target is ColorRgba colorRgba)
            {
                return (colorRgba.R, colorRgba.G, colorRgba.B, colorRgba.A);
            }

            var r = o.Get("r");
            var g = o.Get("g");
            var b = o.Get("b");
            var a = o.Get("a");
            if (r.IsNumber() && g.IsNumber() && b.IsNumber())
            {
                var byteR = (byte)Math.Clamp(r.AsNumber(), 0, 255);
                var byteG = (byte)Math.Clamp(g.AsNumber(), 0, 255);
                var byteB = (byte)Math.Clamp(b.AsNumber(), 0, 255);
                var byteA = a.IsNumber() ? (byte)Math.Clamp(a.AsNumber(), 0, 255) : (byte)255;
                return (byteR, byteG, byteB, byteA);
            }
        }
        else if (val.IsString())
        {
            var str = val.AsString();
            if (str.StartsWith('#') && SkiaSharp.SKColor.TryParse(str, out var skColor))
            {
                return (skColor.Red, skColor.Green, skColor.Blue, skColor.Alpha);
            }
        }

        return (0, 0, 0, 255);
    }
}
