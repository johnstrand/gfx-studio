# `.gfx` Language Reference

GfxStudio uses a declarative domain-specific language (`.gfx`) designed for deterministic, procedural 2D generative graphics, texture synthesis, sprite atlas generation, and animation rendering.

---

## 1. Syntax & Lexical Structure

A `.gfx` document consists of assignments and structured blocks:

```gfx
// Single-line comment
/* Multi-line
   comment */

// Top-level property assignment
seed = 42

// Object block assignment
output = {
  mode = single
  dimensions = { width = 128, height = 128 }
  filename = "render_${run.number:0000}.png"
}

// Layers block
layers = {
  bg = {
    type = fill
    color = rgb(30, 41, 59)
  }
}
```

### Identifiers & Properties
- Keys are alphanumeric identifiers (`[a-zA-Z_][a-zA-Z0-9_]*`).
- Assignments use `=`, separated by newlines or commas.
- Dot navigation is supported in expressions: `output.dimensions.width`, `run.number`, `cards.getSuit(run.index)`.

---

## 2. Data Types & Literals

| Type | Syntax Examples | Description |
|---|---|---|
| **Number** | `42`, `3.14159`, `-10`, `1e-3` | 64-bit IEEE 754 floating-point number. |
| **String** | `"hello"`, `"run_${run.number:0000}.png"` | UTF-8 string with template interpolation. |
| **Boolean** | `true`, `false` | Boolean flags. |
| **Color** | `rgb(255, 0, 0)`, `rgba(0, 0, 0, 180)`, `hsv(180, 1, 1)` | 8-bit per channel RGBA color. |
| **Array** | `[1, 2, 3]`, `["spades", "hearts"]` | Ordered collection of values. |
| **Object** | `{ width = 64, height = 64 }` | Key-value mapping. |
| **Color Ramp** | `color_ramp(rgb(0,0,0), rgb(255,255,255))` | Multi-stop gradient ramp for noise and procedural mapping. |

### String Interpolation
Strings enclosed in double quotes support variable interpolation and formatting:
```gfx
filename = "tile_${run.index:0000}.png"
label = "Frame ${frame.number} of ${frame.count}"
```

---

## 3. Built-In Constants

### Named Colors (`NAMED_COLORS.*`)
- `NAMED_COLORS.WHITE` (`rgb(255, 255, 255)`)
- `NAMED_COLORS.BLACK` (`rgb(0, 0, 0)`)
- `NAMED_COLORS.RED` (`rgb(255, 0, 0)`)
- `NAMED_COLORS.GREEN` (`rgb(0, 255, 0)`)
- `NAMED_COLORS.BLUE` (`rgb(0, 0, 255)`)
- `NAMED_COLORS.YELLOW` (`rgb(255, 255, 0)`)
- `NAMED_COLORS.CYAN` (`rgb(0, 255, 255)`)
- `NAMED_COLORS.MAGENTA` (`rgb(255, 0, 255)`)
- `NAMED_COLORS.ORANGE` (`rgb(255, 165, 0)`)
- `NAMED_COLORS.PURPLE` (`rgb(128, 0, 128)`)
- `NAMED_COLORS.PINK` (`rgb(255, 192, 203)`)
- `NAMED_COLORS.GRAY` / `NAMED_COLORS.GREY` (`rgb(128, 128, 128)`)
- `NAMED_COLORS.TRANSPARENT` (`rgba(0, 0, 0, 0)`)

### Alignment (`ALIGN.*`)
Used for text and element anchoring:
- `ALIGN.CENTER`
- `ALIGN.LEFT`
- `ALIGN.RIGHT`
- `ALIGN.TOP`
- `ALIGN.BOTTOM`

### Math Constants
- `PI`: $\pi \approx 3.141592653589793$
- `TAU`: $2\pi \approx 6.283185307179586$
- `E`: $e \approx 2.718281828459045$

---

## 4. Context Variables

Context variables provide execution state during evaluation:

