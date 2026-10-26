using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;

namespace GfxStudio.Core.Functions;

internal static class StandardFunctions
{
    public static bool IsBuiltIn(IGfxCallable callable) => callable is BuiltInFunction;

    public static void RegisterAll(GfxFunctionRegistry registry)
    {
        registry.Register(Fixed("rgb", 3, Rgb));
        registry.Register(Fixed("rgba", 4, Rgba));
        registry.Register(Fixed("hsl", 3, Hsl));
        registry.Register(Fixed("hsla", 4, Hsla));
        registry.Register(Fixed("hex", 1, Hex));
        registry.Register(Variadic("color_ramp", 2, ColorRamp));
        registry.Register(new BuiltInFunction("sample_layer", 3, 4, SampleLayer));
        registry.Register(new BuiltInFunction("sample_canvas", 2, 3, SampleCanvas));

        registry.Register(Variadic("random", 0, Random));
        registry.Register(Variadic("random_frame", 0, RandomFrame));
        registry.Register(Fixed("random_int", 2, RandomInt));
        registry.Register(Fixed("random_choice", 1, RandomChoice));

        registry.Register(Fixed("floor", 1, (args, _, span) => new GfxNumber(Math.Floor(Number(args, 0, "floor", span)))));
        registry.Register(Fixed("ceil", 1, (args, _, span) => new GfxNumber(Math.Ceiling(Number(args, 0, "ceil", span)))));
        registry.Register(Fixed("round", 1, (args, _, span) => new GfxNumber(Math.Round(Number(args, 0, "round", span), MidpointRounding.AwayFromZero))));
        registry.Register(Fixed("abs", 1, (args, _, span) => new GfxNumber(Math.Abs(Number(args, 0, "abs", span)))));
        registry.Register(Fixed("sign", 1, (args, _, span) =>
        {
            var value = Number(args, 0, "sign", span);
            return new GfxNumber(value > 0.0 ? 1.0 : value < 0.0 ? -1.0 : 0.0);
        }));
        registry.Register(Variadic("min", 2, (args, _, span) => new GfxNumber(args.Select((_, index) => Number(args, index, "min", span)).Min())));
        registry.Register(Variadic("max", 2, (args, _, span) => new GfxNumber(args.Select((_, index) => Number(args, index, "max", span)).Max())));
        registry.Register(Fixed("clamp", 3, Clamp));
        registry.Register(Fixed("sqrt", 1, (args, _, span) => new GfxNumber(Math.Sqrt(Number(args, 0, "sqrt", span)))));
        registry.Register(Fixed("pow", 2, (args, _, span) => new GfxNumber(Math.Pow(Number(args, 0, "pow", span), Number(args, 1, "pow", span)))));
        registry.Register(Fixed("exp", 1, (args, _, span) => new GfxNumber(Math.Exp(Number(args, 0, "exp", span)))));
        registry.Register(Fixed("log", 1, (args, _, span) => new GfxNumber(Math.Log(Number(args, 0, "log", span)))));
        registry.Register(Fixed("sin", 1, (args, _, span) => new GfxNumber(Math.Sin(Number(args, 0, "sin", span)))));
        registry.Register(Fixed("cos", 1, (args, _, span) => new GfxNumber(Math.Cos(Number(args, 0, "cos", span)))));
        registry.Register(Fixed("tan", 1, (args, _, span) => new GfxNumber(Math.Tan(Number(args, 0, "tan", span)))));
        registry.Register(Fixed("asin", 1, (args, _, span) => new GfxNumber(Math.Asin(Number(args, 0, "asin", span)))));
        registry.Register(Fixed("acos", 1, (args, _, span) => new GfxNumber(Math.Acos(Number(args, 0, "acos", span)))));
        registry.Register(Fixed("atan2", 2, (args, _, span) => new GfxNumber(Math.Atan2(Number(args, 0, "atan2", span), Number(args, 1, "atan2", span)))));
        registry.Register(Fixed("deg_to_rad", 1, (args, _, span) => new GfxNumber(Number(args, 0, "deg_to_rad", span) * (Math.PI / 180.0))));
        registry.Register(Fixed("rad_to_deg", 1, (args, _, span) => new GfxNumber(Number(args, 0, "rad_to_deg", span) * (180.0 / Math.PI))));

        registry.Register(Fixed("lerp", 3, Lerp));
        registry.Register(Fixed("smoothstep", 3, SmoothStep));
        registry.Register(Fixed("ease_in", 1, (args, _, span) =>
        {
            var t = Number(args, 0, "ease_in", span);
            return new GfxNumber(t * t);
        }));
        registry.Register(Fixed("ease_out", 1, (args, _, span) =>
        {
            var t = Number(args, 0, "ease_out", span);
            var inverse = 1.0 - t;
            return new GfxNumber(1.0 - (inverse * inverse));
        }));
        registry.Register(Fixed("ease_in_out", 1, (args, _, span) =>
        {
            var t = Number(args, 0, "ease_in_out", span);
            return new GfxNumber((3.0 * t * t) - (2.0 * t * t * t));
        }));
        registry.Register(Fixed("ping_pong", 1, (args, _, span) =>
        {
            var t = Number(args, 0, "ping_pong", span);
            var phase = t - Math.Floor(t);
            return new GfxNumber(1.0 - Math.Abs((2.0 * phase) - 1.0));
        }));
    }

