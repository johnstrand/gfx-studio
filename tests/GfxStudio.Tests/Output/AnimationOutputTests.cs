using FluentAssertions;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Output;
using GfxStudio.Core.Randomness;
using SkiaSharp;

namespace GfxStudio.Tests.Output;

public sealed class AnimationOutputTests
{
    [Fact]
    public void AnimationFrames_OutputsTenSequentialFilesWithFrameProgressFromZeroToPointNine()
    {
        const string source = """
            layers = {
              bg = {
                type = fill
                color = rgb(round(frame.progress * 200), round(t * 100), round(frame.time * 100))
              }
            }
            output = {
              mode = animation_frames
              frames = 10
              fps = 10
              dimensions = { width = 8, height = 8 }
              filename = "frame_${frame.number:0000}.png"
            }
            """;

        var outputDirectory = CreateTemporaryDirectory();
        try
        {
            using var result = new GfxEngine().RenderSource(source, new RenderOptions
            {
                OutputDirectory = outputDirectory,
                SeedOverride = 42
            });

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().HaveCount(10);

            for (var i = 0; i < 10; i++)
            {
                var artifact = result.Artifacts[i];
                Path.GetFileName(artifact.FilePath).Should().Be($"frame_{i:0000}.png");
                File.Exists(artifact.FilePath).Should().BeTrue();

                var expectedProgress = i / 10.0;
                var expectedRed = (byte)Math.Round(expectedProgress * 200, MidpointRounding.AwayFromZero);
                var expectedGreen = (byte)Math.Round(expectedProgress * 100, MidpointRounding.AwayFromZero);
                var expectedBlue = (byte)Math.Round((i / 10.0) * 100, MidpointRounding.AwayFromZero);

                var pixel = artifact.Bitmap.GetPixel(4, 4);
                pixel.Red.Should().Be(expectedRed);
                pixel.Green.Should().Be(expectedGreen);
                pixel.Blue.Should().Be(expectedBlue);
            }
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnimationFrames_WithStaticFilename_AutomaticallyAppendsSequentialFrameNumbers()
    {
        const string source = """
            layers = {
              bg = {
                type = fill
                color = rgb(frame.number * 20, 0, 0)
              }
            }
            output = {
              mode = animation_frames
              frames = 4
              dimensions = { width = 4, height = 4 }
              filename = "frame.png"
            }
            """;

        var outputDirectory = CreateTemporaryDirectory();
        try
        {
            using var result = new GfxEngine().RenderSource(source, new RenderOptions
            {
                OutputDirectory = outputDirectory
            });

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().HaveCount(4);
            result.Artifacts.Select(a => Path.GetFileName(a.FilePath))
                .Should().Equal("frame_0000.png", "frame_0001.png", "frame_0002.png", "frame_0003.png");
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnimationGif_FifteenFramesAtFifteenFps_DecodesViaSKCodecWithFifteenFramesAndSeventyMsDelay()
    {
        const string source = """
            layers = {
              bg = {
                type = fill
                color = rgb(round(frame.progress * 240), 40, 120)
              }
            }
            output = {
              mode = animation_gif
              frames = 15
              fps = 15
              loop = 0
              dimensions = { width = 16, height = 16 }
              filename = "animated.gif"
            }
            """;

        var outputDirectory = CreateTemporaryDirectory();
        try
        {
            using var result = new GfxEngine().RenderSource(source, new RenderOptions
            {
                OutputDirectory = outputDirectory,
                SeedOverride = 123
            });

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            var artifact = result.Artifacts.Should().ContainSingle().Subject;
            Path.GetFileName(artifact.FilePath).Should().Be("animated.gif");
            File.Exists(artifact.FilePath).Should().BeTrue();

            using var stream = new MemoryStream(artifact.EncodedBytes);
            using var codec = SKCodec.Create(stream);
            codec.Should().NotBeNull();
            codec!.Info.Width.Should().Be(16);
            codec.Info.Height.Should().Be(16);
            codec.FrameCount.Should().Be(15);

            var frameInfos = codec.FrameInfo;
            frameInfos.Should().HaveCount(15);
            foreach (var info in frameInfos)
            {
                info.Duration.Should().Be(70);
            }

            // Decode first and last frames to verify animated colors are preserved in the GIF stream
            var frameBitmap = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul));
            codec.GetPixels(frameBitmap.Info, frameBitmap.GetPixels(), new SKCodecOptions(0)).Should().Be(SKCodecResult.Success);
            var firstPixel = frameBitmap.GetPixel(8, 8);
            firstPixel.Red.Should().Be(0);
            firstPixel.Green.Should().Be(40);
            firstPixel.Blue.Should().Be(120);

            codec.GetPixels(frameBitmap.Info, frameBitmap.GetPixels(), new SKCodecOptions(14)).Should().Be(SKCodecResult.Success);
            var lastPixel = frameBitmap.GetPixel(8, 8);
            lastPixel.Red.Should().Be((byte)Math.Round((14.0 / 15.0) * 240, MidpointRounding.AwayFromZero));
            lastPixel.Green.Should().Be(40);
            lastPixel.Blue.Should().Be(120);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public void AnimatedNumberGfx_KeepsChosenRandomDigitConstantAcrossFramesWhileAnimatingPropertiesAndSupportingRandomFrame()
    {
        const string source = """
            layers = {
              layer0 = {
                type = fill
                color = NAMED_COLORS.BLACK
              }
              layer1 = {
                type = text
                text = "${floor(random(0, 10))}"
                font_size = lerp(24, 48, frame.progress)
                color = NAMED_COLORS.WHITE
                jitter = random_frame(0, 100)
              }
              layer2 = {
                type = fractal_clouds
                color = color_ramp(NAMED_COLORS.WHITE, NAMED_COLORS.BLACK)
                phase = frame.progress
                loop_seamless = true
                mix = multiply
              }
            }
            output = {
              mode = animation_gif
              frames = 8
              fps = 15
              dimensions = { width = 64, height = 64 }
              filename = "animated_number.gif"
            }
            """;

        var seedProvider = new DeterministicSeedProvider(999UL);
        var runSeed = seedProvider.DeriveSeed(0, 0, "__run__");
        var parseResult = GfxStudio.Core.Parsing.GfxParser.Parse(source);
        parseResult.HasErrors.Should().BeFalse();

        var digits = new List<string>();
        var fontSizes = new List<double>();
        var jitters = new List<double>();

        for (var frameIndex = 0; frameIndex < 8; frameIndex++)
        {
            var context = new EvaluationContext(
                parseResult.Document,
                new RunContext(0, 1, runSeed),
                new FrameContext(frameIndex, 8, 15.0),
                seedProvider.CreateLayerRandom(0, "__root__"),
                frameRandom: seedProvider.CreateRandom(0, frameIndex, "__root__:frame"));

            var evaluator = new GfxEvaluator(context, seedProvider);
            var textVal = evaluator.EvaluatePath("layers.layer1.text").Value as GfxString;
            var sizeVal = evaluator.EvaluatePath("layers.layer1.font_size").Value as GfxNumber;
            var jitterVal = evaluator.EvaluatePath("layers.layer1.jitter").Value as GfxNumber;

            textVal.Should().NotBeNull();
            sizeVal.Should().NotBeNull();
            jitterVal.Should().NotBeNull();

            digits.Add(textVal!.Value);
            fontSizes.Add(sizeVal!.Value);
            jitters.Add(jitterVal!.Value);
        }

        // Acceptance Criterion 3: randomly chosen digit stays constant across all frames of the same run
        digits.Distinct().Should().ContainSingle();

        // Animated property varies smoothly with frame.progress
        fontSizes.Should().BeInAscendingOrder();
        fontSizes.First().Should().BeApproximately(24.0, 1e-6);
        fontSizes.Last().Should().BeApproximately(24.0 + (24.0 * 7.0 / 8.0), 1e-6);

        // random_frame() varies across frames
        jitters.Distinct().Should().HaveCount(8);

        // Full end-to-end GIF render also succeeds and decodes all 8 frames
        var outputDirectory = CreateTemporaryDirectory();
        try
        {
            using var renderResult = new GfxEngine().RenderSource(source, new RenderOptions
            {
                OutputDirectory = outputDirectory,
                SeedOverride = 999UL
            });

            renderResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, renderResult.Diagnostics));
            using var stream = new MemoryStream(renderResult.Artifacts.Single().EncodedBytes);
            using var codec = SKCodec.Create(stream);
            codec.Should().NotBeNull();
            codec!.FrameCount.Should().Be(8);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "gfxstudio-animation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