### Run Context (`run.*`)
Available in all execution modes:
- `run.number`: Current batch item or series execution index (0-based).
- `run.index`: Alias for `run.number`.
- `run.count`: Total number of runs in the batch or series.
- `run.seed`: 64-bit deterministic master seed for the current run.

### Frame Context (`frame.*`)
Available in animation modes (`animation_frames`, `animation_gif`):
- `frame.number`: Current frame index (0-based).
- `frame.index`: Alias for `frame.number`.
- `frame.count`: Total frame count in the animation sequence.
- `frame.time`: Timestamp in seconds (`frame.number / output.fps`).
- `frame.progress` (or shorthand `t`): Normalized progress through the loop $[0.0, 1.0)$.

### Output Context (`output.*`)
- `output.dimensions`: Dimension object `{ width, height }`.
- `output.width`: Canvas width.
- `output.height`: Canvas height.

---

## 5. Built-In Functions

### Random Functions
Random functions sample from independent, deterministic PRNG streams scoped to the current run, frame, and layer:
- `random(min, max)`: Uniform pseudo-random number in $[min, max)$.
- `random_frame(min, max)`: Frame-variant random value for animation noise.
- `random_int(min, max)`: Integer random value in $[min, max]$.
- `random_choice(array)`: Selects a random element from an array.

### Mathematical Functions
- `floor(x)`, `ceil(x)`, `round(x)`: Rounding functions.
- `abs(x)`: Absolute value.
- `sign(x)`: Sign indicator ($-1, 0, 1$).
- `min(a, b, ...)`, `max(a, b, ...)`: Minimum and maximum.
- `clamp(val, min, max)`: Clamps value to range.
- `sqrt(x)`: Square root.
- `pow(base, exp)`: Power exponentiation.
- `exp(x)`, `log(x)`: Natural exponential and logarithm.
- `sin(rad)`, `cos(rad)`, `tan(rad)`: Trigonometric functions.
- `asin(x)`, `acos(x)`, `atan2(y, x)`: Inverse trigonometry.
- `deg_to_rad(degrees)`, `rad_to_deg(radians)`: Angle conversion.

### Interpolation & Easing
- `lerp(a, b, t)`: Linear interpolation $(1 - t)a + tb$.
- `smoothstep(edge0, edge1, x)`: Hermite polynomial interpolation.
- `ease_in(t)`: Quadratic ease-in ($t^2$).
- `ease_out(t)`: Quadratic ease-out ($1 - (1 - t)^2$).
- `ease_in_out(t)`: Smooth quadratic ease in/out.

### Color Functions
- `rgb(r, g, b)`: RGB color ($0..255$).
- `rgba(r, g, b, a)`: RGBA color with alpha ($0..255$).
- `hsv(h, s, v)`: Hue ($0..360$), Saturation ($0..1$), Value ($0..1$).
- `hsva(h, s, v, a)`: HSVA color with alpha ($0..255$).
- `color_ramp(stops...)`: Builds a multi-stop color ramp.
- `color_stop(position, color)`: Explicitly positioned gradient stop ($position \in [0.0, 1.0]$).

---

## 6. Layer Types & Procedural Generators

Layers are declared in `layers = { ... }` in ascending draw order (first layer is drawn first, subsequent layers composite on top).

### Texture & Noise Layers
- **`fill`**: Fills the layer bounds with a solid color (`color = rgb(...)`).
- **`gradient`**: Linear, radial, or conic gradients.
  - `style`: `"linear"`, `"radial"`, or `"conic"`
  - `color`: `color_ramp(...)`
  - `angle`: Rotation in degrees (linear/conic)
  - `center`: `{ x, y }` focus point
  - `radius`: Radius for radial gradients