    private static IGfxFunction Fixed(string name, int arity, Func<IReadOnlyList<GfxValue>, EvaluationContext, SourceSpan, GfxValue> invoke) =>
        new BuiltInFunction(name, arity, arity, invoke);

    private static IGfxFunction Variadic(string name, int minArity, Func<IReadOnlyList<GfxValue>, EvaluationContext, SourceSpan, GfxValue> invoke) =>
        new BuiltInFunction(name, minArity, int.MaxValue, invoke);

    private static GfxValue Rgb(IReadOnlyList<GfxValue> args, EvaluationContext _, SourceSpan span) =>
        new GfxColor(new ColorRgba(Channel(args, 0, "rgb", span), Channel(args, 1, "rgb", span), Channel(args, 2, "rgb", span)));

    private static GfxValue Rgba(IReadOnlyList<GfxValue> args, EvaluationContext _, SourceSpan span) =>
        new GfxColor(new ColorRgba(
            Channel(args, 0, "rgba", span),
            Channel(args, 1, "rgba", span),
            Channel(args, 2, "rgba", span),
            Alpha(args, 3, "rgba", span)));

    private static GfxValue Hsl(IReadOnlyList<GfxValue> args, EvaluationContext _, SourceSpan span) =>
        new GfxColor(ConvertHsl(Number(args, 0, "hsl", span), Number(args, 1, "hsl", span), Number(args, 2, "hsl", span), 255, span));

    private static GfxValue Hsla(IReadOnlyList<GfxValue> args, EvaluationContext _, SourceSpan span) =>
        new GfxColor(ConvertHsl(
            Number(args, 0, "hsla", span),
            Number(args, 1, "hsla", span),
            Number(args, 2, "hsla", span),
            Alpha(args, 3, "hsla", span),
            span));

    private static GfxValue Hex(IReadOnlyList<GfxValue> args, EvaluationContext _, SourceSpan span)
    {
        if (args[0] is not GfxString text)
        {
            throw TypeError("hex", 1, "a string", args[0], span);
        }

        var value = text.Value;
        if (value.Length is not 7 and not 9 || value[0] != '#')
        {
            throw InvalidValue("hex expects '#RRGGBB' or '#RRGGBBAA'.", span);
        }

        if (!TryHexByte(value, 1, out var red)
            || !TryHexByte(value, 3, out var green)
            || !TryHexByte(value, 5, out var blue))
        {
            throw InvalidValue($"'{value}' is not a valid hexadecimal color.", span);
        }

        var alpha = byte.MaxValue;
        if (value.Length == 9 && !TryHexByte(value, 7, out alpha))
        {
            throw InvalidValue($"'{value}' is not a valid hexadecimal color.", span);
        }

        return new GfxColor(new ColorRgba(red, green, blue, alpha));
    }

    private static GfxValue ColorRamp(IReadOnlyList<GfxValue> args, EvaluationContext _, SourceSpan span)
    {
        var stops = new List<ColorStop>(args.Count);
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument is GfxColor color)
            {
                var position = index / (float)(args.Count - 1);
                stops.Add(new ColorStop(position, color.Color));
                continue;
            }

