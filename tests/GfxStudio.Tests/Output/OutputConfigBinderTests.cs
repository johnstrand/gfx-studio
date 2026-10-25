using FluentAssertions;
using GfxStudio.Core.Output;
using GfxStudio.Core.Parsing;

namespace GfxStudio.Tests.Output;

public sealed class OutputConfigBinderTests
{
    [Fact]
    public void Bind_AppliesFrameRateOverride()
    {
        const string source = """
            layers = {
              layer0 = { type = fill, color = NAMED_COLORS.WHITE }
            }
            output = {
              dimensions = { width = 2, height = 2 }
              filename = "fps.gif"
              mode = animation_gif
              frames = 2
              fps = 12
            }
            """;

        var document = GfxParser.Parse(source).Document!;
        var bindResult = new OutputConfigBinder().Bind(document, options: new RenderOptions { FpsOverride = 24.0 });

        bindResult.HasErrors.Should().BeFalse();
        bindResult.Config!.Fps.Should().Be(24.0);
    }

    [Theory]
    [InlineData("layers = { layer0 = { type = fill } }", "OUT001")]
    [InlineData("layers = { layer0 = { type = fill } }\noutput = { dimensions = { width = 0, height = 10 }, filename = \"out.png\" }", "OUT001")]
    public void RenderSource_MissingOrInvalidDimensions_ReturnsClearDiagnostic(string source, string expectedCode)
    {
        using var result = new GfxEngine().RenderSource(source);

        result.HasErrors.Should().BeTrue();
        result.Artifacts.Should().BeEmpty();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == expectedCode);
    }

    [Fact]
    public void RenderSource_SupportsPngJpegWebpAndBmpEncoders()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "gfxstudio-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            foreach (var extension in new[] { ".png", ".jpg", ".webp", ".bmp" })
            {
                var source = $$"""
                    layers = {
                      layer0 = {
                        type = fill
                        color = NAMED_COLORS.RED
                      }
                    }
                    output = {
                      dimensions = { width = 3, height = 2 }
                      filename = "encoded{{extension}}"
                    }
                    """;

                using var result = new GfxEngine().RenderSource(source, new RenderOptions { OutputDirectory = outputDirectory });

                result.HasErrors.Should().BeFalse($"{extension}: {string.Join(Environment.NewLine, result.Diagnostics)}");
                var artifact = result.Artifacts.Single();
                artifact.EncodedBytes.Should().NotBeEmpty();
                File.ReadAllBytes(artifact.FilePath).Should().Equal(artifact.EncodedBytes);
            }
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public void RenderSource_UsesDocumentSeedUnlessOverridden()
    {
        const string source = """
            seed = 42
            layers = {
              layer0 = {
                type = fill
                color = NAMED_COLORS.WHITE
              }
            }
            output = {
              dimensions = { width = 2, height = 2 }
              filename = "seed.png"
            }
            """;
        var outputDirectory = Path.Combine(Path.GetTempPath(), "gfxstudio-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            using var fromDocument = new GfxEngine().RenderSource(source, new RenderOptions { OutputDirectory = outputDirectory });
            using var fromOverride = new GfxEngine().RenderSource(source, new RenderOptions { OutputDirectory = outputDirectory, SeedOverride = 99 });

            fromDocument.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, fromDocument.Diagnostics));
            fromOverride.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, fromOverride.Diagnostics));
            fromDocument.MasterSeed.Should().Be(42);
            fromOverride.MasterSeed.Should().Be(99);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public void RenderSource_UsesCurrentTimestampWhenNoSeedIsSpecifiedAndPreservesExplicitZero()
    {
        const string sourceWithoutSeed = """
            layers = { layer0 = { type = fill, color = NAMED_COLORS.WHITE } }
            output = { dimensions = { width = 2, height = 2 }, filename = "timestamp.png" }
            """;
        const string sourceWithZeroSeed = """
            seed = 0
            layers = { layer0 = { type = fill, color = NAMED_COLORS.WHITE } }
            output = { dimensions = { width = 2, height = 2 }, filename = "zero.png" }
            """;

        var outputDirectory = Path.Combine(Path.GetTempPath(), "gfxstudio-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            var before = unchecked((ulong)DateTime.UtcNow.Ticks);
            using var timestampResult = new GfxEngine().RenderSource(
                sourceWithoutSeed,
                new RenderOptions { OutputDirectory = outputDirectory });
            var after = unchecked((ulong)DateTime.UtcNow.Ticks);
            using var zeroResult = new GfxEngine().RenderSource(
                sourceWithZeroSeed,
                new RenderOptions { OutputDirectory = outputDirectory });

            timestampResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, timestampResult.Diagnostics));
            timestampResult.MasterSeed.Should().BeInRange(before, after);
            zeroResult.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, zeroResult.Diagnostics));
            zeroResult.MasterSeed.Should().Be(0);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }
}
