# Agent Instructions

## Issue & Repository Conventions
- Interpret bare references like `#2` as GitHub issue numbers for this repository, unless the conversation clearly establishes a different meaning.
- When asked to look at an issue, inspect the issue and its discussion before deciding what work is needed. Do not treat `#N` as a numbered choice by default.

---

## Solution & Architecture Overview

GfxStudio is a deterministic 2D generative graphics engine, DSL, CLI tool, and visual preview editor built on .NET 8, SkiaSharp, and Avalonia.

### Project Layout
- **`src/GfxStudio.Core`**:
  - `Parsing/`: Lexer (`GfxLexer`), AST node hierarchy (`Parsing/Ast/`), and recursive-descent/Pratt parser (`GfxParser`).
  - `Evaluation/`: Expression and document evaluation (`GfxEvaluator`), value types (`GfxValue`), scopes and context (`EvaluationContext`).
  - `Functions/`: Built-in standard functions (`StandardFunctions`, e.g., `rgb`, `rgba`, `hsl`, `hex`, `color_ramp`, `random`, `lerp`, `smoothstep`).
  - `Randomness/`: Deterministic PRNG based on SplitMix64 (`DeterministicSeedProvider`, `GfxRandom`, `IGfxRandom`).
  - `Scripting/`: JavaScript scripting integration powered by Jint (`JsScriptHost`, `JsCallable`, `GfxJsValueConverter`).
  - `Rendering/`: SkiaSharp compositor (`GfxCanvasCompositor`), layer renderers (`Rendering/Layers/`), post-processing effects (`Rendering/Effects/`), and blend modes (`PixelBlender`).
  - `Output/`: Pipeline orchestrator (`GfxEngine`), configuration binder (`OutputConfigBinder`), and artifact writers for single images, series, sprite sheet / texture atlas image maps, and animated frame sequences / GIFs.
- **`src/GfxStudio.Cli`**:
  - Command-line tool `gfx` built with `System.CommandLine`. Supports `render`, `validate`, `inspect`, and live re-rendering `--watch`.
- **`src/GfxStudio.Editor`**:
  - Desktop GUI editor and live preview application built with Avalonia UI. Features live side-by-side rendering, frame navigation, checkerboard preview, gutter line numbers, and diagnostics list.
- **`tests/GfxStudio.Tests`**:
  - Comprehensive unit and integration regression test suite using xUnit and FluentAssertions.

---

## Common Build & Test Commands

Run commands from the repository root:

```powershell
# Build entire solution
dotnet build GfxStudio.sln

# Run all unit and integration tests
dotnet test GfxStudio.sln

# Run the CLI tool
dotnet run --project src/GfxStudio.Cli -- render samples/portal.gfx
dotnet run --project src/GfxStudio.Cli -- validate samples/portal.gfx
dotnet run --project src/GfxStudio.Cli -- inspect samples/portal.gfx

# Launch the visual editor
dotnet run --project src/GfxStudio.Editor
```

---

## Developer Workflows & Invariants

### 1. Determinism
- Rendering **must be 100% deterministic** for any given master seed.
- **Never use `System.Random` or non-deterministic time/hardware clocks directly in rendering or evaluation.**
- Use `LayerRenderContext.Random`, `EffectContext.Random`, or `DeterministicSeedProvider`.

### 2. Adding a New Layer Type
1. Implement `ILayerRenderer` in `src/GfxStudio.Core/Rendering/Layers/`.
2. Specify `LayerType` and return all recognized property names in `SupportedProperties`.
3. Register the renderer in `LayerRendererRegistry.CreateDefault()`.
4. Add unit and rendering tests in `tests/GfxStudio.Tests/Rendering/`.

### 3. Adding a New Effect
1. Implement `ILayerEffect` in `src/GfxStudio.Core/Rendering/Effects/`.
2. Register the effect in `LayerEffectRegistry.CreateDefault()`.
3. If the effect can be invoked as an inline layer property (e.g. `blur = 4`), register it in `GfxCanvasCompositor.InlineEffectBindings`.

### 4. Adding a Built-in Function
1. Add function implementation in `src/GfxStudio.Core/Functions/StandardFunctions.cs`.
2. Register in `StandardFunctions.RegisterAll(GfxFunctionRegistry registry)`.
3. Add unit tests in `tests/GfxStudio.Tests/Functions/StandardFunctionsTests.cs`.
