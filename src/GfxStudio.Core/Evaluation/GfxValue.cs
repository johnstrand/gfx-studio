using System.Globalization;
using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Primitives;

namespace GfxStudio.Core.Evaluation;

/// <summary>Represents a callable function or script callback within the .gfx evaluation engine.</summary>
public interface IGfxCallable
{
    string Name { get; }

    GfxValue Invoke(IReadOnlyList<GfxValue> arguments, EvaluationContext context, SourceSpan callSpan);
}

/// <summary>Base discriminated value type produced by evaluating .gfx expressions.</summary>
public abstract record GfxValue
{
    /// <summary>Formats the value as an invariant string suitable for text layers or string interpolation.</summary>
    public virtual string ToDisplayString(string? format = null)
    {
        return this switch
        {
            GfxNumber number => FormatNumber(number.Value, format),
            GfxString str => str.Value,
            GfxBoolean boolean => boolean.Value ? "true" : "false",
            GfxSymbol symbol => symbol.Identifier,
            _ => ToString()
        };
    }

    /// <summary>Attempts to coerce this value into a <see cref="double"/> value.</summary>
    public bool TryCoerceToDouble(out double number)
    {
        if (this is GfxNumber num)
        {
            number = num.Value;
            return true;
        }

        number = 0.0;
        return false;
    }

    /// <summary>Attempts to coerce this value into a <see cref="Dimensions"/> value.</summary>
    public bool TryCoerceToDimensions(out Dimensions dimensions)
    {
        if (this is GfxDimensions gfxDimensions)
        {
            dimensions = gfxDimensions.Dimensions;
            return true;
        }

        if (this is GfxObject obj
            && obj.Properties.TryGetValue("width", out var widthVal) && widthVal is GfxNumber widthNum
            && obj.Properties.TryGetValue("height", out var heightVal) && heightVal is GfxNumber heightNum)
        {
            var w = (int)Math.Round(widthNum.Value);
            var h = (int)Math.Round(heightNum.Value);
            if (w > 0 && h > 0)
            {
                dimensions = new Dimensions(w, h);
                return true;
            }
        }

        dimensions = default;
        return false;
    }

    /// <summary>Attempts to coerce this value into an <see cref="Alignment"/> value.</summary>
    public bool TryCoerceToAlignment(out Alignment alignment)
    {
        if (this is GfxObject obj)
        {
            var horizontal = HorizontalAlign.Left;
            var vertical = VerticalAlign.Top;
            var hasAny = false;

            if (obj.Properties.TryGetValue("horizontal", out var hVal) && TryParseHorizontalAlign(hVal, out var parsedH))
            {
                horizontal = parsedH;
                hasAny = true;
            }

            if (obj.Properties.TryGetValue("vertical", out var vVal) && TryParseVerticalAlign(vVal, out var parsedV))
            {
                vertical = parsedV;
                hasAny = true;
            }

            if (hasAny)
            {
                alignment = new Alignment(horizontal, vertical);
                return true;
            }
        }

        alignment = default;
        return false;
    }

    /// <summary>Attempts to coerce this value into a <see cref="Point2D"/> value.</summary>
    public bool TryCoerceToPoint2D(out Point2D point)
    {
        if (this is GfxDimensions gfxDimensions)
        {
            point = new Point2D(gfxDimensions.Dimensions.Width, gfxDimensions.Dimensions.Height);
            return true;
        }

        if (this is GfxObject obj
            && obj.Properties.TryGetValue("x", out var xVal) && xVal is GfxNumber xNum
            && obj.Properties.TryGetValue("y", out var yVal) && yVal is GfxNumber yNum)
        {
            point = new Point2D((float)xNum.Value, (float)yNum.Value);
            return true;
        }

        point = default;
        return false;
    }

    /// <summary>Attempts to coerce this value into a <see cref="BlendMode"/> value.</summary>
    public bool TryCoerceToBlendMode(out BlendMode blendMode)
    {
        var name = this switch
        {
            GfxSymbol symbol => symbol.Identifier,
            GfxString str => str.Value,
            _ => null
        };

        if (name is not null && Enum.TryParse(name, ignoreCase: true, out blendMode))
        {
            return true;
        }

        blendMode = default;
        return false;
    }

    private static bool TryParseHorizontalAlign(GfxValue value, out HorizontalAlign align)
    {
        var text = value switch
        {
            GfxSymbol s => s.Identifier,
            GfxString str => str.Value,
            _ => null
        };

        return Enum.TryParse(text, ignoreCase: true, out align);
    }

    private static bool TryParseVerticalAlign(GfxValue value, out VerticalAlign align)
    {
        var text = value switch
        {
            GfxSymbol s => s.Identifier,
            GfxString str => str.Value,
            _ => null
        };

        return Enum.TryParse(text, ignoreCase: true, out align);
    }

    private static string FormatNumber(double value, string? format)
    {
        var isWholeNumber = Math.Abs(value - Math.Round(value)) < 1e-9
            && value is >= long.MinValue and <= long.MaxValue;

        if (!string.IsNullOrEmpty(format))
        {
            if (isWholeNumber)
            {
                var intValue = (long)Math.Round(value);
                try
                {
                    return intValue.ToString(format, CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    // Fall back to double formatting if the format string is float-specific
                }
            }

            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        if (isWholeNumber)
        {
            return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>A numeric runtime value.</summary>
public sealed record GfxNumber(double Value) : GfxValue
{
    public int AsInt32() => (int)Math.Round(Value);

    public float AsSingle() => (float)Value;
}

/// <summary>A string runtime value.</summary>
public sealed record GfxString(string Value) : GfxValue;

/// <summary>A boolean runtime value.</summary>
public sealed record GfxBoolean(bool Value) : GfxValue;

/// <summary>An unquoted symbol/enum identifier value (e.g., <c>fill</c>, <c>multiply</c>, <c>CENTER</c>).</summary>
public sealed record GfxSymbol(string Identifier) : GfxValue;

/// <summary>An RGBA color runtime value.</summary>
public sealed record GfxColor(ColorRgba Color) : GfxValue;

/// <summary>A color ramp runtime value.</summary>
public sealed record GfxColorRamp(ColorRamp Ramp) : GfxValue;

/// <summary>A 2D pixel dimensions runtime value.</summary>
public sealed record GfxDimensions(Dimensions Dimensions) : GfxValue;

/// <summary>An ordered key-value object map runtime value.</summary>
public sealed record GfxObject(
    IReadOnlyDictionary<string, GfxValue> Properties,
    IReadOnlyList<string> OrderedKeys) : GfxValue
{
    public bool TryGetProperty(string name, out GfxValue value) =>
        Properties.TryGetValue(name, out value!);
}

/// <summary>An ordered list/array runtime value.</summary>
public sealed record GfxArray(IReadOnlyList<GfxValue> Items) : GfxValue;

/// <summary>A first-class callable function reference (such as an exported JavaScript function).</summary>
public sealed record GfxFunctionRef(IGfxCallable Callable) : GfxValue;
