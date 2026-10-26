using System.Text;
using GfxStudio.Core.Rendering;
using GfxStudio.Core.Rendering.Effects;

namespace GfxStudio.Core.Documentation;

/// <summary>
/// Generates comprehensive GitHub Wiki Markdown documentation directly from
/// <see cref="LayerRendererRegistry"/>, <see cref="LayerEffectRegistry"/>, and <see cref="LayerMetadataCatalog"/>.
/// </summary>
public static class GfxWikiGenerator
{
    /// <summary>
    /// Generates all Wiki Markdown pages and returns a dictionary mapping filename (e.g. <c>Home.md</c>) to Markdown content.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GeneratePages(
        LayerRendererRegistry? layerRegistry = null,
        LayerEffectRegistry? effectRegistry = null)
    {
        var layers = layerRegistry ?? LayerRendererRegistry.CreateDefault();
        var effects = effectRegistry ?? LayerEffectRegistry.CreateDefault();

        var pages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Home.md"] = BuildHomePage(layers, effects),
            ["_Sidebar.md"] = BuildSidebarPage(),
            ["Getting-Started.md"] = BuildGettingStartedPage(),
            ["Language-Reference.md"] = BuildLanguageReferencePage(),
            ["Common-Layer-Properties-and-Transforms.md"] = BuildCommonPropertiesAndTransformsPage(),
            ["Layers-Overview.md"] = BuildLayersOverviewPage(layers),
            ["Layers-Core-and-Fill.md"] = BuildCategoryLayersPage(
                "Core & Fill Layers",
                "Core foundational layers for solid color fills, gradients, external bitmap images, and transparent placeholders.",
                layers,
                "Core & Fill"),
            ["Layers-Shapes-and-Text.md"] = BuildCategoryLayersPage(
                "Shapes & Text Layers",
                "Vector geometry, typography, and constructive solid geometry (2D SDF) layers.",
                layers,
                "Shapes & Text"),
            ["Layers-Geometric-Patterns.md"] = BuildCategoryLayersPage(
                "Geometric Pattern Layers",
                "Infinite procedural repeating patterns, tiles, weaves, and waves with seamless `pattern_offset` support.",
                layers,
                "Geometric Patterns"),
            ["Layers-Procedural-Noise-and-Organic.md"] = BuildCategoryLayersPage(
                "Procedural Noise & Organic Layers",
                "Deterministic multi-octave noise, cellular textures, natural materials, and reaction-diffusion systems.",
                layers,
                "Procedural Noise & Organic"),
            ["Effects-and-Shaders.md"] = BuildEffectsAndShadersPage(layers, effects),
            ["Output-Modes-and-CLI.md"] = BuildOutputModesAndCliPage()
        };

        return pages;
    }

    /// <summary>
    /// Writes all generated Wiki Markdown pages to <paramref name="outputDirectory"/> and returns the written file paths.
    /// </summary>
    public static IReadOnlyList<string> WriteToDirectory(
        string outputDirectory,
        LayerRendererRegistry? layerRegistry = null,
        LayerEffectRegistry? effectRegistry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var pages = GeneratePages(layerRegistry, effectRegistry);
        var writtenFiles = new List<string>(pages.Count);

        foreach (var (fileName, content) in pages.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            var fullPath = Path.Combine(outputDirectory, fileName);
            File.WriteAllText(fullPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writtenFiles.Add(fullPath);
        }

        return writtenFiles;
    }

    private static string BuildHomePage(LayerRendererRegistry layers, LayerEffectRegistry effects)
    {
        var nonEffectLayerCount = layers.RegisteredLayerTypes
            .Count(t => layers.TryGet(t, out var r) && r is not EffectAdjustmentLayerRenderer);
        var effectCount = effects.RegisteredEffectNames.Count;

        return $$"""
            # Welcome to the GfxStudio Wiki

            **GfxStudio** is a deterministic 2D generative graphics engine, domain-specific language (`.gfx`), CLI tool (`gfx`), and live visual preview editor built on .NET 8, SkiaSharp, and Avalonia.

            Every render in GfxStudio is **100% deterministic** for any given master seed—making it ideal for procedural game textures, sprite sheets, animated effects, and reproducible generative art.

            ---

            ## Documentation Sections

            ### User Guides
            - **[Getting Started](Getting-Started)** — Install GfxStudio, write your first `.gfx` scene, and use the CLI and Desktop Editor.
            - **[Language Reference](Language-Reference)** — Syntax, expressions, vector math, built-in constants, functions (`rgb`, `color_ramp`, `sample_layer`, `sample_canvas`), and JavaScript scripting.
            - **[Common Layer Properties & Transforms](Common-Layer-Properties-and-Transforms)** — Universal layer properties (`size`, `mix`, `opacity`, `mask`, `copy`, `seed`) and the `transform = { ... }` block.
            - **[Output Modes & CLI Reference](Output-Modes-and-CLI)** — Rendering single images, batch series, sprite sheet atlases (`image_map`), animations (`animation_frames`, `animation_gif`), and CLI commands (`render`, `validate`, `inspect`, `format`, `wiki`).

            ### Layer & Effect Reference ({{nonEffectLayerCount}} Layer Types & {{effectCount}} Effects)
            - **[Layers Overview](Layers-Overview)** — Complete directory of all layer types and categories.
            - **[Core & Fill Layers](Layers-Core-and-Fill)** — `fill`, `gradient`, `image`, `empty`
            - **[Shapes & Text Layers](Layers-Shapes-and-Text)** — `rect`, `circle`, `ellipse`, `line`, `polygon`, `star`, `text`, `sdf`
            - **[Geometric Pattern Layers](Layers-Geometric-Patterns)** — `stripes`, `checkerboard`, `grid`, `dots`, `bricks`, `truchet`, `tiling`, `hex_grid`, `chevron`, `waves`, `spiral`
            - **[Procedural Noise & Organic Layers](Layers-Procedural-Noise-and-Organic)** — `noise`, `fractal_clouds`, `perlin`, `simplex`, `worley`, `voronoi`, `wood`, `marble`, `caustics`, `flow_field`, `curl_noise`, `starfield`, `reaction_diffusion`, `turing`
            - **[Effects & Programmable Shaders](Effects-and-Shaders)** — All spatial distortion effects, surface/stylization filters, and JavaScript / SkSL pixel shaders.
            """.Replace("\r\n", "\n") + "\n";
    }

    private static string BuildSidebarPage()
    {
        return """
            ### GfxStudio Wiki
            * [Home](Home)
            * [Getting Started](Getting-Started)
            * [Language Reference](Language-Reference)
            * [Common Properties & Transforms](Common-Layer-Properties-and-Transforms)
            * [Output Modes & CLI](Output-Modes-and-CLI)

            ### Layer Reference
            * [Layers Overview](Layers-Overview)
            * [Core & Fill Layers](Layers-Core-and-Fill)
            * [Shapes & Text Layers](Layers-Shapes-and-Text)
            * [Geometric Pattern Layers](Layers-Geometric-Patterns)
            * [Procedural Noise & Organic](Layers-Procedural-Noise-and-Organic)
            * [Effects & Shaders](Effects-and-Shaders)
            """.Replace("\r\n", "\n") + "\n";
    }

    private static string BuildGettingStartedPage()
    {
        return """
            # Getting Started with GfxStudio

            ## 1. Building & Running

            Build the solution from the repository root using the .NET 8 SDK:

            ```powershell
            dotnet build GfxStudio.sln
            ```

            ### Launch the Live Visual Editor

            ```powershell
            dotnet run --project src/GfxStudio.Editor
            ```

            Key Editor shortcuts:
            - **`Ctrl+N` / `Ctrl+O` / `Ctrl+S`**: New, Open, Save `.gfx` document
            - **`Ctrl+Shift+L`**: Quick-Add Layer modal (guided layer builder with required/optional fields)
            - **`Shift+Alt+F` / `Ctrl+Shift+F`**: Format / Prettify `.gfx` document
            - **`Ctrl+E`**: Export rendered PNG

            ### Render from the Command Line

            ```powershell
            dotnet run --project src/GfxStudio.Cli -- render samples/portal.gfx
            ```

            ---

            ## 2. Anatomy of a `.gfx` Document

            A `.gfx` file is a declarative scene description with three primary top-level blocks:

            1. **`variables`** *(optional)* — Reusable parameters that can also be overridden from the CLI via `--var key=value`.
            2. **`layers`** *(required)* — Ordered bottom-to-top stack of named layers.
            3. **`output`** *(required for file export)* — Canvas dimensions, output mode, and target filename.

            ```gfx
            variables = {
              accent = hex("#ff4081")
            }

            layers = {
              bg = {
                type = gradient
                style = radial
                stops = [
                  { pos = 0.0, color = accent },
                  { pos = 1.0, color = hex("#0d1021") }
                ]
              }
              grid_overlay = {
                type = grid
                spacing = 16
                color = rgba(255, 255, 255, 0.15)
                mix = overlay
              }
            }

            output = {
              dimensions = { width = 256, height = 256 }
              filename = "first_render.png"
            }
            ```
            """.Replace("\r\n", "\n") + "\n";
    }

    private static string BuildLanguageReferencePage()
    {
        return """
            # Language Reference

            ## 1. Syntax & Data Types

            - **Comments**: `# line comment`, `// line comment`, and `/* block comment */`.
            - **Assignments**: `key = value` (or `key: value`, normalized to `key = value` by `gfx format`).
            - **Numbers**: Integer (`64`, `-5`) and floating-point (`0.5`, `3.14159`).
            - **Booleans**: `true` / `false` (or `True` / `False`).
            - **Strings & Interpolation**: `"text"` or interpolated `"frame_${frame.index:0000}.png"`.
            - **Objects**: `{ x = 10, y = 20 }` (2D points) or `{ width = 64, height = 64 }` (dimensions).
            - **Arrays**: `[1, 2, 3]` or arrays of objects `[{ pos = 0.0, color = ... }]`.

            ### Vector & Dimension Arithmetic
            2D points (`{ x, y }`) and dimensions (`{ width, height }`) support arithmetic with scalars and component-wise operations:
            - `{ x = 10, y = 20 } + { x = 5, y = -5 }` -> `{ x = 15, y = 15 }`
            - `{ width = 64, height = 64 } * 0.5` -> `{ width = 32, height = 32 }`
            - `-offset` -> negates both `x` and `y`.

            ---

            ## 2. Built-in Context Objects & Constants

            ### `run` Context
            - `run.index` — 0-based index of the current batch run (`0` in single mode).
            - `run.number` — 1-based number of the current batch run (`run.index + 1`).
            - `run.count` — Total number of runs.
            - `run.seed` — Effective deterministic seed for the current run.

            ### `frame` Context (Animations)
            - `frame.index` — 0-based frame index (`0 .. frame.count - 1`).
            - `frame.number` — 1-based frame number.
            - `frame.count` — Total number of frames.
            - `frame.t` — Normalized loop progress in `[0.0, 1.0)`.
            - `frame.progress` — Clamped progress in `[0.0, 1.0]` (`frame.index / max(1, frame.count - 1)`).
            - `frame.angle` — Full loop angle in degrees (`frame.t * 360.0`).
            - `frame.radians` — Full loop angle in radians (`frame.t * TAU`).

            ### Built-in Constants
            - **Math**: `PI`, `TAU`, `E`
            - **`NAMED_COLORS`**: `RED`, `GREEN`, `BLUE`, `WHITE`, `BLACK`, `TRANSPARENT`, `YELLOW`, `CYAN`, `MAGENTA`, `ORANGE`, `PURPLE`, `PINK`, `GRAY`, `GOLD`, `SILVER`, `TEAL`, `NAVY`, `LIME`, `CORAL`, `INDIGO`, `VIOLET`, etc.
            - **`ALIGN`**: `LEFT`, `CENTER`, `RIGHT`, `TOP`, `BOTTOM`

            ---

            ## 3. Built-in Standard Functions

            ### Color & Palette Functions
            | Function | Description |
            | :--- | :--- |
            | `rgb(r, g, b)` | Creates an opaque color from `0..255` (or normalized `0..1`) channels. |
            | `rgba(r, g, b, a)` | Creates a color with alpha (`a` in `0..1` or `0..255`). |
            | `hsl(h, s, l)` / `hsla(h, s, l, a)` | Creates a color from Hue (`0..360`), Saturation (`0..100` or `0..1`), Lightness (`0..100` or `0..1`). |
            | `hex("#rrggbb")` / `hex("#rrggbbaa")` | Parses a hex color string. |
            | `color_ramp(c1, c2, ...)` | Builds an evenly spaced multi-stop color ramp from 2 or more colors. |
            | `sample_ramp(ramp, t)` | Evaluates a `color_ramp` or stops list at `t` in `[0, 1]`. |
            | `sample_layer(layer_id, x, y [, normalized])` | Samples the rendered pixel color of `layer_id` at pixel `(x, y)` or normalized UV `(u, v)` when `normalized = true`. |
            | `sample_canvas(x, y [, normalized])` | Samples the cumulative composited canvas state of all layers prior to the current layer. |

            ### Math & Interpolation Functions
            | Function | Description |
            | :--- | :--- |
            | `min(a, b)`, `max(a, b)`, `clamp(v, min, max)` | Clamping and extrema. |
            | `abs(x)`, `floor(x)`, `ceil(x)`, `round(x)`, `fract(x)` | Rounding and fractional part. |
            | `sqrt(x)`, `pow(x, y)`, `exp(x)`, `log(x)` | Power and exponential functions. |
            | `sin(r)`, `cos(r)`, `tan(r)`, `asin(x)`, `acos(x)`, `atan(x)`, `atan2(y, x)` | Trigonometric functions (in radians). |
            | `deg_to_rad(deg)` / `rad(deg)`, `rad_to_deg(rad)` / `deg(rad)` | Angle unit conversion. |
            | `lerp(a, b, t)` / `mix(a, b, t)` | Linearly interpolates numbers, colors, points, or dimensions. |
            | `smoothstep(edge0, edge1, x)` | Hermite smooth interpolation in `[0, 1]`. |
            | `remap(v, inMin, inMax, outMin, outMax)` | Remaps `v` from one range to another. |

            ### Deterministic Randomness Functions
            | Function | Description |
            | :--- | :--- |
            | `random()` / `random(min, max)` | Deterministic float in `[0, 1)` or `[min, max)`. |
            | `random_int(min, max)` | Deterministic integer in `[min, max)`. |
            | `random_choice(array)` | Deterministically selects an element from an array. |
            | `random_bool([probability])` | Deterministic boolean (`true` with probability `p`, default `0.5`). |
            | `random_color()` | Deterministic opaque RGB color. |
            """.Replace("\r\n", "\n") + "\n";
    }

    private static string BuildCommonPropertiesAndTransformsPage()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Common Layer Properties & Transforms");
        sb.AppendLine();
        sb.AppendLine("Every layer in `layers = { ... }` supports a universal set of compositing properties as well as a unified `transform = { ... }` block.");
        sb.AppendLine();
        sb.AppendLine("## 1. Universal Layer Properties");
        sb.AppendLine();
        AppendPropertyTable(sb, GfxCanvasCompositor.UniversalPropertyDescriptors);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 2. The `transform = { ... }` Block");
        sb.AppendLine();
        sb.AppendLine("Spatial transformations can be grouped inside `transform = { ... }`. Grouping inside `transform` is especially useful on procedural noise layers (where top-level `scale` controls noise frequency while `transform.scale` scales the rendered layer bitmap) and effect layers (where `transform.offset` positions the layer without colliding with effect-specific `offset`).");
        sb.AppendLine();
        AppendPropertyTable(sb, GfxCanvasCompositor.TransformPropertyDescriptors);
        sb.AppendLine();
        sb.AppendLine("### Example");
        sb.AppendLine();
        sb.AppendLine("```gfx");
        sb.AppendLine("layers = {");
        sb.AppendLine("  bg = { type = fill, color = NAMED_COLORS.BLACK }");
        sb.AppendLine("  badge = {");
        sb.AppendLine("    type = star");
        sb.AppendLine("    points = 5");
        sb.AppendLine("    color = NAMED_COLORS.GOLD");
        sb.AppendLine("    size = { width = 96, height = 96 }");
        sb.AppendLine("    mix = screen");
        sb.AppendLine("    opacity = 0.9");
        sb.AppendLine("    transform = {");
        sb.AppendLine("      offset = { x = 16, y = 16 }");
        sb.AppendLine("      rotation = 15");
        sb.AppendLine("      scale = { x = 1.2, y = 1.2 }");
        sb.AppendLine("      anchor = { x = 0.5, y = 0.5 }");
        sb.AppendLine("      wrap = clamp");
        sb.AppendLine("      resample = bilinear");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("output = { dimensions = { width = 128, height = 128 } }");
        sb.AppendLine("```");

        return sb.ToString().Replace("\r\n", "\n");
    }

    private static string BuildLayersOverviewPage(LayerRendererRegistry layers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Layers Overview");
        sb.AppendLine();
        sb.AppendLine("GfxStudio provides a rich library of deterministic procedural, geometric, shape, image, and shader layer types.");
        sb.AppendLine();

        var categoryPages = new (string Category, string WikiPage)[]
        {
            ("Core & Fill", "Layers-Core-and-Fill"),
            ("Shapes & Text", "Layers-Shapes-and-Text"),
            ("Geometric Patterns", "Layers-Geometric-Patterns"),
            ("Procedural Noise & Organic", "Layers-Procedural-Noise-and-Organic"),
            ("Programmable Shaders", "Effects-and-Shaders"),
            ("Spatial Distortion Effects", "Effects-and-Shaders"),
            ("Surface, Print & Post-Processing Effects", "Effects-and-Shaders")
        };

        foreach (var (category, wikiPage) in categoryPages)
        {
            var matching = layers.RegisteredLayerTypes
                .Select(t => (Type: t, Renderer: layers.TryGet(t, out var r) ? r : null))
                .Where(x => x.Renderer is not null && string.Equals(x.Renderer.Category, category, StringComparison.Ordinal))
                .OrderBy(x => x.Type, StringComparer.Ordinal)
                .ToList();

            if (matching.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"## [{category}]({wikiPage})");
            sb.AppendLine();
            sb.AppendLine("| Layer `type` | Description | Required Properties |");
            sb.AppendLine("| :--- | :--- | :--- |");
            foreach (var (type, renderer) in matching)
            {
                var reqProps = renderer!.PropertyDescriptors
                    .Where(p => p.IsRequired)
                    .Select(p => $"`{p.Name}`")
                    .ToList();
                var reqText = reqProps.Count > 0 ? string.Join(", ", reqProps) : "*(none)*";
                sb.AppendLine($"| [`{type}`]({wikiPage}#{type}) | {EscapeTableCell(renderer.Description)} | {reqText} |");
            }

            sb.AppendLine();
        }

        return sb.ToString().Replace("\r\n", "\n");
    }

    private static string BuildCategoryLayersPage(
        string title,
        string intro,
        LayerRendererRegistry layers,
        string category)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine(intro);
        sb.AppendLine();
        sb.AppendLine("> All layers on this page also support the [Common Layer Properties & Transforms](Common-Layer-Properties-and-Transforms) (`size`, `mix`, `opacity`, `visible`, `seed`, `mask`, `copy`, `transform = { ... }`) and inline post-processing [Effects](Effects-and-Shaders).");
        sb.AppendLine();

        var renderers = layers.RegisteredLayerTypes
            .Select(t => (Type: t, Renderer: layers.TryGet(t, out var r) ? r : null))
            .Where(x => x.Renderer is not null && string.Equals(x.Renderer.Category, category, StringComparison.Ordinal))
            .OrderBy(x => x.Type, StringComparer.Ordinal)
            .ToList();

        foreach (var (type, renderer) in renderers)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"## `{type}`");
            sb.AppendLine();
            sb.AppendLine(renderer!.Description);
            sb.AppendLine();

            if (renderer.PropertyDescriptors.Count > 0)
            {
                sb.AppendLine("### Properties");
                sb.AppendLine();
                AppendPropertyTable(sb, renderer.PropertyDescriptors);
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("*This layer type has no layer-specific properties beyond the universal layer properties.*");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(renderer.ExampleGfx))
            {
                sb.AppendLine("### Example");
                sb.AppendLine();
                sb.AppendLine("```gfx");
                sb.AppendLine(renderer.ExampleGfx.Trim());
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString().Replace("\r\n", "\n");
    }

    private static string BuildEffectsAndShadersPage(LayerRendererRegistry layers, LayerEffectRegistry effects)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Effects & Programmable Shaders");
        sb.AppendLine();
        sb.AppendLine("Effects in GfxStudio can be used in **two ways**:");
        sb.AppendLine("1. **Inline on any layer**: Add the effect property (e.g. `gaussian_blur = { radius = 4 }` or `outline = { width = 2, color = NAMED_COLORS.BLACK }`) directly inside a layer block.");
        sb.AppendLine("2. **As an Adjustment Layer**: Create a standalone layer with `type = <effect_name>` in `layers = { ... }` to process the cumulative canvas below it.");
        sb.AppendLine();

        // 1. Programmable Shaders
        if (layers.TryGet("shader", out var shaderRenderer))
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## Programmable Pixel Shaders (`shader` / `pixel_shader`)");
            sb.AppendLine();
            sb.AppendLine(shaderRenderer.Description);
            sb.AppendLine();
            AppendPropertyTable(sb, shaderRenderer.PropertyDescriptors);
            sb.AppendLine();
            sb.AppendLine("### Built-in Shader Helpers (JavaScript & SkSL)");
            sb.AppendLine("- `sample(u, v [, wrap])` / `ctx.sample(u, v)` — Samples the current layer's input bitmap at normalized `(u, v)`.");
            sb.AppendLine("- `sampleLayer(layerId, u, v [, wrap])` / `ctx.sampleLayer(layerId, u, v)` — Samples any named layer in the scene.");
            sb.AppendLine("- `sampleCanvas(u, v [, wrap])` / `ctx.sampleCanvas(u, v)` — Samples the cumulative composited canvas state prior to the current layer.");
            sb.AppendLine("- `noise(x, y [, z])` / `fbm(x, y [, z, octaves])` — Deterministic Perlin/FBM noise seeded by the layer seed.");
            sb.AppendLine();
            sb.AppendLine("### Example");
            sb.AppendLine();
            sb.AppendLine("```gfx");
            sb.AppendLine(shaderRenderer.ExampleGfx.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // 2. Effects grouped by category
        var seenTypes = new HashSet<Type>();
        var distinctEffects = new List<(string PrimaryName, List<string> Aliases, ILayerEffect Effect)>();

        foreach (var name in effects.RegisteredEffectNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!effects.TryGet(name, out var effect) || effect is null)
            {
                continue;
            }

            if (string.Equals(effect.EffectType, "shader", StringComparison.Ordinal) ||
                string.Equals(effect.EffectType, "pixel_shader", StringComparison.Ordinal))
            {
                continue;
            }

            if (!seenTypes.Add(effect.GetType()))
            {
                continue;
            }

            var allNames = effects.RegisteredEffectNames
                .Where(n => effects.TryGet(n, out var e) && e?.GetType() == effect.GetType())
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            var primary = effect.EffectType;
            var aliases = allNames.Where(n => !string.Equals(n, primary, StringComparison.Ordinal)).ToList();
            distinctEffects.Add((primary, aliases, effect));
        }

        foreach (var category in new[] { "Spatial Distortion Effects", "Surface, Print & Post-Processing Effects" })
        {
            var group = distinctEffects
                .Where(x => string.Equals(x.Effect.Category, category, StringComparison.Ordinal))
                .OrderBy(x => x.PrimaryName, StringComparer.Ordinal)
                .ToList();

            if (group.Count == 0)
            {
                continue;
            }

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"## {category}");
            sb.AppendLine();

            foreach (var (primary, aliases, effect) in group)
            {
                var aliasHeader = aliases.Count > 0
                    ? $" (aliases: {string.Join(", ", aliases.Select(a => $"`{a}`"))})"
                    : string.Empty;
                sb.AppendLine($"### `{primary}`{aliasHeader}");
                sb.AppendLine();
                sb.AppendLine(effect.Description);
                sb.AppendLine();
                if (effect.PropertyDescriptors.Count > 0)
                {
                    AppendPropertyTable(sb, effect.PropertyDescriptors);
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(effect.ExampleGfx))
                {
                    sb.AppendLine("```gfx");
                    sb.AppendLine(effect.ExampleGfx.Trim());
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString().Replace("\r\n", "\n");
    }

    private static string BuildOutputModesAndCliPage()
    {
        return """
            # Output Modes & CLI Reference

            ## 1. Output Modes (`output = { ... }`)

            | Mode | Description | Key Properties |
            | :--- | :--- | :--- |
            | `single` *(default)* | Renders a single PNG image. | `dimensions`, `filename` |
            | `series` | Renders `count` variations across `run.index` (`0 .. count - 1`). | `dimensions`, `count`, `filename` (with `${run.number:0000}`) |
            | `image_map` | Renders `count` variations and packs them into a single sprite sheet / texture atlas PNG. | `cell_dimensions`, `columns`, `rows`, `count`, `padding`, `spacing`, `background`, `filename` |
            | `animation_frames` | Renders `frames` sequential animation frames using `frame.t` / `frame.index`. | `dimensions`, `frames`, `fps`, `filename` (with `${frame.number:0000}`) |
            | `animation_gif` | Renders an animated looping GIF (with optional `spritesheet` companion export). | `dimensions`, `frames`, `fps`, `loop`, `filename`, `spritesheet` |

            ---

            ## 2. CLI Commands (`gfx`)

            ### `gfx render <file.gfx>`
            Renders a `.gfx` file to disk.
            - `--seed <uint64>`: Override the master deterministic seed.
            - `--output <path>`: Override the output file or directory.
            - `--var <key=value>`: Override top-level `variables` entries (repeatable).
            - `--watch`: Watch the `.gfx` file and imported scripts for changes and re-render live.

            ### `gfx validate <file.gfx>`
            Parses, evaluates, and validates a `.gfx` file without writing output images. Exits with code `0` if valid, `1` on errors.

            ### `gfx inspect <file.gfx>`
            Prints structured metadata about a `.gfx` document (dimensions, output mode, variables, layers, and diagnostics).

            ### `gfx format <file.gfx> [--check]`
            Formats `.gfx` files in-place using `GfxFormatter` (2-space indentation, compact leaf objects, preserved comments).
            - `--check`: Verifies formatting without modifying files (exits with code `1` if unformatted).

            ### `gfx wiki --output <directory>`
            Generates all GitHub Wiki Markdown pages directly from the registered `ILayerRenderer` and `ILayerEffect` metadata catalog.
            """.Replace("\r\n", "\n") + "\n";
    }

    private static void AppendPropertyTable(StringBuilder sb, IReadOnlyList<LayerPropertyDescriptor> descriptors)
    {
        sb.AppendLine("| Property | Aliases | Type | Required | Default | Description |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var d in descriptors)
        {
            var aliases = d.Aliases is { Count: > 0 }
                ? string.Join(", ", d.Aliases.Select(a => $"`{a}`"))
                : "—";
            var req = d.IsRequired ? "**Yes**" : "No";
            var def = d.DefaultValue is not null ? $"`{EscapeTableCell(d.DefaultValue)}`" : "—";
            var desc = EscapeTableCell(d.Description);
            if (d.AllowedValues is { Count: > 0 })
            {
                desc += $" Allowed: {string.Join(", ", d.AllowedValues.Select(v => $"`{v}`"))}.";
            }

            sb.AppendLine($"| `{d.Name}` | {aliases} | `{EscapeTableCell(d.ValueType)}` | {req} | {def} | {desc} |");
        }
    }

    private static string EscapeTableCell(string? text) =>
        (text ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