- **`fractal_clouds`** / **`clouds`**: Multi-octave Perlin/Simplex Fractal Brownian Motion (fBm).
  - `color`: `color_ramp(...)`
  - `scale`: Spatial frequency scaling (default: `4.0`)
  - `octaves`: Noise octaves (1 to 12, default: `5`)
  - `persistence` / `gain`: Octave amplitude falloff (default: `0.5`)
  - `lacunarity`: Octave frequency multiplier (default: `2.0`)
  - `phase` / `z`: 3D/4D time coordinate for animation
  - `loop_seamless`: `true` to enable 4D circular torus sampling for perfectly seamless loops
  - `warp_strength`, `warp_scale`: Domain warping parameters
- **`worley`** / **`voronoi`**: Cellular Voronoi distance field.
  - `color`: `color_ramp(...)`
  - `cell_size` or `cells`: Cell density
  - `metric`: `"euclidean"`, `"manhattan"`, or `"chebyshev"`
  - `feature`: `"f1"`, `"f2"`, `"f2_minus_f1"`, or `"cells"`
  - `jitter`: Cell point displacement ($0.0..1.0$)
- **`perlin`** / **`simplex`**: Low-level 2D/3D coherent noise generators.

### Procedural Patterns
- **`checkerboard`**: Alternating checker grid (`color1`, `color2`, `cell_size`).
- **`stripes`**: Angled procedural stripes (`color1`, `color2`, `width`, `angle`).
- **`grid`**: Coordinate grid lines (`color`, `background`, `cell_size`, `line_width`).
- **`dots`**: Dot lattice pattern (`color`, `background`, `radius`, `spacing`).

### Vector Shapes
- **`rect`**: Rectangle or rounded rectangle (`fill_color`, `stroke_color`, `stroke_width`, `corner_radius`, `rotation`).
- **`circle`**: Circle or ellipse (`fill_color`, `stroke_color`, `stroke_width`, `radius`).
- **`line`**: Line segment (`start = { x, y }`, `end = { x, y }`, `color`, `width`, `cap`).
- **`polygon`**: Regular n-sided polygon (`vertices`, `radius`, `fill_color`, `stroke_color`).

### Typography & Assets
- **`text`**: Text rendering with automatic glyph metrics and font resolution.
  - `text`: String or number to render
  - `font_size`: Font size in pixels
  - `color`: Font color
  - `align`: `{ vertical = ALIGN.CENTER, horizontal = ALIGN.CENTER }`
  - `font_family`: System font family or bundled fallback (Roboto)
  - `font_path`: Relative or absolute file path to a `.ttf`/`.otf` typeface
- **`image`**: External bitmap asset loader.
  - `path`: Image file path
  - `fit`: `"fill"`, `"contain"`, `"cover"`, `"tile"`, or `"center"`
  - `resample`: `"linear"` or `"nearest"`

---

## 7. Blend Modes (`mix`)

Every layer supports the `mix` property specifying how its pixels blend over the canvas:

- `normal` (default alpha blending)
- `multiply`
- `screen`
- `overlay`
- `darken`, `lighten`
- `color_dodge`, `color_burn`
- `hard_light`, `soft_light`
- `difference`, `exclusion`
- `hue`, `saturation`, `color`, `luminosity`
- `add`, `subtract`

---

## 8. Layer Effects & Adjustment Layers

Effects can be attached directly to any layer (e.g. `drop_shadow = { ... }`) or declared as a standalone adjustment layer (`type = gaussian_blur`) affecting all rendered layers beneath it:

- **`gaussian_blur`** / **`blur`**: Gaussian box blur convolution.
  ```gfx
  blur = 4
  // or
  blur = { radius = 4 }
  ```
- **`threshold`**: Binarizes pixels based on luminance.
  ```gfx
  threshold = { level = 0.5, high_color = rgb(255, 255, 255), low_color = rgb(0, 0, 0) }
  ```
- **`posterize`**: Quantizes color channels into discrete tone steps.
  ```gfx
  posterize = { levels = 4 }
  ```
- **`pixelate`**: Downsamples the layer into pixel blocks.
  ```gfx
  pixelate = { cell_size = 8 }
  ```
