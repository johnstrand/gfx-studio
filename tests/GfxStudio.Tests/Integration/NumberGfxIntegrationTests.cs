using FluentAssertions;
using GfxStudio.Core.Output;
using SkiaSharp;

namespace GfxStudio.Tests.Integration;

public sealed class NumberGfxIntegrationTests
{
    [Fact]
    public void RenderFile_NumberGfx_WritesDeterministicCenteredNumberImage()
    {
        var sourcePath = FindRepositoryFile("number.gfx");
        var outputDirectory = CreateTemporaryDirectory();
        try
        {
            using var first = new GfxEngine().RenderFile(sourcePath, new RenderOptions
            {
                SeedOverride = 42,
                OutputDirectory = outputDirectory
            });
            using var second = new GfxEngine().RenderFile(sourcePath, new RenderOptions
            {
                SeedOverride = 42,
                OutputDirectory = outputDirectory
            });

            first.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, first.Diagnostics));
            first.Artifacts.Should().ContainSingle();
            var artifact = first.Artifacts[0];
            Path.GetFileName(artifact.FilePath).Should().Be("worn_number_0000.png");
            artifact.Dimensions.Should().Be(new GfxStudio.Core.Primitives.Dimensions(64, 64));
            artifact.Bitmap.Width.Should().Be(64);
            artifact.Bitmap.Height.Should().Be(64);
            File.Exists(artifact.FilePath).Should().BeTrue();
            artifact.EncodedBytes.Should().Equal(File.ReadAllBytes(artifact.FilePath));
            artifact.EncodedBytes.Should().Equal(second.Artifacts[0].EncodedBytes);
            artifact.Bitmap.GetPixelSpan().ToArray().Should().Equal(second.Artifacts[0].Bitmap.GetPixelSpan().ToArray());

            var pixels = Enumerable.Range(0, artifact.Bitmap.Height)
                .SelectMany(y => Enumerable.Range(0, artifact.Bitmap.Width).Select(x => artifact.Bitmap.GetPixel(x, y)))
                .ToArray();
            pixels.Should().Contain(pixel => pixel.Red == 0 && pixel.Green == pixel.Blue && pixel.Green > 0);
            pixels.Should().Contain(pixel => pixel.Red == 0 && pixel.Green == 0 && pixel.Blue == 0);
            pixels.Should().Contain(pixel => pixel.Red == 0 && pixel.Green > 0 && pixel.Green < 255 && pixel.Blue > 0 && pixel.Blue < 255);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static string FindRepositoryFile(string filename)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, filename);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find repository test fixture '{filename}'.");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "gfxstudio-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
