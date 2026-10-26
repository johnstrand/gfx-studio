namespace GfxStudio.Core.Rendering;

/// <summary>
/// Centralized metadata catalog for universal layer properties, spatial transform properties,
/// built-in <see cref="ILayerRenderer"/> types, and built-in <see cref="Effects.ILayerEffect"/> filters.
/// </summary>
public static class LayerMetadataCatalog
{
    public static IReadOnlyList<LayerPropertyDescriptor> UniversalProperties { get; } =
    [
        new("type", "identifier", IsRequired: true, Description: "Layer renderer or adjustment effect type identifier."),
        new("size", "dimensions", DefaultValue: "output.dimensions", Description: "Layer bounding box dimensions ({ width = <int>, height = <int> })."),
        new("mix", "enum", DefaultValue: "normal", Description: "Blend mode used when compositing this layer onto the canvas.",
            AllowedValues: ["normal", "multiply", "screen", "overlay", "darken", "lighten", "color_dodge", "color_burn", "hard_light", "soft_light", "difference", "exclusion", "add", "subtract"]),
        new("opacity", "number", DefaultValue: "1.0", Description: "Layer opacity in [0.0 .. 1.0]."),
        new("visible", "boolean", DefaultValue: "true", Description: "Whether the layer is rendered and composited.", Aliases: ["enabled"]),
        new("seed", "number", DefaultValue: "derived", Description: "Deterministic unsigned 64-bit seed override for this layer."),
        new("mask", "identifier | string", Description: "Name of another layer whose alpha/luminance masks this layer."),
        new("copy", "identifier | string", Description: "Name of an earlier layer whose properties are inherited as defaults."),
        new("transform", "object", Description: "Grouped 2D spatial transform block ({ offset, rotation, scale, shear, flip_x, flip_y, anchor, wrap, resample })."),
        new("effects", "array", Description: "Ordered list of post-processing effect objects ([ { type = ..., ... } ])."),
    ];

    public static IReadOnlyList<LayerPropertyDescriptor> TransformProperties { get; } =
    [
        new("offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Canvas placement offset in pixels ({ x = <number>, y = <number> }).", Aliases: ["position"]),
        new("rotation", "number", DefaultValue: "0", Description: "Clockwise rotation angle in degrees around the anchor point.", Aliases: ["rotate"]),
        new("scale", "number | point2d", DefaultValue: "1.0", Description: "Uniform scalar or 2D ({ x, y }) scale factor around the anchor point.", Aliases: ["transform_scale"]),
        new("shear", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Horizontal and vertical skew/shear factors ({ x, y })."),
        new("flip_x", "boolean", DefaultValue: "false", Description: "Mirrors the layer horizontally."),
        new("flip_y", "boolean", DefaultValue: "false", Description: "Mirrors the layer vertically."),
        new("anchor", "point2d", DefaultValue: "{ x = 0.5, y = 0.5 }", Description: "Normalized [0..1] or pixel pivot point for rotation, scaling, and shear.", Aliases: ["pivot"]),
        new("wrap", "enum", DefaultValue: "none", Description: "Edge wrapping mode when transforming pixels.", AllowedValues: ["none", "repeat", "mirror", "clamp"], Aliases: ["wrap_mode"]),
        new("resample", "enum", DefaultValue: "bilinear", Description: "Resampling filter used during spatial transforms.", AllowedValues: ["bilinear", "nearest"]),
    ];

    private sealed record EntryMetadata(
        string Category,
        string Description,
        IReadOnlyList<LayerPropertyDescriptor> Properties,
        string ExampleGfx);

    private static readonly Dictionary<string, EntryMetadata> LayerCatalog = BuildLayerCatalog();
    private static readonly Dictionary<string, EntryMetadata> EffectCatalog = BuildEffectCatalog();

    public static string GetLayerCategory(string layerType) =>
        LayerCatalog.TryGetValue(layerType, out var meta) ? meta.Category :
        EffectCatalog.TryGetValue(layerType, out var fxMeta) ? fxMeta.Category : "General";

    public static string GetLayerDescription(string layerType) =>
        LayerCatalog.TryGetValue(layerType, out var meta) ? meta.Description :
        EffectCatalog.TryGetValue(layerType, out var fxMeta) ? fxMeta.Description : $"Renders a '{layerType}' layer.";

    public static string GetLayerExample(string layerType) =>
        LayerCatalog.TryGetValue(layerType, out var meta) ? meta.ExampleGfx :
        EffectCatalog.TryGetValue(layerType, out var fxMeta) ? fxMeta.ExampleGfx : DefaultLayerExample(layerType);

    public static IReadOnlyList<LayerPropertyDescriptor> GetLayerPropertyDescriptors(
        string layerType,
        IReadOnlyCollection<string> supportedProperties)
    {
        if (LayerCatalog.TryGetValue(layerType, out var meta))
        {
            return MergeWithSupported(meta.Properties, supportedProperties);
        }

        if (EffectCatalog.TryGetValue(layerType, out var fxMeta))
        {
            return MergeWithSupported(fxMeta.Properties, supportedProperties);
        }

        return MergeWithSupported([], supportedProperties);
    }

    public static string GetEffectCategory(string effectType) =>
        EffectCatalog.TryGetValue(effectType, out var meta) ? meta.Category : "Post-Processing Effects";

    public static string GetEffectDescription(string effectType) =>
        EffectCatalog.TryGetValue(effectType, out var meta) ? meta.Description : $"Applies the '{effectType}' effect.";

    public static string GetEffectExample(string effectType) =>
        EffectCatalog.TryGetValue(effectType, out var meta) ? meta.ExampleGfx : DefaultEffectExample(effectType);

    public static IReadOnlyList<LayerPropertyDescriptor> GetEffectPropertyDescriptors(
        string effectType,
        IReadOnlyCollection<string> supportedProperties)
    {
        if (EffectCatalog.TryGetValue(effectType, out var meta))
        {
            return MergeWithSupported(meta.Properties, supportedProperties);
        }

        return MergeWithSupported([], supportedProperties);
    }

    private static IReadOnlyList<LayerPropertyDescriptor> MergeWithSupported(
        IReadOnlyList<LayerPropertyDescriptor> known,
        IReadOnlyCollection<string> supported)
    {
        if (supported.Count == 0)
        {
            return known;
        }

        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in known)
        {
            covered.Add(d.Name);
            if (d.Aliases is not null)
            {
                foreach (var alias in d.Aliases)
                {
                    covered.Add(alias);
                }
            }
        }

        var missing = supported.Where(p => !covered.Contains(p)).ToList();
        if (missing.Count == 0)
        {
            return known;
        }

        var result = new List<LayerPropertyDescriptor>(known);
        foreach (var prop in missing)
        {
            result.Add(new LayerPropertyDescriptor(prop, "any", IsRequired: false, Description: $"Optional '{prop}' parameter."));
        }

        return result;
    }

    private static string DefaultLayerExample(string layerType) => $$"""
        layers = {
          sample = {
            type = {{layerType}}
          }
        }
        output = {
          dimensions = { width = 64, height = 64 }
        }
        """;

    private static string DefaultEffectExample(string effectType) => $$"""
        layers = {
          bg = {
            type = fractal_clouds
            scale = 4
          }
          fx = {
            type = {{effectType}}
          }
        }
        output = {
          dimensions = { width = 64, height = 64 }
        }
        """;

