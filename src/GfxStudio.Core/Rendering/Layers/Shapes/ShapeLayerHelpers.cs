using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;

namespace GfxStudio.Core.Rendering.Layers.Shapes;

internal static class ShapeLayerHelpers
{
    public static ColorRgba? ResolveColor(LayerRenderContext context, string primaryName, string? aliasName = null)
    {
        if (context.LayerProperties.TryGetProperty(primaryName, out var value)
            || aliasName is not null && context.LayerProperties.TryGetProperty(aliasName, out value))
        {
            if (value is GfxColor color)
            {
                return color.Color;
            }

            context.AddDiagnostic("RND030", $"Property '{primaryName}' on layer '{context.LayerId}' must be a color.");
        }

        return null;
    }

    public static double ResolveNumber(LayerRenderContext context, string primaryName, double defaultValue, string? aliasName = null)
    {
        if (context.LayerProperties.TryGetProperty(primaryName, out var value)
            || aliasName is not null && context.LayerProperties.TryGetProperty(aliasName, out value))
        {
            if (value is GfxNumber number && double.IsFinite(number.Value))
            {
                return number.Value;
            }

            context.AddDiagnostic("RND031", $"Property '{primaryName}' on layer '{context.LayerId}' must be a finite number.");
        }

        return defaultValue;
    }

    public static Point2D ResolvePoint(LayerRenderContext context, string propertyName, Point2D defaultValue)
    {
        if (!context.LayerProperties.TryGetProperty(propertyName, out var value))
        {
            return defaultValue;
        }

        if (value.TryCoerceToPoint2D(out var point) && float.IsFinite(point.X) && float.IsFinite(point.Y))
        {
            return point;
        }

        context.AddDiagnostic("RND032", $"Property '{propertyName}' on layer '{context.LayerId}' must contain finite numeric x and y values.");
        return defaultValue;
    }

    public static bool HasColor(LayerRenderContext context, params string[] propertyNames) =>
        propertyNames.Any(context.LayerProperties.Properties.ContainsKey);

    public static bool ValidateStrokeWidth(LayerRenderContext context, double width, string propertyName = "stroke_width")
    {
        if (width >= 0 && double.IsFinite(width))
        {
            return true;
        }

        context.AddDiagnostic("RND033", $"Property '{propertyName}' on layer '{context.LayerId}' must be a non-negative finite number.");
        return false;
    }
}
