using FluentAssertions;
using GfxStudio.Core.Output;

namespace GfxStudio.Tests.Output;

public sealed class OutputConfigBinderTests
{
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
}
