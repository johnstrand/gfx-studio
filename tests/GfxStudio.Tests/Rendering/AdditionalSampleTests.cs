using FluentAssertions;
using GfxStudio.Core.Output;
using GfxStudio.Core.Rendering;

namespace GfxStudio.Tests.Rendering;

public sealed class AdditionalSampleTests
{
    private static string GetSamplePath(string filename)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "samples", filename);
    }

    [Fact]
    public void RpgItemsSheetSample_RendersCleanly()
    {
        var samplePath = GetSamplePath("rpg_items_sheet.gfx");
        File.Exists(samplePath).Should().BeTrue();

        var tempDir = Path.Combine(Path.GetTempPath(), "gfx_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var engine = new GfxEngine();
            using var result = engine.RenderFile(samplePath, new RenderOptions
            {
                OutputDirectory = tempDir,
                SeedOverride = 42UL
            });

            result.Diagnostics.Should().BeEmpty(string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
            result.Artifacts.Should().HaveCount(1);
            result.MetadataFilePath.Should().NotBeNull();
            File.Exists(result.MetadataFilePath!).Should().BeTrue();
            File.Exists(result.Artifacts[0].FilePath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void PbrMaterialMapsSample_RendersCleanly()
    {
        var samplePath = GetSamplePath("pbr_material_maps.gfx");
        File.Exists(samplePath).Should().BeTrue();
        var source = File.ReadAllText(samplePath);

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source, masterSeed: 42, filePath: samplePath);

        result.Diagnostics.Should().BeEmpty(string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        result.Bitmap.Should().NotBeNull();
        result.Bitmap!.Width.Should().Be(1024);
        result.Bitmap!.Height.Should().Be(280);
    }

    [Fact]
    public void SacredGeometryMandalaSample_RendersCleanly()
    {
        var samplePath = GetSamplePath("sacred_geometry_mandala.gfx");
        File.Exists(samplePath).Should().BeTrue();
        var source = File.ReadAllText(samplePath);

        var compositor = new GfxCanvasCompositor();
        using var result = compositor.RenderSource(source, masterSeed: 42, filePath: samplePath);

        result.Diagnostics.Should().BeEmpty(string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        result.Bitmap.Should().NotBeNull();
        result.Bitmap!.Width.Should().Be(512);
        result.Bitmap!.Height.Should().Be(512);
    }
}