            if (argument is not GfxObject stopObject
                || !stopObject.Properties.TryGetValue("pos", out var positionValue)
                || positionValue is not GfxNumber positionNumber
                || !stopObject.Properties.TryGetValue("color", out var colorValue)
                || colorValue is not GfxColor stopColor)
            {
                throw TypeError("color_ramp", index + 1, "a color or an object with numeric 'pos' and color 'color' properties", argument, span);
            }

            if (!double.IsFinite(positionNumber.Value) || positionNumber.Value is < 0.0 or > 1.0)
            {
                throw InvalidValue("Color ramp stop positions must be finite values in [0, 1].", span);
            }

            stops.Add(new ColorStop((float)positionNumber.Value, stopColor.Color));
        }

        try
        {
            return new GfxColorRamp(new GfxStudio.Core.Primitives.ColorRamp(stops));
        }
        catch (ArgumentException exception)
        {
            throw InvalidValue(exception.Message, span);
        }
    }

    private static GfxValue SampleLayer(IReadOnlyList<GfxValue> args, EvaluationContext context, SourceSpan span)
    {
        var layerId = args[0] switch
        {
            GfxString str => str.Value,
            GfxSymbol sym => sym.Identifier,
            _ => throw TypeError("sample_layer", 1, "a layer name string or identifier", args[0], span)
        };

        if (string.IsNullOrWhiteSpace(layerId))
        {
            throw InvalidValue("sample_layer requires a non-empty layer name.", span);
        }

        var x = Number(args, 1, "sample_layer", span);
        var y = Number(args, 2, "sample_layer", span);
        var normalized = false;
        if (args.Count == 4)
        {
            if (args[3] is not GfxBoolean b)
            {
                throw TypeError("sample_layer", 4, "a boolean", args[3], span);
            }

            normalized = b.Value;
        }

        if (context.ResolveLayerBitmap is null)
        {
            throw InvalidValue($"sample_layer('{layerId}') cannot be called outside a canvas rendering pass.", span);
        }

        var bitmap = context.ResolveLayerBitmap(layerId, span);
        if (bitmap is null)
        {
            throw new GfxException(new GfxDiagnostic(
                "RND056",
                $"Cannot sample layer '{layerId}': layer does not exist or introduces a circular reference.",
                span,
                DiagnosticSeverity.Error));
        }

        return SampleBitmapColor(bitmap, x, y, normalized);
    }

    private static GfxValue SampleCanvas(IReadOnlyList<GfxValue> args, EvaluationContext context, SourceSpan span)
    {
        var x = Number(args, 0, "sample_canvas", span);
        var y = Number(args, 1, "sample_canvas", span);
        var normalized = false;
        if (args.Count == 3)
        {
            if (args[2] is not GfxBoolean b)
            {
                throw TypeError("sample_canvas", 3, "a boolean", args[2], span);
            }

            normalized = b.Value;
        }

        if (context.ResolveCanvasBitmap is null)
        {
            return new GfxColor(new ColorRgba(0, 0, 0, 0));
        }

        var bitmap = context.ResolveCanvasBitmap(context.CurrentLayerId, span);
        if (bitmap is null)
        {
            return new GfxColor(new ColorRgba(0, 0, 0, 0));
        }

        return SampleBitmapColor(bitmap, x, y, normalized);
    }

    private static GfxColor SampleBitmapColor(SkiaSharp.SKBitmap bitmap, double x, double y, bool normalized)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return new GfxColor(new ColorRgba(0, 0, 0, 0));
        }

        int px;
        int py;
        if (normalized)
        {
            px = (int)Math.Round(Math.Clamp(x, 0.0, 1.0) * (bitmap.Width - 1));
            py = (int)Math.Round(Math.Clamp(y, 0.0, 1.0) * (bitmap.Height - 1));
        }
        else
        {
            px = Math.Clamp((int)Math.Round(x), 0, bitmap.Width - 1);
            py = Math.Clamp((int)Math.Round(y), 0, bitmap.Height - 1);
        }

        return new GfxColor(ColorRgba.FromSKColor(bitmap.GetPixel(px, py)));
    }

    private static GfxValue Random(IReadOnlyList<GfxValue> args, EvaluationContext context, SourceSpan span) =>
        SampleRandom(args, context.Random, "random", span);

    private static GfxValue RandomFrame(IReadOnlyList<GfxValue> args, EvaluationContext context, SourceSpan span) =>
        SampleRandom(args, context.FrameRandom, "random_frame", span);

    private static GfxValue SampleRandom(IReadOnlyList<GfxValue> args, GfxStudio.Core.Randomness.IGfxRandom rng, string functionName, SourceSpan span)
    {
        if (args.Count == 0)
        {
            return new GfxNumber(rng.NextDouble());
        }

        if (args.Count != 2)
        {
            throw ArityError(functionName, "0 or 2", args.Count, span);
        }

        var min = Number(args, 0, functionName, span);
        var max = Number(args, 1, functionName, span);
        try
        {
            return new GfxNumber(rng.NextDouble(min, max));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw InvalidValue(exception.Message, span);
        }
    }

    private static GfxValue RandomInt(IReadOnlyList<GfxValue> args, EvaluationContext context, SourceSpan span)
    {
        var minValue = Number(args, 0, "random_int", span);
        var maxValue = Number(args, 1, "random_int", span);
        if (!IsInt32(minValue) || !IsInt32(maxValue))
        {
            throw InvalidValue("random_int bounds must be 32-bit integers.", span);
        }

        try
        {
            return new GfxNumber(context.Random.NextInt((int)minValue, (int)maxValue));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw InvalidValue(exception.Message, span);
        }
    }

    private static GfxValue RandomChoice(IReadOnlyList<GfxValue> args, EvaluationContext context, SourceSpan span)
    {
        if (args[0] is not GfxArray array)
        {
            throw TypeError("random_choice", 1, "an array", args[0], span);
        }

        if (array.Items.Count == 0)
        {
            throw InvalidValue("random_choice cannot select from an empty array.", span);
        }

        return array.Items[context.Random.NextInt(0, array.Items.Count)];
    }

    private static GfxValue Clamp(IReadOnlyList<GfxValue> args, EvaluationContext _, SourceSpan span)
    {
        var value = Number(args, 0, "clamp", span);
        var min = Number(args, 1, "clamp", span);
        var max = Number(args, 2, "clamp", span);
        if (min > max)
        {
            throw InvalidValue("clamp requires its minimum to be less than or equal to its maximum.", span);
        }

        return new GfxNumber(Math.Clamp(value, min, max));
    }

    private static GfxValue Lerp(IReadOnlyList<GfxValue> args, EvaluationContext _, SourceSpan span)
    {
        var from = args[0];
        var to = args[1];
        var amount = Number(args, 2, "lerp", span);
        if (from is GfxNumber fromNumber && to is GfxNumber toNumber)
        {
            return new GfxNumber(fromNumber.Value + ((toNumber.Value - fromNumber.Value) * amount));
        }

        if (from is GfxColor fromColor && to is GfxColor toColor)
        {
            return new GfxColor(new ColorRgba(
                Interpolate(fromColor.Color.R, toColor.Color.R, amount),
                Interpolate(fromColor.Color.G, toColor.Color.G, amount),
                Interpolate(fromColor.Color.B, toColor.Color.B, amount),
                Interpolate(fromColor.Color.A, toColor.Color.A, amount)));
        }

        throw TypeError("lerp", 1, "a number or color matching the second argument", from, span);
    }

    private static GfxValue SmoothStep(IReadOnlyList<GfxValue> args, EvaluationContext _, SourceSpan span)
    {
        var edge0 = Number(args, 0, "smoothstep", span);
        var edge1 = Number(args, 1, "smoothstep", span);
        var value = Number(args, 2, "smoothstep", span);
        if (edge0 == edge1)
        {
            return new GfxNumber(value < edge0 ? 0.0 : 1.0);
        }

        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return new GfxNumber(t * t * (3.0 - (2.0 * t)));
    }

    private static double Number(IReadOnlyList<GfxValue> args, int index, string functionName, SourceSpan span)
    {
        if (args[index] is GfxNumber number)
        {
            return number.Value;
        }

        throw TypeError(functionName, index + 1, "a number", args[index], span);
    }

    private static byte Channel(IReadOnlyList<GfxValue> args, int index, string functionName, SourceSpan span) =>
        ToByte(Number(args, index, functionName, span), functionName, span);

    private static byte Alpha(IReadOnlyList<GfxValue> args, int index, string functionName, SourceSpan span)
    {
        var value = Number(args, index, functionName, span);
        if (!double.IsFinite(value))
        {
            throw InvalidValue($"{functionName} channel values must be finite.", span);
        }

        return ToByte(value <= 1.0 ? value * 255.0 : value, functionName, span);
    }

    private static byte ToByte(double value, string functionName, SourceSpan span)
    {
        if (!double.IsFinite(value))
        {
            throw InvalidValue($"{functionName} channel values must be finite.", span);
        }

        if (value <= byte.MinValue)
        {
            return byte.MinValue;
        }

        if (value >= byte.MaxValue)
        {
            return byte.MaxValue;
        }

        return (byte)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static ColorRgba ConvertHsl(double hue, double saturation, double lightness, byte alpha, SourceSpan span)
    {
        if (!double.IsFinite(hue) || !double.IsFinite(saturation) || !double.IsFinite(lightness))
        {
            throw InvalidValue("HSL components must be finite numbers.", span);
        }

        hue = Math.Clamp(hue, 0.0, 360.0) % 360.0 / 360.0;
        saturation = Math.Clamp(saturation, 0.0, 1.0);
        lightness = Math.Clamp(lightness, 0.0, 1.0);

        double red;
        double green;
        double blue;
        if (saturation == 0.0)
        {
            red = green = blue = lightness;
        }
        else
        {
            var q = lightness < 0.5
                ? lightness * (1.0 + saturation)
                : lightness + saturation - (lightness * saturation);
            var p = (2.0 * lightness) - q;
            red = HueToRgb(p, q, hue + (1.0 / 3.0));
            green = HueToRgb(p, q, hue);
            blue = HueToRgb(p, q, hue - (1.0 / 3.0));
        }

        return new ColorRgba(ToByte(red * 255.0, "hsl", span), ToByte(green * 255.0, "hsl", span), ToByte(blue * 255.0, "hsl", span), alpha);
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0.0) t += 1.0;
        if (t > 1.0) t -= 1.0;
        if (t < 1.0 / 6.0) return p + ((q - p) * 6.0 * t);
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + ((q - p) * (2.0 / 3.0 - t) * 6.0);
        return p;
    }

    private static byte Interpolate(byte from, byte to, double amount) =>
        (byte)Math.Clamp((int)Math.Round(from + ((to - from) * amount), MidpointRounding.AwayFromZero), byte.MinValue, byte.MaxValue);

    private static bool IsInt32(double value) =>
        double.IsFinite(value) && Math.Truncate(value) == value && value >= int.MinValue && value <= int.MaxValue;

    private static bool TryHexByte(string value, int start, out byte result) =>
        byte.TryParse(value.AsSpan(start, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out result);

    private static GfxException TypeError(string functionName, int argumentIndex, string expected, GfxValue actual, SourceSpan span) =>
        new(new GfxDiagnostic(
            "FUNC002",
            $"Function '{functionName}' argument {argumentIndex} must be {expected}; got '{actual.GetType().Name}'.",
            span,
            DiagnosticSeverity.Error));

    private static GfxException InvalidValue(string message, SourceSpan span) =>
        new(new GfxDiagnostic("FUNC003", message, span, DiagnosticSeverity.Error));

    private static GfxException ArityError(string functionName, string expected, int actual, SourceSpan span) =>
        new(new GfxDiagnostic(
            "FUNC001",
            $"Function '{functionName}' expects {expected} argument(s), but received {actual}.",
            span,
            DiagnosticSeverity.Error));

    private sealed class BuiltInFunction(
        string name,
        int minArity,
        int maxArity,
        Func<IReadOnlyList<GfxValue>, EvaluationContext, SourceSpan, GfxValue> implementation) : IGfxFunction
    {
        public string Name { get; } = name;

        public GfxValue Invoke(IReadOnlyList<GfxValue> arguments, EvaluationContext context, SourceSpan callSpan)
        {
            if (arguments.Count < minArity || arguments.Count > maxArity)
            {
                var expected = minArity == maxArity
                    ? minArity.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : $"{minArity} to {maxArity}";
                if (maxArity == int.MaxValue)
                {
                    expected = $"at least {minArity}";
                }

                throw new GfxException(new GfxDiagnostic(
                    "FUNC001",
                    $"Function '{Name}' expects {expected} argument(s), but received {arguments.Count}.",
                    callSpan,
                    DiagnosticSeverity.Error));
            }

            return implementation(arguments, context, callSpan);
        }
    }
}
