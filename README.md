# GfxStudio

[![CI](https://github.com/johnstrand/gfx-studio/actions/workflows/ci.yml/badge.svg)](https://github.com/johnstrand/gfx-studio/actions/workflows/ci.yml)
[![NuGet Core](https://img.shields.io/nuget/v/GfxStudio.Core.svg?label=GfxStudio.Core)](https://www.nuget.org/packages/GfxStudio.Core/)
[![NuGet Tool](https://img.shields.io/nuget/v/gfx-studio.svg?label=dotnet%20tool%20gfx)](https://www.nuget.org/packages/gfx-studio/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A programmable, deterministic 2D generative graphics engine and domain-specific language (`.gfx`) designed for procedural textures, game sprite sheets, atlas generation, and looping animated GIFs.

---

## Features

- **Declarative `.gfx` DSL**: Intuitive syntax with math expressions, color ramps, blend modes, filters, transforms, and string interpolation.
- **100% Cross-Platform Determinism**: Identical, bit-exact pixel rendering across Windows, Linux, and macOS using SkiaSharp and deterministic seeded PRNG hierarchies.
- **5 Output Modes**:
  1. `single` — Standalone high-resolution renders.
  2. `series` — Batch-generated numbered item variations.
  3. `image_map` — Packed 2D sprite sheets with structured JSON atlas metadata.
  4. `animation_frames` — Frame sequences for game sprite animations.
  5. `animation_gif` — Optimized animated GIFs with Floyd-Steinberg dithering and seamless 4D looping noise.
- **Rich Procedural Layer Engine**: Multi-octave Perlin Fractal Brownian Motion (fBm), Worley (cellular) noise, domain warping, gradients, geometric shapes, patterns, typography, and image loading.
- **Compositing & Effects**: 18 blend modes (Multiply, Screen, Overlay, etc.), Gaussian blur, drop shadows, outlines, posterization, pixelation, and color adjustments.
- **Embedded JavaScript Engine**: Seamless integration with modern JavaScript (ES modules) via Jint for complex procedural logic.

---

## Quickstart

### CLI Installation

Install the command-line tool globally via NuGet:

```bash
dotnet tool install -g gfx-studio
```

Then render any `.gfx` script:

```bash
gfx render samples/number.gfx --output-dir ./dist
```

### CLI Command Reference

```bash
gfx render <file.gfx> [options]

Options:
  -o, --output-dir <path>       Destination directory for generated artifacts [default: .]
  -s, --seed <ulong>            Master random seed override
  -c, --count <int>             Execution run count override for series / atlas modes
  -m, --mode <mode>             Output mode override (single, series, image_map, animation_frames, animation_gif)
  --fps <double>                Animation frame rate override
  --start-index <int>           First index for series runs
  --metadata-filename <name>    JSON sprite map output path
```

---

## Sample Gallery (`samples/`)

Explore the curated gallery of ready-to-run `.gfx` templates:

### 1. Single Worn Number (`samples/number.gfx`)
Single-image worn number generator combining solid fill, multi-octave fractal clouds, and centered typography.

```bash
gfx render samples/number.gfx
```

### 2. Series of Numbered Tiles (`samples/worn_numbers_series.gfx`)
Generates 20 distinct numbered tile variations using `mode = series` and soft drop shadows.

```bash
gfx render samples/worn_numbers_series.gfx --output-dir ./output_series
```

### 3. Sprite Sheet & Atlas Map (`samples/worn_numbers_atlas.gfx`)
Generates a $4 \times 5$ sprite sheet alongside JSON metadata detailing sprite coordinates, dimensions, and seeds:

```bash
gfx render samples/worn_numbers_atlas.gfx --output-dir ./output_atlas
```

### 4. Animation Frame Sequence (`samples/evolving_clouds_frames.gfx`)
Generates 10 sequential animation frames demonstrating continuous phase evolution and pulsing typography.

```bash
gfx render samples/evolving_clouds_frames.gfx --output-dir ./output_frames
```

### 5. Seamless Looping Portal GIF (`samples/looping_portal.gfx`)
Generates an animated GIF demonstrating seamless 4D torus fractal noise looping, cellular Worley noise, and radial lighting.

```bash
gfx render samples/looping_portal.gfx --output-dir ./output_gif
```

### 6. JavaScript-Driven Card Generator (`samples/scripted_cards.gfx`)
Procedural playing card deck generated using exported functions from `samples/scripts/card_generator.js`:

```bash
gfx render samples/scripted_cards.gfx --output-dir ./output_cards
```

---

## Embedding in C# / .NET

Install the core library:

```bash
dotnet add package GfxStudio.Core
```

Render directly from C#:

```csharp
using GfxStudio.Core.Output;

var engine = new GfxEngine();
using var result = engine.RenderFile("samples/looping_portal.gfx", new RenderOptions
{
    OutputDirectory = "./output"
});

foreach (var artifact in result.Artifacts)
{
    Console.WriteLine($"Rendered: {artifact.FilePath}");
}
```

See [docs/library-usage.md](docs/library-usage.md) for full instructions on writing custom layer renderers and function plugins.

---

## Documentation

- [`.gfx` Language Reference](docs/gfx-language-reference.md) — Comprehensive syntax, built-ins, and properties specification.
- [.NET Library Usage Guide](docs/library-usage.md) — Embed `GfxEngine` in your .NET applications and build custom plugins.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