- **`color_adjust`**: Adjusts brightness, contrast, saturation, and hue.
  ```gfx
  color_adjust = { brightness = 0.1, contrast = 1.2, saturation = 1.3, hue = 15 }
  ```
- **`outline`**: Stroked outline contour around non-transparent pixel silhouettes.
  ```gfx
  outline = { width = 2, color = rgb(0, 0, 0) }
  ```
- **`drop_shadow`**: Soft blurred offset drop shadow.
  ```gfx
  drop_shadow = { offset = { x = 2, y = 3 }, blur = 3, color = rgba(0, 0, 0, 180) }
  ```

---

## 9. Transforms & Masking

Every layer supports universal 2D geometric transforms and clipping masks:

```gfx
layer_star = {
  type = polygon
  vertices = 5
  scale = { x = 1.2, y = 1.2 }
  rotation = 45.0
  offset = { x = 10, y = 20 }
  mask = background_layer
}
```

- `scale`: Number or `{ x, y }` scaling factors.
- `rotation`: Clockwise rotation in degrees around layer center.
- `offset` or `position`: `{ x, y }` translation offset.
- `opacity`: Alpha multiplier ($0.0$ to $1.0$).
- `mask`: Name of another layer in the document whose alpha channel clips this layer.

---

## 10. JavaScript Scripting (`scripts`)

You can import ES modules or CommonJS scripts to calculate procedural properties dynamically on each render:

```gfx
scripts = {
  cards = "scripts/card_generator.js"
}

layers = {
  card_text = {
    type = text
    text = cards.getCardLabel(run.index)
    color = cards.getCardColor(run.index)
  }
}
```

JavaScript scripts export standard functions taking primitives and returning strings, numbers, booleans, objects (`{ r, g, b, a }`), or arrays.

---

## 11. Output Modes & Configuration

Configured under the `output = { ... }` block:

```gfx
output = {
  mode = image_map
  dimensions = { width = 64, height = 64 }
  count = 20
  columns = 4
  padding = 2
  spacing = 2
  background = rgb(15, 23, 42)
  filename = "spritesheet.png"
  metadata_filename = "spritesheet.json"
}
```

### Supported Output Modes
1. **`single`** (default): Renders a single image file.
2. **`series`**: Renders a numbered batch sequence of distinct image files.
3. **`image_map`**: Packs rendered tiles into a 2D sprite atlas with an accompanying JSON coordinate map.
4. **`animation_frames`**: Generates an animation as individual sequential frame PNG images.
5. **`animation_gif`**: Encodes a fully self-contained animated GIF file with palette quantization and Floyd-Steinberg dithering.

### Output Properties Table

| Property | Type | Default | Description |
|---|---|---|---|
| `mode` | symbol/string | `single` | Output mode (`single`, `series`, `image_map`, `animation_frames`, `animation_gif`). |
| `dimensions` | object | `{ width = 64, height = 64 }` | Individual tile/frame size `{ width, height }`. |
| `filename` | string | **required** | File destination path/template. |
| `count` / `runs` | integer | `1` | Total execution count for batch and atlas runs. |
| `frames` | integer | `1` | Frame count for animation modes. |
| `start_index` | integer | `0` | First index offset for `run.index`. |
| `columns` | integer | `ceil(sqrt(count))` | Number of columns in atlas sprite sheets. |
| `padding` | integer | `0` | Outer border padding in pixels for atlas sprite sheets. |
| `spacing` | integer | `0` | Gutter spacing between tiles in atlas sprite sheets. |
| `background` | color | `rgba(0, 0, 0, 0)` | Canvas backdrop color for atlas sprite sheets. |
| `metadata_filename` | string | `null` | JSON metadata output path for `image_map`. |
| `fps` | double | `15.0` | Playback frame rate for animation output. |
| `frame_delay_ms`| integer | `null` | Explicit millisecond delay per GIF frame override. |
| `loop` | integer | `0` | GIF loop count (0 = infinite loop). |
| `dither` | boolean | `true` | Enables Floyd-Steinberg error-diffusion dithering for GIF encoding. |