    private static Dictionary<string, EntryMetadata> BuildLayerCatalog()
    {
        var map = new Dictionary<string, EntryMetadata>(StringComparer.OrdinalIgnoreCase);

        // 1. Core & Fill
        map["fill"] = new(
            "Core & Fill",
            "Renders a solid color fill across the layer bounds.",
            [
                new("color", "color | color_ramp", IsRequired: true, DefaultValue: "NAMED_COLORS.WHITE", Description: "Solid fill color (or color_ramp sampled at 0.0).")
            ],
            """
            layers = {
              background = {
                type = fill
                color = hex("#1e293b")
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["gradient"] = new(
            "Core & Fill",
            "Renders a smooth linear, radial, or conic color-ramp gradient.",
            [
                new("color", "color_ramp", IsRequired: true, DefaultValue: "color_ramp(0.0, NAMED_COLORS.BLACK, 1.0, NAMED_COLORS.WHITE)", Description: "Color ramp defining gradient stops."),
                new("style", "enum", DefaultValue: "linear", Description: "Gradient geometry style.", AllowedValues: ["linear", "radial", "conic"]),
                new("angle", "number", DefaultValue: "0", Description: "Gradient angle in degrees (used by linear and conic styles)."),
                new("center", "point2d", DefaultValue: "{ x = 32, y = 32 }", Description: "Gradient center point in pixels."),
                new("radius", "number", DefaultValue: "farthest_corner", Description: "Radial gradient falloff radius in pixels.")
            ],
            """
            layers = {
              sky = {
                type = gradient
                style = linear
                angle = 90
                color = color_ramp(0.0, hex("#0f172a"), 0.6, hex("#1d4ed8"), 1.0, hex("#38bdf8"))
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["image"] = new(
            "Core & Fill",
            "Loads and renders an external PNG/JPEG/WebP bitmap asset with optional cropping, fitting, tiling, and resampling.",
            [
                new("source", "string", IsRequired: true, DefaultValue: "\"sprite.png\"", Description: "Relative or absolute file path to the image asset.", Aliases: ["path"]),
                new("fit", "enum", DefaultValue: "stretch", Description: "How the source image fits into the layer bounds.", AllowedValues: ["stretch", "contain", "cover", "tile", "none"]),
                new("resample", "enum", DefaultValue: "bicubic", Description: "Resampling filter when scaling the image.", AllowedValues: ["nearest", "bilinear", "bicubic"]),
                new("crop", "object", Description: "Source crop rectangle ({ x = <int>, y = <int>, width = <int>, height = <int> })."),
                new("tile_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Pixel shift when fit = tile ({ x, y }).")
            ],
            """
            layers = {
              sprite = {
                type = image
                source = "assets/hero.png"
                fit = contain
                resample = nearest
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        // 2. Shapes & Text
        map["text"] = new(
            "Shapes & Text",
            "Renders anti-aliased typography with alignment, font family/path, weight, style, and optional outline stroke.",
            [
                new("text", "string", IsRequired: true, DefaultValue: "\"Hello\"", Description: "Text string (or number/boolean) to render."),
                new("font_size", "number", DefaultValue: "16", Description: "Font size in pixels."),
                new("color", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Text fill color."),
                new("align", "alignment", DefaultValue: "{ horizontal = ALIGN.LEFT, vertical = ALIGN.TOP }", Description: "Horizontal and vertical alignment within the layer."),
                new("font_family", "string", Description: "Installed system font family name."),
                new("font_path", "string", Description: "Path to a custom .ttf / .otf font file."),
                new("font_weight", "enum | number", DefaultValue: "normal", Description: "Font weight (100..900 or named weight).", AllowedValues: ["thin", "light", "normal", "medium", "semibold", "bold", "black"]),
                new("font_style", "enum", DefaultValue: "normal", Description: "Font slant style.", AllowedValues: ["normal", "italic", "oblique"]),
                new("stroke_color", "color", Description: "Optional text outline stroke color."),
                new("stroke_width", "number", DefaultValue: "1.0", Description: "Outline stroke width in pixels.")
            ],
            """
            layers = {
              bg = { type = fill, color = hex("#0f172a") }
              label = {
                type = text
                text = "GFX"
                font_size = 24
                font_weight = bold
                color = hex("#f8fafc")
                align = { horizontal = ALIGN.CENTER, vertical = ALIGN.CENTER }
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["rect"] = new(
            "Shapes & Text",
            "Renders a filled and/or stroked rectangle with optional rounded corners.",
            [
                new("color", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Interior fill color (at least one of color/fill_color or stroke_color is required).", Aliases: ["fill_color"]),
                new("stroke_color", "color", Description: "Border stroke color."),
                new("stroke_width", "number", DefaultValue: "0", Description: "Border stroke thickness in pixels."),
                new("corner_radius", "number", DefaultValue: "0", Description: "Rounded corner radius in pixels."),
                new("rotation", "number", DefaultValue: "0", Description: "Clockwise rotation angle in degrees.")
            ],
            """
            layers = {
              box = {
                type = rect
                size = { width = 40, height = 28 }
                color = hex("#3b82f6")
                stroke_color = hex("#93c5fd")
                stroke_width = 2
                corner_radius = 6
                transform = { offset = { x = 12, y = 18 } }
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        LayerPropertyDescriptor[] circleProps =
        [
            new("color", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Interior fill color (at least one of color/fill_color or stroke_color is required).", Aliases: ["fill_color"]),
            new("stroke_color", "color", Description: "Outline stroke color."),
            new("stroke_width", "number", DefaultValue: "0", Description: "Outline stroke width in pixels."),
            new("center", "point2d", DefaultValue: "layer center", Description: "Center point ({ x, y }) in pixels."),
            new("radius", "number", DefaultValue: "min(width, height) / 2", Description: "Uniform circle radius in pixels."),
            new("radii", "point2d", Description: "Independent horizontal and vertical radii ({ x, y }) for ellipses.")
        ];

        map["circle"] = new(
            "Shapes & Text",
            "Renders a filled and/or stroked circle or ellipse.",
            circleProps,
            """
            layers = {
              orb = {
                type = circle
                center = { x = 32, y = 32 }
                radius = 22
                color = hex("#f43f5e")
                stroke_color = hex("#ffe4e6")
                stroke_width = 2
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["ellipse"] = new(
            "Shapes & Text",
            "Renders a filled and/or stroked ellipse with independent horizontal and vertical radii.",
            circleProps,
            """
            layers = {
              oval = {
                type = ellipse
                center = { x = 32, y = 32 }
                radii = { x = 26, y = 16 }
                color = hex("#a855f7")
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["line"] = new(
            "Shapes & Text",
            "Renders an anti-aliased line segment between two points with rounded caps.",
            [
                new("color", "color", IsRequired: true, DefaultValue: "NAMED_COLORS.WHITE", Description: "Line stroke color.", Aliases: ["stroke_color"]),
                new("start", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Start point ({ x, y }) in pixels."),
                new("end", "point2d", DefaultValue: "{ x = width, y = height }", Description: "End point ({ x, y }) in pixels."),
                new("width", "number", DefaultValue: "1.0", Description: "Line thickness in pixels.", Aliases: ["stroke_width"])
            ],
            """
            layers = {
              beam = {
                type = line
                start = { x = 8, y = 56 }
                end = { x = 56, y = 8 }
                width = 4
                color = hex("#38bdf8")
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        LayerPropertyDescriptor[] polygonProps =
        [
            new("color", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Polygon fill color.", Aliases: ["fill_color"]),
            new("stroke_color", "color", Description: "Polygon outline color."),
            new("stroke_width", "number", DefaultValue: "0", Description: "Outline stroke width in pixels."),
            new("sides", "number", DefaultValue: "6 (or 5 for star)", Description: "Number of regular polygon sides or star points (3..512)."),
            new("radius", "number", DefaultValue: "min(width, height) / 2", Description: "Outer vertex radius in pixels."),
            new("inner_radius", "number", DefaultValue: "radius / 2", Description: "Inner vertex radius for star polygons."),
            new("center", "point2d", DefaultValue: "layer center", Description: "Center point ({ x, y }) in pixels."),
            new("rotation", "number", DefaultValue: "0", Description: "Rotation angle in degrees."),
            new("points", "array", Description: "Explicit array of vertex points ([ { x = ..., y = ... }, ... ]) overriding regular polygon generation.")
        ];

        map["polygon"] = new(
            "Shapes & Text",
            "Renders a regular polygon or custom multi-vertex polygon path.",
            polygonProps,
            """
            layers = {
              hex = {
                type = polygon
                sides = 6
                radius = 24
                color = hex("#10b981")
                stroke_color = hex("#d1fae5")
                stroke_width = 2
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["star"] = new(
            "Shapes & Text",
            "Renders a multi-pointed star polygon with configurable outer and inner radii.",
            polygonProps,
            """
            layers = {
              badge = {
                type = star
                sides = 5
                radius = 26
                inner_radius = 12
                color = hex("#facc15")
                stroke_color = hex("#fef08a")
                stroke_width = 2
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["sdf"] = new(
            "Shapes & Text",
            "Renders composable 2D Signed Distance Field shapes with smooth CSG boolean operations, strokes, outer glow, and distance isolines.",
            [
                new("shape", "enum", DefaultValue: "circle", Description: "Primary SDF primitive shape.", AllowedValues: ["circle", "box", "rounded_box", "ring", "segment", "polygon", "star", "cross", "vesica"], Aliases: ["primitive"]),
                new("shapes", "array", Description: "Array of SDF primitive objects combined with CSG boolean operations."),
                new("color", "color | color_ramp", DefaultValue: "NAMED_COLORS.WHITE", Description: "Shape fill color or distance-field color ramp.", Aliases: ["fill_color"]),
                new("background", "color", DefaultValue: "NAMED_COLORS.TRANSPARENT", Description: "Background color outside the SDF shape."),
                new("stroke_color", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Contour stroke color around d = 0."),
                new("stroke_width", "number", DefaultValue: "0", Description: "Contour stroke thickness in pixels."),
                new("glow_color", "color", Description: "Outer distance glow color."),
                new("glow_radius", "number", DefaultValue: "0", Description: "Outer distance glow falloff radius.", Aliases: ["glow"]),
                new("mode", "enum", DefaultValue: "shape", Description: "Rendering mode: crisp shape or distance isoline contours.", AllowedValues: ["shape", "isoline"], Aliases: ["style"]),
                new("bands", "number", DefaultValue: "8", Description: "Number of contour rings in isoline mode.", Aliases: ["contours"]),
                new("center", "point2d", DefaultValue: "layer center", Description: "Primitive center ({ x, y })."),
                new("radius", "number", DefaultValue: "0.3 * min_dim", Description: "Primitive radius in pixels."),
                new("corner_radius", "number", DefaultValue: "0", Description: "Box corner rounding radius."),
                new("rotation", "number", DefaultValue: "0", Description: "Primitive rotation in degrees."),
                new("vertices", "number", DefaultValue: "5", Description: "Polygon/star vertex count."),
                new("inner_radius", "number", Description: "Star inner radius."),
                new("thickness", "number", Description: "Ring/segment/cross thickness."),
                new("start", "point2d", Description: "Segment start point ({ x, y })."),
                new("end", "point2d", Description: "Segment end point ({ x, y })."),
                new("op", "enum", DefaultValue: "union", Description: "CSG boolean operation when combining shapes.", AllowedValues: ["union", "smooth_union", "subtract", "smooth_subtract", "intersect", "smooth_intersect", "xor"], Aliases: ["operation"]),
                new("smoothness", "number", DefaultValue: "8.0", Description: "Smooth CSG blending radius k.", Aliases: ["k"]),
                new("onion", "number", DefaultValue: "0", Description: "Concentric shell thickness (abs(d) - onion).")
            ],
            """
            layers = {
              metaballs = {
                type = sdf
                color = hex("#ec4899")
                glow_color = hex("#f472b6")
                glow_radius = 8
                shapes = [
                  { shape = circle, center = { x = 24, y = 32 }, radius = 14 },
                  { shape = box, center = { x = 40, y = 32 }, size = { width = 22, height = 22 }, corner_radius = 4, op = smooth_union, k = 10 }
                ]
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        // 3. Geometric Patterns
        map["stripes"] = new(
            "Geometric Patterns",
            "Renders repeating angled stripes with two alternating colors or a continuous color ramp.",
            [
                new("stripe_width", "number", DefaultValue: "8.0", Description: "Width of each stripe in pixels.", Aliases: ["width"]),
                new("angle", "number", DefaultValue: "45.0", Description: "Stripe direction angle in degrees."),
                new("color1", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "First alternating stripe color."),
                new("color2", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Second alternating stripe color.", Aliases: ["background"]),
                new("color", "color | color_ramp", Description: "Optional color ramp across each two-stripe period (or override for color1)."),
                new("phase", "number", DefaultValue: "0.0", Description: "Normalized phase shift [0..1] along the stripe normal."),
                new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pattern coordinate shift ({ x, y }) in pixels.", Aliases: ["phase_offset"])
            ],
            """
            layers = {
              hazard = {
                type = stripes
                stripe_width = 8
                angle = 45
                color1 = hex("#facc15")
                color2 = hex("#18181b")
                pattern_offset = { x = 4, y = 0 }
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["checkerboard"] = new(
            "Geometric Patterns",
            "Renders a crisp two-color alternating checkerboard grid.",
            [
                new("tile_size", "number | dimensions", DefaultValue: "8", Description: "Square tile size in pixels or { width, height }.", Aliases: ["cell_size"]),
                new("color1", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Even tile color.", Aliases: ["color"]),
                new("color2", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Odd tile color.", Aliases: ["background"]),
                new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pattern coordinate shift ({ x, y }) in pixels.", Aliases: ["phase_offset"])
            ],
            """
            layers = {
              board = {
                type = checkerboard
                tile_size = 8
                color1 = hex("#e2e8f0")
                color2 = hex("#475569")
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["grid"] = new(
            "Geometric Patterns",
            "Renders horizontal and vertical grid lines over a configurable background.",
            [
                new("cell_size", "number | dimensions", DefaultValue: "16", Description: "Grid cell spacing in pixels or { width, height }.", Aliases: ["tile_size"]),
                new("line_width", "number", DefaultValue: "1", Description: "Grid line thickness in pixels.", Aliases: ["thickness"]),
                new("color", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Grid line color."),
                new("background", "color", DefaultValue: "NAMED_COLORS.TRANSPARENT", Description: "Cell interior background color."),
                new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal grid coordinate shift ({ x, y }) in pixels.", Aliases: ["phase_offset"])
            ],
            """
            layers = {
              blueprint = {
                type = grid
                cell_size = 16
                line_width = 1
                color = hex("#38bdf8")
                background = hex("#0c4a6e")
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["dots"] = new(
            "Geometric Patterns",
            "Renders a repeating polka-dot / halftone circle grid.",
            [
                new("spacing", "number | dimensions", DefaultValue: "12.0", Description: "Center-to-center dot cell spacing in pixels or { width, height }.", Aliases: ["cell_size"]),
                new("radius", "number", DefaultValue: "4.0", Description: "Dot circle radius in pixels."),
                new("color", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Dot foreground color."),
                new("background", "color", DefaultValue: "NAMED_COLORS.TRANSPARENT", Description: "Background color between dots."),
                new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pattern coordinate shift ({ x, y }) in pixels.", Aliases: ["phase_offset"])
            ],
            """
            layers = {
              polka = {
                type = dots
                spacing = 16
                radius = 5
                color = hex("#f43f5e")
                background = hex("#1e1b4b")
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["bricks"] = new(
            "Geometric Patterns",
            "Renders procedural masonry and brick walls with running, stack, or herringbone bond, mortar joints, bevel shading, and per-brick color variation.",
            [
                new("brick_width", "number", DefaultValue: "32.0", Description: "Individual brick width in pixels."),
                new("brick_height", "number", DefaultValue: "16.0", Description: "Individual brick height in pixels."),
                new("brick_size", "dimensions", Description: "Combined brick dimensions ({ width, height })."),
                new("mortar_width", "number", DefaultValue: "2.0", Description: "Mortar joint thickness in pixels.", Aliases: ["mortar"]),
                new("mortar_color", "color", DefaultValue: "rgb(210, 206, 198)", Description: "Mortar joint color.", Aliases: ["background"]),
                new("bond", "enum", DefaultValue: "running", Description: "Brick laying bond pattern.", AllowedValues: ["running", "stack", "herringbone"], Aliases: ["style"]),
                new("row_offset", "number", DefaultValue: "0.5", Description: "Fractional horizontal shift between consecutive brick rows."),
                new("bevel", "number", DefaultValue: "0.15", Description: "Brick edge bevel shading intensity in [0..1]."),
                new("color_variation", "number", DefaultValue: "0.12", Description: "Per-brick random brightness variation in [0..1].", Aliases: ["variation"]),
                new("color", "color | color_ramp", DefaultValue: "rgb(178, 76, 52)", Description: "Base brick color or color ramp sampled per brick."),
                new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pattern coordinate shift ({ x, y }) in pixels.", Aliases: ["phase_offset"])
            ],
            """
            layers = {
              wall = {
                type = bricks
                brick_width = 24
                brick_height = 12
                mortar_width = 2
                bond = running
                color = hex("#b45309")
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["truchet"] = new(
            "Geometric Patterns",
            "Renders deterministic Truchet maze/curve tessellations using quarter-circle Smith arcs, diagonal maze lines, or split triangles.",
            [
                new("cell_size", "number | dimensions", DefaultValue: "16", Description: "Tile cell size in pixels.", Aliases: ["tile_size"]),
                new("style", "enum", DefaultValue: "arcs", Description: "Truchet tile motif.", AllowedValues: ["arcs", "diagonal", "triangles"], Aliases: ["pattern"]),
                new("line_width", "number", DefaultValue: "0.15 * cell_size", Description: "Curve/diagonal stroke width in pixels.", Aliases: ["width"]),
                new("color", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Foreground motif color."),
                new("background", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Tile background color."),
                new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pattern coordinate shift ({ x, y }) in pixels.", Aliases: ["phase_offset"])
            ],
            """
            layers = {
              maze = {
                type = truchet
                cell_size = 16
                style = arcs
                line_width = 3
                color = hex("#38bdf8")
                background = hex("#0f172a")
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        LayerPropertyDescriptor[] tilingProps =
        [
            new("style", "enum", DefaultValue: "hex", Description: "Tessellation geometry style.", AllowedValues: ["hex", "triangle", "isometric", "chevron"], Aliases: ["pattern"]),
            new("cell_size", "number", DefaultValue: "20.0", Description: "Characteristic cell radius/size in pixels."),
            new("line_width", "number", DefaultValue: "1.5", Description: "Cell border stroke width in pixels.", Aliases: ["stroke_width"]),
            new("stroke_color", "color", DefaultValue: "rgb(20, 24, 33)", Description: "Cell border stroke color."),
            new("color", "color | color_ramp", DefaultValue: "rgb(70, 130, 180)", Description: "Base cell fill color or color ramp.", Aliases: ["fill_color"]),
            new("color_variation", "number", DefaultValue: "0.15", Description: "Per-cell deterministic brightness variation in [0..1].", Aliases: ["variation"]),
            new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pattern coordinate shift ({ x, y }) in pixels.", Aliases: ["phase_offset"])
        ];

        map["tiling"] = new(
            "Geometric Patterns",
            "Renders geometric tessellations including hexagonal honeycombs, equilateral triangles, 3D isometric cubes, and chevron zigzags.",
            tilingProps,
            """
            layers = {
              cubes = {
                type = tiling
                style = isometric
                cell_size = 18
                color = hex("#6366f1")
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["hex_grid"] = new(
            "Geometric Patterns",
            "Renders a pointy-top hexagonal honeycomb grid with per-cell color variation and border strokes.",
            tilingProps,
            """
            layers = {
              hive = {
                type = hex_grid
                cell_size = 14
                color = hex("#f59e0b")
                stroke_color = hex("#451a03")
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["chevron"] = new(
            "Geometric Patterns",
            "Renders herringbone/chevron zigzag stripes with configurable cell size and stroke.",
            tilingProps,
            """
            layers = {
              zig = {
                type = chevron
                cell_size = 16
                color = hex("#14b8a6")
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["waves"] = new(
            "Geometric Patterns",
            "Renders periodic wave patterns including sine, square, sawtooth, triangle, concentric radial ripples, multi-source wave interference, and plasma.",
            [
                new("style", "enum", DefaultValue: "sine", Description: "Wave function type.", AllowedValues: ["sine", "square", "sawtooth", "triangle", "radial", "interference", "plasma"], Aliases: ["wave_type"]),
                new("frequency", "number", DefaultValue: "4.0", Description: "Wave spatial frequency.", Aliases: ["scale"]),
                new("angle", "number", DefaultValue: "0.0", Description: "Propagation angle in degrees for directional waves.", Aliases: ["rotation"]),
                new("phase", "number", DefaultValue: "0.0", Description: "Wave phase offset in cycles [0..1]."),
                new("sources", "number", DefaultValue: "3", Description: "Number of wave emitters in interference mode (1..16).", Aliases: ["count"]),
                new("color", "color | color_ramp", DefaultValue: "NAMED_COLORS.WHITE", Description: "Wave foreground color or color ramp."),
                new("background", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Wave trough background color."),
                new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pattern coordinate shift ({ x, y }) in pixels.", Aliases: ["phase_offset"])
            ],
            """
            layers = {
              ripples = {
                type = waves
                style = interference
                frequency = 5
                sources = 4
                color = color_ramp(0.0, hex("#0f172a"), 0.5, hex("#0ea5e9"), 1.0, hex("#e0f2fe"))
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["spiral"] = new(
            "Geometric Patterns",
            "Renders Archimedean, logarithmic, or vortex multi-arm spiral patterns.",
            [
                new("style", "enum", DefaultValue: "archimedean", Description: "Spiral radial progression curve.", AllowedValues: ["archimedean", "logarithmic", "vortex"]),
                new("arms", "number", DefaultValue: "2", Description: "Number of spiral arms (1..64).", Aliases: ["branches"]),
                new("tightness", "number", DefaultValue: "3.0", Description: "Spiral winding tightness.", Aliases: ["twist"]),
                new("phase", "number", DefaultValue: "0.0", Description: "Rotational phase offset in turns [0..1]."),
                new("center", "point2d", DefaultValue: "layer center", Description: "Spiral origin point ({ x, y }) in pixels."),
                new("color", "color | color_ramp", DefaultValue: "NAMED_COLORS.WHITE", Description: "Spiral arm color or color ramp."),
                new("background", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Background color between arms.")
            ],
            """
            layers = {
              vortex = {
                type = spiral
                style = vortex
                arms = 3
                tightness = 4.0
                color = color_ramp(0.0, hex("#1e1b4b"), 0.5, hex("#8b5cf6"), 1.0, hex("#f5d0fe"))
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        // 4. Procedural Noise & Organic
        LayerPropertyDescriptor[] fractalProps =
        [
            new("color", "color_ramp | color", DefaultValue: "white-to-black ramp", Description: "Color ramp mapping noise values in [0..1] to colors."),
            new("scale", "number", DefaultValue: "4.0", Description: "Base noise frequency across the layer.", Aliases: ["frequency"]),
            new("octaves", "number", DefaultValue: "5 (1 for perlin/simplex)", Description: "Number of fractional Brownian motion octaves (1..12)."),
            new("persistence", "number", DefaultValue: "0.5", Description: "Amplitude multiplier per octave.", Aliases: ["gain"]),
            new("lacunarity", "number", DefaultValue: "2.0", Description: "Frequency multiplier per octave."),
            new("phase", "number", DefaultValue: "0.0", Description: "3D Z-slice / animation phase coordinate.", Aliases: ["z"]),
            new("loop_seamless", "boolean", DefaultValue: "false", Description: "When true, orbits in 4D/3D space so phase = 0..1 loops seamlessly."),
            new("warp_strength", "number", DefaultValue: "0.0", Description: "Domain warping displacement amplitude."),
            new("warp_scale", "number", DefaultValue: "scale", Description: "Domain warping noise frequency."),
            new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pixel coordinate shift ({ x, y }) before sampling noise.", Aliases: ["phase_offset"])
        ];

        map["fractal_clouds"] = new(
            "Procedural Noise & Organic",
            "Renders multi-octave fractal Brownian motion (fBm) Perlin cloud textures with optional domain warping and seamless looping.",
            fractalProps,
            """
            layers = {
              clouds = {
                type = fractal_clouds
                scale = 4.0
                octaves = 5
                persistence = 0.55
                color = color_ramp(0.0, hex("#0f172a"), 0.5, hex("#3b82f6"), 1.0, hex("#f8fafc"))
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["perlin"] = new(
            "Procedural Noise & Organic",
            "Renders smooth gradient Perlin noise (defaults to 1 octave, configurable up to 12 octaves).",
            fractalProps,
            """
            layers = {
              noise = {
                type = perlin
                scale = 6.0
                color = color_ramp(0.0, NAMED_COLORS.BLACK, 1.0, NAMED_COLORS.WHITE)
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["simplex"] = new(
            "Procedural Noise & Organic",
            "Renders smooth gradient noise alias with configurable scale, octaves, and domain warping.",
            fractalProps,
            """
            layers = {
              noise = {
                type = simplex
                scale = 5.0
                color = color_ramp(0.0, hex("#111827"), 1.0, hex("#34d399"))
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        LayerPropertyDescriptor[] worleyProps =
        [
            new("color", "color_ramp | color", DefaultValue: "white-to-black ramp", Description: "Color ramp mapping cellular distance/value in [0..1]."),
            new("cell_size", "number | dimensions", DefaultValue: "16.0", Description: "Cellular grid size in pixels."),
            new("scale", "number", Description: "Alternative frequency/cell scale across the layer.", Aliases: ["frequency"]),
            new("jitter", "number", DefaultValue: "1.0", Description: "Feature point randomness within each cell in [0..1]."),
            new("distance_metric", "enum", DefaultValue: "euclidean", Description: "Distance metric between pixels and feature points.", AllowedValues: ["euclidean", "manhattan", "chebyshev"], Aliases: ["metric"]),
            new("feature", "enum", DefaultValue: "f1", Description: "Cellular feature function to evaluate.", AllowedValues: ["f1", "f2", "f2_minus_f1", "cell_value"]),
            new("warp_strength", "number", DefaultValue: "0.0", Description: "Perlin domain warp strength."),
            new("warp_scale", "number", DefaultValue: "4.0", Description: "Perlin domain warp frequency."),
            new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pixel coordinate shift ({ x, y }) before cellular evaluation.", Aliases: ["phase_offset"])
        ];

        map["worley"] = new(
            "Procedural Noise & Organic",
            "Renders cellular Worley / Voronoi noise supporting F1, F2, F2-F1 crystal edges, and flat Voronoi cell shading.",
            worleyProps,
            """
            layers = {
              cells = {
                type = worley
                cell_size = 16
                feature = f2_minus_f1
                color = color_ramp(0.0, hex("#090d16"), 0.3, hex("#0284c7"), 1.0, hex("#e0f2fe"))
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["voronoi"] = new(
            "Procedural Noise & Organic",
            "Renders Voronoi cellular diagrams and distance fields (alias for worley).",
            worleyProps,
            """
            layers = {
              mosaic = {
                type = voronoi
                cell_size = 14
                feature = cell_value
                color = color_ramp(0.0, hex("#312e81"), 0.5, hex("#8b5cf6"), 1.0, hex("#fcd34d"))
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["wood"] = new(
            "Procedural Noise & Organic",
            "Renders procedural wood grain using turbulent growth rings and fine axial fiber noise.",
            [
                new("rings", "number", DefaultValue: "8.0", Description: "Number of concentric wood growth rings.", Aliases: ["frequency"]),
                new("turbulence", "number", DefaultValue: "1.5", Description: "Ring distortion turbulence strength.", Aliases: ["warp_strength"]),
                new("grain", "number", DefaultValue: "0.25", Description: "Fine axial wood fiber intensity in [0..1]."),
                new("scale", "number", DefaultValue: "3.0", Description: "Noise frequency scale."),
                new("angle", "number", DefaultValue: "0.0", Description: "Grain orientation angle in degrees.", Aliases: ["rotation"]),
                new("color", "color_ramp", DefaultValue: "warm oak wood ramp", Description: "Color ramp mapping wood ring & fiber values."),
                new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pixel coordinate shift ({ x, y }).", Aliases: ["phase_offset"])
            ],
            """
            layers = {
              plank = {
                type = wood
                rings = 6
                turbulence = 1.8
                grain = 0.3
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["marble"] = new(
            "Procedural Noise & Organic",
            "Renders procedural veined marble textures using sine-wrapped multi-octave fBm turbulence.",
            [
                new("veins", "number", DefaultValue: "3.5", Description: "Marble vein frequency.", Aliases: ["frequency"]),
                new("turbulence", "number", DefaultValue: "5.0", Description: "Turbulent vein folding intensity.", Aliases: ["warp_strength"]),
                new("scale", "number", DefaultValue: "3.0", Description: "Underlying fBm noise scale."),
                new("octaves", "number", DefaultValue: "5", Description: "Number of fBm octaves (1..10)."),
                new("sharpness", "number", DefaultValue: "1.5", Description: "Vein contrast exponent."),
                new("angle", "number", DefaultValue: "25.0", Description: "Vein direction angle in degrees.", Aliases: ["rotation"]),
                new("phase", "number", DefaultValue: "0.0", Description: "3D noise phase offset."),
                new("color", "color_ramp", DefaultValue: "carrara marble ramp", Description: "Color ramp mapping marble values."),
                new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pixel coordinate shift ({ x, y }).", Aliases: ["phase_offset"])
            ],
            """
            layers = {
              stone = {
                type = marble
                veins = 4.0
                turbulence = 6.0
                sharpness = 1.8
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["caustics"] = new(
            "Procedural Noise & Organic",
            "Renders underwater refracted light networks via interfering ridged cellular Worley fields.",
            [
                new("scale", "number", DefaultValue: "4.0", Description: "Caustic network spatial frequency.", Aliases: ["frequency"]),
                new("sharpness", "number", DefaultValue: "2.5", Description: "Light crease concentration exponent."),
                new("phase", "number", DefaultValue: "0.0", Description: "Animation phase shift."),
                new("color", "color_ramp", DefaultValue: "deep water caustics ramp", Description: "Color ramp for water and light ridges."),
                new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pixel coordinate shift ({ x, y }).", Aliases: ["phase_offset"])
            ],
            """
            layers = {
              pool = {
                type = caustics
                scale = 4.0
                sharpness = 2.5
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        LayerPropertyDescriptor[] flowProps =
        [
            new("scale", "number", DefaultValue: "3.0", Description: "Vector potential field frequency."),
            new("octaves", "number", DefaultValue: "4", Description: "Number of fBm octaves (1..8)."),
            new("steps", "number", DefaultValue: "6", Description: "Advection integration steps along the curl vector (1..32).", Aliases: ["iterations"]),
            new("step_size", "number", DefaultValue: "0.12", Description: "Advection step distance.", Aliases: ["strength"]),
            new("phase", "number", DefaultValue: "0.0", Description: "3D potential field phase."),
            new("color", "color_ramp", DefaultValue: "cyberpunk flow ramp", Description: "Color ramp mapped to advected field."),
            new("pattern_offset", "point2d", DefaultValue: "{ x = 0, y = 0 }", Description: "Internal pixel coordinate shift ({ x, y }).", Aliases: ["phase_offset"])
        ];

        map["flow_field"] = new(
            "Procedural Noise & Organic",
            "Renders swirling divergence-free vector flow ribbons by advecting coordinates along the 2D curl of an fBm potential field.",
            flowProps,
            """
            layers = {
              streams = {
                type = flow_field
                scale = 3.5
                steps = 8
                step_size = 0.15
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["curl_noise"] = new(
            "Procedural Noise & Organic",
            "Renders divergence-free 2D curl noise turbulence (alias for flow_field).",
            flowProps,
            """
            layers = {
              swirls = {
                type = curl_noise
                scale = 4.0
                steps = 6
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["starfield"] = new(
            "Procedural Noise & Organic",
            "Renders a deterministic deep-space starfield with variable star radii, soft glow halos, nebula clouds, and animatable twinkle.",
            [
                new("count", "number", DefaultValue: "area / 48", Description: "Number of stars to render (1..20000).", Aliases: ["stars"]),
                new("density", "number", Description: "Optional star density per pixel overriding default count."),
                new("min_size", "number", DefaultValue: "0.5", Description: "Minimum star core radius in pixels.", Aliases: ["min_radius"]),
                new("max_size", "number", DefaultValue: "2.2", Description: "Maximum star core radius in pixels.", Aliases: ["max_radius"]),
                new("glow", "number", DefaultValue: "0.6", Description: "Halo glow intensity around brighter stars in [0..2]."),
                new("twinkle", "number", DefaultValue: "0.3", Description: "Phase-driven brightness modulation depth in [0..1]."),
                new("nebula", "number", DefaultValue: "0.0", Description: "Background procedural nebula intensity in [0..1]."),
                new("phase", "number", DefaultValue: "0.0", Description: "Animation phase in [0..1] driving star twinkle and nebula."),
                new("color", "color | color_ramp", DefaultValue: "NAMED_COLORS.WHITE", Description: "Star color or color ramp sampled per star."),
                new("background", "color", DefaultValue: "rgb(6, 9, 20)", Description: "Deep space background color.")
            ],
            """
            layers = {
              cosmos = {
                type = starfield
                count = 120
                glow = 0.8
                nebula = 0.5
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        LayerPropertyDescriptor[] rdProps =
        [
            new("preset", "enum", DefaultValue: "coral", Description: "Biological Turing pattern preset.", AllowedValues: ["coral", "spots", "fingerprints", "maze", "mitosis", "worms", "solitons"], Aliases: ["style"]),
            new("feed", "number", DefaultValue: "preset", Description: "Chemical U feed rate f (0.005..0.12).", Aliases: ["f"]),
            new("kill", "number", DefaultValue: "preset", Description: "Chemical V kill rate k (0.01..0.10).", Aliases: ["k"]),
            new("du", "number", DefaultValue: "1.0", Description: "Activator U diffusion rate."),
            new("dv", "number", DefaultValue: "0.5", Description: "Inhibitor V diffusion rate."),
            new("iterations", "number", DefaultValue: "350", Description: "Number of PDE simulation steps (10..4000).", Aliases: ["steps"]),
            new("seed_pattern", "enum", DefaultValue: "spots", Description: "Initial chemical nucleation pattern.", AllowedValues: ["spots", "center", "random"], Aliases: ["seed_mode"]),
            new("scale", "number", DefaultValue: "1.0", Description: "Simulation grid upscaling factor (0.5..8.0)."),
            new("color", "color_ramp | color", DefaultValue: "emerald Turing ramp", Description: "Color ramp or foreground color mapped to chemical V."),
            new("background", "color", DefaultValue: "rgb(15, 23, 42)", Description: "Background color when color is a single color.")
        ];

        map["reaction_diffusion"] = new(
            "Procedural Noise & Organic",
            "Simulates a deterministic 2D Gray-Scott reaction-diffusion system producing organic coral, leopard spots, labyrinthine mazes, and cellular mitosis patterns.",
            rdProps,
            """
            layers = {
              bio = {
                type = reaction_diffusion
                preset = coral
                iterations = 400
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        map["turing"] = new(
            "Procedural Noise & Organic",
            "Renders biological Turing morphogenesis patterns via Gray-Scott reaction-diffusion (alias for reaction_diffusion).",
            rdProps,
            """
            layers = {
              skin = {
                type = turing
                preset = spots
                iterations = 350
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);

        return map;
    }

    private static Dictionary<string, EntryMetadata> BuildEffectCatalog()
    {
        var map = new Dictionary<string, EntryMetadata>(StringComparer.OrdinalIgnoreCase);

        // 1. Core Filters & Stylization
        LayerPropertyDescriptor[] blurProps =
        [
            new("radius", "number", DefaultValue: "2.0", Description: "Blur sigma/radius in pixels.", Aliases: ["sigma"])
        ];

        map["gaussian_blur"] = new(
            "Surface, Print & Post-Processing Effects",
            "Applies a smooth Gaussian blur filter to a layer or canvas.",
            blurProps,
            DefaultEffectExample("gaussian_blur"));
        map["box_blur"] = map["gaussian_blur"];
        map["blur"] = map["gaussian_blur"];

        map["threshold"] = new(
            "Surface, Print & Post-Processing Effects",
            "Applies a binary luminance cutoff mapping dark pixels to low_color and bright pixels to high_color.",
            [
                new("level", "number", DefaultValue: "0.5", Description: "Luminance cutoff threshold in [0..1].", Aliases: ["threshold"]),
                new("high_color", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Color for pixels at or above the threshold."),
                new("low_color", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Color for pixels below the threshold.")
            ],
            DefaultEffectExample("threshold"));

        map["posterize"] = new(
            "Surface, Print & Post-Processing Effects",
            "Quantizes RGB channels into a discrete number of tonal steps (2..64).",
            [
                new("levels", "number", DefaultValue: "4", Description: "Number of quantized color levels per channel (2..64).", Aliases: ["steps"])
            ],
            DefaultEffectExample("posterize"));

        map["pixelate"] = new(
            "Surface, Print & Post-Processing Effects",
            "Downsamples the image into crisp mosaic pixel blocks of size block_size.",
            [
                new("block_size", "number", DefaultValue: "4", Description: "Mosaic block size in pixels (>= 1).", Aliases: ["size"])
            ],
            DefaultEffectExample("pixelate"));

        LayerPropertyDescriptor[] colorAdjustProps =
        [
            new("brightness", "number", DefaultValue: "0.0", Description: "Additive brightness shift in [-1..1]."),
            new("contrast", "number", DefaultValue: "0.0", Description: "Contrast adjustment in [-1..1]."),
            new("saturation", "number", DefaultValue: "0.0", Description: "Saturation boost/cut in [-1..1]."),
            new("hue_shift", "number", DefaultValue: "0.0", Description: "Hue rotation in degrees.", Aliases: ["hue"]),
            new("invert", "boolean", DefaultValue: "false", Description: "Whether to invert RGB channels.")
        ];

        map["color_adjust"] = new(
            "Surface, Print & Post-Processing Effects",
            "Adjusts brightness, contrast, saturation, hue rotation, and RGB inversion.",
            colorAdjustProps,
            DefaultEffectExample("color_adjust"));
        map["invert"] = new(
            "Surface, Print & Post-Processing Effects",
            "Inverts RGB color channels while preserving alpha.",
            colorAdjustProps,
            DefaultEffectExample("invert"));

        map["outline"] = new(
            "Surface, Print & Post-Processing Effects",
            "Draws a colored pixel stroke around non-transparent silhouettes.",
            [
                new("width", "number", DefaultValue: "2.0", Description: "Outline stroke radius in pixels.", Aliases: ["thickness"]),
                new("color", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Outline stroke color."),
                new("alpha_threshold", "number", DefaultValue: "16", Description: "Minimum alpha (1..255) treated as opaque foreground.", Aliases: ["threshold"])
            ],
            DefaultEffectExample("outline"));

        LayerPropertyDescriptor[] shadowProps =
        [
            new("offset", "point2d", DefaultValue: "{ x = 2, y = 2 }", Description: "Shadow displacement in pixels ({ x, y })."),
            new("blur", "number", DefaultValue: "1.5", Description: "Shadow Gaussian blur radius.", Aliases: ["radius"]),
            new("color", "color", DefaultValue: "rgba(0, 0, 0, 0.7)", Description: "Shadow tint and opacity.")
        ];

        map["drop_shadow"] = new(
            "Surface, Print & Post-Processing Effects",
            "Renders a shifted, blurred silhouette shadow behind a layer's alpha channel.",
            shadowProps,
            DefaultEffectExample("drop_shadow"));
        map["shadow"] = map["drop_shadow"];

        // 2. Spatial Distortion Effects
        LayerPropertyDescriptor[] warpProps =
        [
            new("strength", "number", DefaultValue: "8.0", Description: "Maximum pixel displacement distance.", Aliases: ["amount", "amplitude"]),
            new("scale", "number", DefaultValue: "4.0", Description: "Displacement noise frequency.", Aliases: ["frequency"]),
            new("octaves", "number", DefaultValue: "3", Description: "Number of fBm octaves when warping by procedural noise."),
            new("map", "identifier | string", Description: "Optional layer name to use as a red/green X/Y displacement map.", Aliases: ["displacement_map", "source"]),
            new("phase", "number", DefaultValue: "0.0", Description: "Noise animation phase."),
            new("wrap", "boolean", DefaultValue: "false", Description: "Whether displaced coordinates wrap around bitmap edges.")
        ];

        map["warp"] = new(
            "Spatial Distortion Effects",
            "Displaces pixels using multi-octave Perlin noise or another layer as a displacement map.",
            warpProps,
            DefaultEffectExample("warp"));
        map["displace"] = map["warp"];

        LayerPropertyDescriptor[] twirlProps =
        [
            new("angle", "number", DefaultValue: "180.0", Description: "Maximum twist rotation at the center in degrees.", Aliases: ["strength", "degrees"]),
            new("radius", "number", DefaultValue: "0.45 * min_dim", Description: "Vortex influence radius in pixels."),
            new("center", "point2d", DefaultValue: "layer center", Description: "Vortex center point ({ x, y }).")
        ];

        map["twirl"] = new(
            "Spatial Distortion Effects",
            "Rotates pixels around a center point with quadratic radial falloff.",
            twirlProps,
            DefaultEffectExample("twirl"));
        map["swirl"] = map["twirl"];

        map["ripple"] = new(
            "Spatial Distortion Effects",
            "Applies concentric radial or directional sine-wave water ripple displacement.",
            [
                new("amplitude", "number", DefaultValue: "4.0", Description: "Ripple displacement amplitude in pixels.", Aliases: ["strength"]),
                new("wavelength", "number", DefaultValue: "16.0", Description: "Distance between wave crests in pixels.", Aliases: ["frequency"]),
                new("phase", "number", DefaultValue: "0.0", Description: "Wave animation phase in cycles."),
                new("mode", "enum", DefaultValue: "radial", Description: "Radial pond ripples or directional wave distortion.", AllowedValues: ["radial", "directional", "xy"], Aliases: ["style"]),
                new("center", "point2d", DefaultValue: "layer center", Description: "Ripple origin for radial mode."),
                new("damping", "number", DefaultValue: "0.0", Description: "Radial amplitude attenuation factor.")
            ],
            DefaultEffectExample("ripple"));

        LayerPropertyDescriptor[] spherizeProps =
        [
            new("amount", "number", DefaultValue: "0.6 (-0.6 for pinch)", Description: "Lens curvature strength in [-1..1] (>0 fisheye bulge, <0 pinch).", Aliases: ["strength"]),
            new("radius", "number", DefaultValue: "0.5 * min_dim", Description: "Lens radius in pixels."),
            new("center", "point2d", DefaultValue: "layer center", Description: "Lens center point ({ x, y }).")
        ];

        map["spherize"] = new(
            "Spatial Distortion Effects",
            "Applies a 3D spherical fisheye bulge or barrel/pinch lens distortion.",
            spherizeProps,
            DefaultEffectExample("spherize"));
        map["pinch"] = map["spherize"];

        map["kaleidoscope"] = new(
            "Spatial Distortion Effects",
            "Folds polar angles into N symmetric mirrored wedge segments.",
            [
                new("segments", "number", DefaultValue: "6", Description: "Number of mirrored symmetry wedges (2..64).", Aliases: ["sides", "count"]),
                new("angle", "number", DefaultValue: "0.0", Description: "Output rotation angle in degrees.", Aliases: ["rotation"]),
                new("source_angle", "number", DefaultValue: "0.0", Description: "Source wedge sampling angle in degrees."),
                new("center", "point2d", DefaultValue: "layer center", Description: "Symmetry center point ({ x, y })."),
                new("wrap", "boolean", DefaultValue: "true", Description: "Whether out-of-bounds radial samples wrap.")
            ],
            DefaultEffectExample("kaleidoscope"));

        // 3. Surface, Print & Post-Processing
        map["emboss"] = new(
            "Surface, Print & Post-Processing Effects",
            "Computes directional surface relief lighting from luminance gradients.",
            [
                new("angle", "number", DefaultValue: "135.0", Description: "Light azimuth angle in degrees."),
                new("strength", "number", DefaultValue: "1.5", Description: "Relief height multiplier.", Aliases: ["amount", "height"]),
                new("blend", "number", DefaultValue: "0.0", Description: "Mix factor with original pixel colors in [0..1].")
            ],
            DefaultEffectExample("emboss"));

        map["bevel"] = new(
            "Surface, Print & Post-Processing Effects",
            "Adds 3D inner highlight and shadow beveling along alpha or luminance edges.",
            [
                new("depth", "number", DefaultValue: "4.0", Description: "Bevel ramp width in pixels.", Aliases: ["width", "radius"]),
                new("angle", "number", DefaultValue: "135.0", Description: "Light source angle in degrees."),
                new("strength", "number", DefaultValue: "0.7", Description: "Highlight/shadow intensity in [0..1].", Aliases: ["intensity"]),
                new("highlight_color", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Lit bevel edge color."),
                new("shadow_color", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Shadowed bevel edge color.")
            ],
            DefaultEffectExample("bevel"));

        map["normal_map"] = new(
            "Surface, Print & Post-Processing Effects",
            "Converts bitmap luminance/alpha height into a tangent-space RGB normal map ((Nx, Ny, Nz) -> (R, G, B)).",
            [
                new("strength", "number", DefaultValue: "2.5", Description: "Height-to-slope scale factor.", Aliases: ["scale", "height"]),
                new("invert_x", "boolean", DefaultValue: "false", Description: "Flips the X normal channel."),
                new("invert_y", "boolean", DefaultValue: "false", Description: "Flips the Y normal channel (DirectX vs OpenGL convention).")
            ],
            DefaultEffectExample("normal_map"));

        map["halftone"] = new(
            "Surface, Print & Post-Processing Effects",
            "Renders a rotated newsprint / comic halftone screen of dots, lines, or crosses sized by pixel luminance.",
            [
                new("dot_size", "number", DefaultValue: "6.0", Description: "Halftone screen cell size in pixels.", Aliases: ["cell_size", "size"]),
                new("angle", "number", DefaultValue: "45.0", Description: "Screen grid rotation angle in degrees."),
                new("shape", "enum", DefaultValue: "circle", Description: "Halftone mark shape.", AllowedValues: ["circle", "line", "cross", "diamond"], Aliases: ["style"]),
                new("color", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Ink dot color.", Aliases: ["ink_color"]),
                new("background", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Paper background color.", Aliases: ["paper_color"]),
                new("color_mode", "boolean", DefaultValue: "false", Description: "When true, preserves original pixel hue in halftone dots.")
            ],
            DefaultEffectExample("halftone"));

        LayerPropertyDescriptor[] ditherProps =
        [
            new("style", "enum", DefaultValue: "bayer (or stipple)", Description: "Dithering matrix or error-diffusion algorithm.", AllowedValues: ["bayer", "bayer2", "bayer4", "bayer8", "floyd_steinberg", "atkinson", "stipple", "blue_noise"], Aliases: ["mode", "pattern"]),
            new("levels", "number", DefaultValue: "2", Description: "Quantized tonal levels per channel (2..16)."),
            new("strength", "number", DefaultValue: "1.0", Description: "Dither threshold modulation amplitude.", Aliases: ["spread"]),
            new("color_mode", "boolean", DefaultValue: "true (false for stipple)", Description: "When true, dithers RGB channels independently; when false, dithers luminance between color and background."),
            new("color", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Foreground ink color in monochrome mode."),
            new("background", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Background color in monochrome mode.")
        ];

        map["dither"] = new(
            "Surface, Print & Post-Processing Effects",
            "Applies ordered Bayer matrix dithering, Floyd-Steinberg / Atkinson error diffusion, or stipple quantization.",
            ditherProps,
            DefaultEffectExample("dither"));
        map["stipple"] = map["dither"];

        LayerPropertyDescriptor[] edgeProps =
        [
            new("strength", "number", DefaultValue: "1.5", Description: "Sobel gradient magnitude multiplier.", Aliases: ["amount"]),
            new("threshold", "number", DefaultValue: "0.0", Description: "Minimum edge magnitude cutoff in [0..1]."),
            new("color", "color", DefaultValue: "NAMED_COLORS.WHITE", Description: "Detected edge color."),
            new("background", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Background color in non-overlay mode."),
            new("overlay", "boolean", DefaultValue: "false", Description: "When true, composites detected edges over the original image."),
            new("invert", "boolean", DefaultValue: "false", Description: "Inverts edge intensity (dark ink lines on light background).")
        ];

        map["edge_detect"] = new(
            "Surface, Print & Post-Processing Effects",
            "Extracts luminance contours using a 3x3 Sobel gradient operator.",
            edgeProps,
            DefaultEffectExample("edge_detect"));
        map["sobel"] = map["edge_detect"];

        map["vignette"] = new(
            "Surface, Print & Post-Processing Effects",
            "Darkens or tints the outer edges of a layer with a smooth radial falloff.",
            [
                new("strength", "number", DefaultValue: "0.75", Description: "Maximum edge darkening opacity in [0..1].", Aliases: ["amount", "intensity"]),
                new("radius", "number", DefaultValue: "0.8", Description: "Normalized radial distance where darkening reaches full strength."),
                new("softness", "number", DefaultValue: "0.45", Description: "Width of the smooth transition feather zone.", Aliases: ["feather"]),
                new("color", "color", DefaultValue: "NAMED_COLORS.BLACK", Description: "Vignette edge tint color.")
            ],
            DefaultEffectExample("vignette"));

        LayerPropertyDescriptor[] bloomProps =
        [
            new("threshold", "number", DefaultValue: "0.6", Description: "Luminance threshold above which pixels emit glow in [0..1]."),
            new("radius", "number", DefaultValue: "6.0", Description: "Gaussian blur radius for the glow pass.", Aliases: ["blur"]),
            new("intensity", "number", DefaultValue: "1.0", Description: "Additive glow brightness multiplier.", Aliases: ["strength", "amount"]),
            new("tint", "color", Description: "Optional color tint applied to the bloom pass.", Aliases: ["color"])
        ];

        map["bloom"] = new(
            "Surface, Print & Post-Processing Effects",
            "Extracts bright highlights, blurs them, and additively composites a luminous glow.",
            bloomProps,
            DefaultEffectExample("bloom"));
        map["glow"] = map["bloom"];

        map["chromatic_aberration"] = new(
            "Surface, Print & Post-Processing Effects",
            "Separates red, green, and blue color channels laterally or radially from the lens center.",
            [
                new("offset", "number | point2d", DefaultValue: "3.0", Description: "Channel separation distance in pixels or { x, y }.", Aliases: ["strength", "amount"]),
                new("angle", "number", DefaultValue: "0.0", Description: "Channel separation angle in degrees."),
                new("radial", "boolean", DefaultValue: "false", Description: "When true, scales channel fringing by distance from the center.")
            ],
            DefaultEffectExample("chromatic_aberration"));

        LayerPropertyDescriptor[] crtProps =
        [
            new("scanline_opacity", "number", DefaultValue: "0.35", Description: "Darkening strength of horizontal scanlines in [0..1].", Aliases: ["opacity", "strength"]),
            new("spacing", "number", DefaultValue: "2", Description: "Scanline period in pixels (>= 2).", Aliases: ["period", "line_height"]),
            new("curvature", "number", DefaultValue: "0.12 (0 for scanlines)", Description: "CRT barrel distortion curvature in [0..1]."),
            new("phosphor", "number", DefaultValue: "0.2", Description: "RGB subpixel aperture grille intensity in [0..1]."),
            new("vignette", "number", DefaultValue: "0.25", Description: "Corner tube darkening strength in [0..1].")
        ];

        map["crt"] = new(
            "Surface, Print & Post-Processing Effects",
            "Simulates a vintage CRT monitor with barrel curvature, horizontal scanlines, RGB phosphor mask, and corner vignette.",
            crtProps,
            DefaultEffectExample("crt"));
        map["scanlines"] = map["crt"];

        LayerPropertyDescriptor[] kuwaharaProps =
        [
            new("radius", "number", DefaultValue: "3", Description: "Anisotropic quadrant neighborhood radius in pixels (1..12).", Aliases: ["size"]),
            new("levels", "number", DefaultValue: "0", Description: "Optional tonal quantization steps for stylized brush strokes.")
        ];

        map["oil_paint"] = new(
            "Surface, Print & Post-Processing Effects",
            "Applies an edge-preserving Kuwahara filter that flattens textures into painterly brush regions.",
            kuwaharaProps,
            DefaultEffectExample("oil_paint"));
        map["kuwahara"] = map["oil_paint"];

        // 4. Programmable Shaders
        LayerPropertyDescriptor[] shaderProps =
        [
            new("fn", "function | string", DefaultValue: "code", Description: "JavaScript shader function name or inline code string.", Aliases: ["code", "script", "shader"]),
            new("language", "enum", DefaultValue: "auto", Description: "Shader language runtime (JavaScript or GPU SkSL).", AllowedValues: ["auto", "js", "sksl"]),
            new("uniforms", "object", Description: "Custom key-value parameters passed into ctx.params / SkSL uniforms.", Aliases: ["params"])
        ];

        map["shader"] = new(
            "Programmable Shaders",
            "Executes a programmable per-pixel shader in JavaScript or Skia SkSL with access to sample(), sampleLayer(), sampleCanvas(), noise(), and fbm().",
            shaderProps,
            """
            layers = {
              custom = {
                type = shader
                code = "return rgba(u * 255, v * 255, 180, 255);"
              }
            }
            output = {
              dimensions = { width = 64, height = 64 }
            }
            """);
        map["pixel_shader"] = map["shader"];

        return map;
    }
}
