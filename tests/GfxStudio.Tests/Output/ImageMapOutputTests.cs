using System.Text.Json;
using FluentAssertions;
using GfxStudio.Core.Output;
using SkiaSharp;

namespace GfxStudio.Tests.Output;

public sealed class ImageMapOutputTests
{
    [Fact]
    public void ImageMap_RendersExpectedGridAndWritesMetadata()
    {
        const string source = """
            layers = {
              layer0 = {
                type = fill
                color = rgb(run.number * 50, 0, 0)
              }
            }
            output = {
              mode = image_map
              count = 4
              columns = 2
              padding = 2
              spacing = 4
              background = NAMED_COLORS.TRANSPARENT
              dimensions = { width = 64, height = 64 }
              filename = "atlas.png"
              metadata_filename = "atlas.json"
            }
            """;
        var outputDirectory = Path.Combine(Path.GetTempPath(), "gfxstudio-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            using var result = new GfxEngine().RenderSource(source, new RenderOptions
            {
                SeedOverride = 123,
                OutputDirectory = outputDirectory
            });

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().ContainSingle();
            var atlas = result.Artifacts.Single();
            atlas.Dimensions.Width.Should().Be(136);
            atlas.Dimensions.Height.Should().Be(136);
            atlas.Bitmap.Width.Should().Be(136);
            atlas.Bitmap.Height.Should().Be(136);
            atlas.Bitmap.GetPixel(2, 2).Red.Should().Be(0);
            atlas.Bitmap.GetPixel(70, 2).Red.Should().Be(50);
            atlas.Bitmap.GetPixel(2, 70).Red.Should().Be(100);
            atlas.Bitmap.GetPixel(70, 70).Red.Should().Be(150);
            atlas.Bitmap.GetPixel(68, 2).Alpha.Should().Be(0);
            result.MetadataFilePath.Should().Be(Path.Combine(outputDirectory, "atlas.json"));

            using var metadata = JsonDocument.Parse(File.ReadAllText(result.MetadataFilePath!));
            var root = metadata.RootElement;
            root.GetProperty("image").GetString().Should().Be("atlas.png");
            root.GetProperty("atlasDimensions").GetProperty("width").GetInt32().Should().Be(136);
            root.GetProperty("atlasDimensions").GetProperty("height").GetInt32().Should().Be(136);
            root.GetProperty("tileDimensions").GetProperty("width").GetInt32().Should().Be(64);
            var sprites = root.GetProperty("sprites").EnumerateArray().ToArray();
            sprites.Should().HaveCount(4);
            sprites[0].GetProperty("name").GetString().Should().Be("tile_0000");
            sprites[0].GetProperty("x").GetInt32().Should().Be(2);
            sprites[0].GetProperty("y").GetInt32().Should().Be(2);
            sprites[1].GetProperty("x").GetInt32().Should().Be(70);
            sprites[1].GetProperty("y").GetInt32().Should().Be(2);
            sprites[2].GetProperty("x").GetInt32().Should().Be(2);
            sprites[2].GetProperty("y").GetInt32().Should().Be(70);
            sprites[3].GetProperty("x").GetInt32().Should().Be(70);
            sprites[3].GetProperty("y").GetInt32().Should().Be(70);
            sprites[0].GetProperty("seed").GetUInt64().Should().NotBe(0);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public void ImageMap_AcceptsFrameCountAndEvaluatesFrameContext()
    {
        const string source = """
            layers = {
              layer0 = {
                type = fill
                color = rgb(frame.number * 100, 0, 0)
              }
            }
            output = {
              mode = image_map
              frames = 3
              columns = 2
              dimensions = { width = 2, height = 2 }
              filename = "frames.png"
            }
            """;
        var outputDirectory = Path.Combine(Path.GetTempPath(), "gfxstudio-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            using var result = new GfxEngine().RenderSource(source, new RenderOptions { OutputDirectory = outputDirectory });

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            var atlas = result.Artifacts.Single().Bitmap;
            atlas.Width.Should().Be(4);
            atlas.Height.Should().Be(4);
            atlas.GetPixel(0, 0).Red.Should().Be(0);
            atlas.GetPixel(2, 0).Red.Should().Be(100);
            atlas.GetPixel(0, 2).Red.Should().Be(200);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }
}
