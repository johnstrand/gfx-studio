# Embedding GfxStudio in .NET Applications

`GfxStudio.Core` provides a modern, high-performance, deterministic 2D rendering pipeline that can be embedded into any .NET 8.0+ project.

---

## 1. Installation

Install the package via NuGet:

```bash
dotnet add package GfxStudio.Core
```

---

## 2. Quickstart: Rendering a `.gfx` File

```csharp
using GfxStudio.Core.Output;

// Instantiate the engine
var engine = new GfxEngine();

// Render with custom options
using var result = engine.RenderFile("path/to/texture.gfx", new RenderOptions
{
    SeedOverride = 1337,
    OutputDirectory = "output/renders"
});

if (result.HasErrors)
{
    foreach (var diag in result.Diagnostics)
    {
        Console.WriteLine($"[{diag.Severity}] {diag.Code}: {diag.Message}");
    }
    return;
}

// Access generated artifacts
foreach (var artifact in result.Artifacts)
{
    Console.WriteLine($"Rendered: {artifact.FilePath} ({artifact.Dimensions.Width}x{artifact.Dimensions.Height})");
}

if (result.MetadataFilePath is not null)
{
    Console.WriteLine($"Sprite atlas JSON: {result.MetadataFilePath}");
}
```

---

## 3. Rendering from an In-Memory String

You can also pass raw `.gfx` DSL strings directly without saving to disk:

```csharp
using GfxStudio.Core.Output;

var gfxScript = """
layers = {
  bg = {
    type = fill
    color = rgb(20, 30, 48)
  }
  clouds = {
    type = fractal_clouds
    scale = 2.0
    color = color_ramp(rgb(255, 120, 50), rgb(30, 144, 255))
    mix = screen
  }
}

output = {
  mode = single
  dimensions = { width = 256, height = 256 }
  filename = "clouds.png"
}
""";

var engine = new GfxEngine();
using var result = engine.RenderSource(gfxScript, new RenderOptions
{
    OutputDirectory = "output"
});

// Access the SkiaSharp SKBitmap directly in memory
using var bitmap = result.Artifacts[0].Bitmap;
```

---

## 4. Registering Custom Layer Renderers (`ILayerRenderer`)

Extend the GfxStudio DSL by implementing custom procedural layer generators:

```csharp
using GfxStudio.Core.Output;
using GfxStudio.Core.Rendering;
using GfxStudio.Core.Primitives;
using SkiaSharp;

public class StarfieldLayerRenderer : ILayerRenderer
{
    public string LayerType => "starfield";

    public SKBitmap RenderLayer(LayerRenderContext context)
    {
        var bitmap = context.CreateLayerBitmap();
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Read layer properties from .gfx document
        var starCount = 100;
        if (context.LayerProperties.TryGetProperty("count", out var countVal) &&
            countVal.TryCoerceToNumber(out var num))
        {
            starCount = (int)num;
        }

        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // Draw deterministic stars using the layer's scoped PRNG
        for (var i = 0; i < starCount; i++)
        {
            var x = (float)(context.Random.NextDouble() * bitmap.Width);
            var y = (float)(context.Random.NextDouble() * bitmap.Height);
            var radius = (float)(context.Random.NextDouble() * 1.5 + 0.5);
            canvas.DrawCircle(x, y, radius, paint);
        }

        return bitmap;
    }
}

// Register the custom renderer
var registry = LayerRendererRegistry.CreateDefault();
registry.Register(new StarfieldLayerRenderer());

var compositor = new GfxCanvasCompositor(registry);
var customEngine = new GfxEngine(compositor);

// Now your .gfx files can declare `type = starfield`!
```

---

## 5. Registering Custom Built-in Functions (`IGfxFunction`)

Add custom domain-specific functions to the expression evaluator:

```csharp
using GfxStudio.Core.Functions;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;

public class HexLuminanceFunction : IGfxFunction
{
    public string Name => "luminance";

    public int? ExpectedArgumentCount => 1;

    public GfxValue Invoke(IReadOnlyList<GfxValue> args, EvaluationContext context, SourceSpan span)
    {
        if (args[0] is GfxColor color)
        {
            var c = color.Color;
            var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return new GfxNumber(lum);
        }

        throw new GfxEvaluationException("Expected a color argument.", span);
    }
}

// Register custom function
var functionRegistry = FunctionRegistry.CreateDefault();
functionRegistry.Register(new HexLuminanceFunction());

var evaluator = new GfxEvaluator(functionRegistry);
```

---

## 6. Execution Model & Determinism

- **Determinism**: Every run derives hierarchical PRNG seeds from the master seed:
  `MasterSeed` $\rightarrow$ `RunSeed` $\rightarrow$ `FrameSeed` $\rightarrow$ `LayerSeed`.
- **Parallelism**: In `series` and `image_map` modes, tile generation utilizes multi-threaded parallel execution across CPU cores while guaranteeing 100% deterministic output across operating systems.
- **Resource Management**: Bitmaps implement `IDisposable`. Always wrap `RenderJobResult` in a `using` statement to release unmanaged Skia memory immediately after use.
