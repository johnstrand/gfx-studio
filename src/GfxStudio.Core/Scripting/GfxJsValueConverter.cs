using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Object;
using SkiaSharp;

namespace GfxStudio.Core.Scripting;

/// <summary>Converts data values between Jint (JavaScript) and GfxValue representations.</summary>
public static class GfxJsValueConverter
{
    public static GfxValue ToGfxValue(JsValue jsValue)
    {
        if (jsValue.IsUndefined() || jsValue.IsNull())
        {
            return new GfxString(string.Empty);
        }

        if (jsValue.IsBoolean())
        {
            return new GfxBoolean(jsValue.AsBoolean());
        }

        if (jsValue.IsNumber())
        {
            return new GfxNumber(jsValue.AsNumber());
        }

        if (jsValue.IsString())
        {
            var str = jsValue.AsString();
            if (str.StartsWith('#') && SKColor.TryParse(str, out var skColor))
            {
                return new GfxColor(ColorRgba.FromSKColor(skColor));
            }

            return new GfxString(str);
        }

        if (jsValue is ArrayInstance arrayInstance)
        {
            var len = (int)arrayInstance.Get("length").AsNumber();
            var items = new List<GfxValue>(len);
            for (var i = 0; i < len; i++)
            {
                items.Add(ToGfxValue(arrayInstance.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            }

            return new GfxArray(items.AsReadOnly());
        }

        if (jsValue is ObjectInstance obj)
        {
            if (obj is Jint.Runtime.Interop.ObjectWrapper wrapper)
            {
                if (wrapper.Target is ColorRgba colorRgba)
                {
                    return new GfxColor(colorRgba);
                }

                if (wrapper.Target is ColorRamp colorRamp)
                {
                    return new GfxColorRamp(colorRamp);
                }

                if (wrapper.Target is Dimensions dimensions)
                {
                    return new GfxDimensions(dimensions);
                }
            }

            // Check for ColorRamp { __isColorRamp: true, stops: [...] }
            var isRampVal = obj.Get("__isColorRamp");
            if (isRampVal.IsBoolean() && isRampVal.AsBoolean() && obj.Get("stops") is ArrayInstance stopsArr)
            {
                var stopsLen = (int)stopsArr.Get("length").AsNumber();
                var colorStops = new List<ColorStop>(stopsLen);
                for (var i = 0; i < stopsLen; i++)
                {
                    var stopObj = stopsArr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).AsObject();
                    var pos = (float)stopObj.Get("position").AsNumber();
                    var colObj = stopObj.Get("color").AsObject();
                    var r = (byte)colObj.Get("r").AsNumber();
                    var g = (byte)colObj.Get("g").AsNumber();
                    var b = (byte)colObj.Get("b").AsNumber();
                    var aVal = colObj.Get("a");
                    var a = aVal.IsNumber() ? (byte)aVal.AsNumber() : (byte)255;
                    colorStops.Add(new ColorStop(pos, new ColorRgba(r, g, b, a)));
                }

                if (colorStops.Count >= 2)
                {
                    return new GfxColorRamp(new ColorRamp(colorStops));
                }
            }

            // Check for Color { r, g, b, [a] }
            var rVal = obj.Get("r");
            var gVal = obj.Get("g");
            var bVal = obj.Get("b");
            if (rVal.IsNumber() && gVal.IsNumber() && bVal.IsNumber())
            {
                var aVal = obj.Get("a");
                var a = aVal.IsNumber() ? (byte)Math.Clamp(aVal.AsNumber(), 0, 255) : (byte)255;
                return new GfxColor(new ColorRgba(
                    (byte)Math.Clamp(rVal.AsNumber(), 0, 255),
                    (byte)Math.Clamp(gVal.AsNumber(), 0, 255),
                    (byte)Math.Clamp(bVal.AsNumber(), 0, 255),
                    a));
            }

            // Check for Dimensions { width, height }
            var wVal = obj.Get("width");
            var hVal = obj.Get("height");
            if (wVal.IsNumber() && hVal.IsNumber())
            {
                var ownPropsCount = obj.GetOwnProperties().Count();
                if (ownPropsCount == 2)
                {
                    return new GfxDimensions(new Dimensions((int)wVal.AsNumber(), (int)hVal.AsNumber()));
                }
            }

            var props = new Dictionary<string, GfxValue>(StringComparer.Ordinal);
            var keys = new List<string>();
            foreach (var prop in obj.GetOwnProperties())
            {
                var name = prop.Key.ToString();
                var childVal = obj.Get(name);
                var converted = ToGfxValue(childVal);
                props[name] = converted;
                keys.Add(name);
            }

            return new GfxObject(props, keys.AsReadOnly());
        }

        return new GfxString(jsValue.ToString());
    }

    public static JsValue ToJsValue(Engine engine, GfxValue gfxValue)
    {
        return gfxValue switch
        {
            GfxNumber num => new JsNumber(num.Value),
            GfxString str => new JsString(str.Value),
            GfxBoolean b => b.Value ? JsBoolean.True : JsBoolean.False,
            GfxSymbol sym => new JsString(sym.Identifier),
            GfxColor color => JsValue.FromObject(engine, new Dictionary<string, object>
            {
                ["r"] = (int)color.Color.R,
                ["g"] = (int)color.Color.G,
                ["b"] = (int)color.Color.B,
                ["a"] = (int)color.Color.A
            }),
            GfxDimensions dims => JsValue.FromObject(engine, new Dictionary<string, object>
            {
                ["width"] = dims.Dimensions.Width,
                ["height"] = dims.Dimensions.Height
            }),
            GfxArray arr => JsValue.FromObject(engine, arr.Items.Select(item => ToJsValue(engine, item)).ToArray()),
            GfxObject obj => CreateJsObject(engine, obj),
            _ => JsValue.Undefined
        };
    }

    private static JsValue CreateJsObject(Engine engine, GfxObject obj)
    {
        var dict = new Dictionary<string, object>(obj.Properties.Count, StringComparer.Ordinal);
        foreach (var (k, v) in obj.Properties)
        {
            dict[k] = ToJsValue(engine, v);
        }

        return JsValue.FromObject(engine, dict);
    }
}
