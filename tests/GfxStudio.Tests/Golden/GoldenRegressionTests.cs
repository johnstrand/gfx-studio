using FluentAssertions;
using GfxStudio.Core.Output;
using SkiaSharp;

namespace GfxStudio.Tests.Golden;

public sealed class GoldenRegressionTests
{
    [Fact]
    public void Sample_NumberGfx_MatchesBaseline()
    {
        var samplePath = FindRepositoryFile(Path.Combine("samples", "number.gfx"));
        var tempDir = CreateTemporaryDirectory();
        try
        {
            using var result = new GfxEngine().RenderFile(samplePath, new RenderOptions
            {
                SeedOverride = 42,
                OutputDirectory = tempDir
            });

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().ContainSingle();
            var artifact = result.Artifacts[0];
            GoldenImageAssert.MatchesBaseline(artifact.Bitmap, "number_sample");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Sample_WornNumbersSeries_MatchesBaseline()
    {
        var samplePath = FindRepositoryFile(Path.Combine("samples", "worn_numbers_series.gfx"));
        var tempDir = CreateTemporaryDirectory();
        try
        {
            using var result = new GfxEngine().RenderFile(samplePath, new RenderOptions
            {
                SeedOverride = 100,
                OutputDirectory = tempDir
            });

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().HaveCount(20);

            // Compare first, middle, and last series items against golden baselines
            GoldenImageAssert.MatchesBaseline(result.Artifacts[0].Bitmap, "worn_numbers_series_00");
            GoldenImageAssert.MatchesBaseline(result.Artifacts[9].Bitmap, "worn_numbers_series_09");
            GoldenImageAssert.MatchesBaseline(result.Artifacts[19].Bitmap, "worn_numbers_series_19");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Sample_WornNumbersAtlas_MatchesBaseline()
    {
        var samplePath = FindRepositoryFile(Path.Combine("samples", "worn_numbers_atlas.gfx"));
        var tempDir = CreateTemporaryDirectory();
        try
        {
            using var result = new GfxEngine().RenderFile(samplePath, new RenderOptions
            {
                SeedOverride = 200,
                OutputDirectory = tempDir
            });

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().ContainSingle();
            var pngArtifact = result.Artifacts[0];

            GoldenImageAssert.MatchesBaseline(pngArtifact.Bitmap, "worn_numbers_atlas");
            result.MetadataFilePath.Should().NotBeNull();
            File.Exists(result.MetadataFilePath!).Should().BeTrue();
            File.ReadAllText(result.MetadataFilePath!).Should().Contain("\"tile_0019\"");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Sample_EvolvingCloudsFrames_MatchesBaseline()
    {
        var samplePath = FindRepositoryFile(Path.Combine("samples", "evolving_clouds_frames.gfx"));
        var tempDir = CreateTemporaryDirectory();
        try
        {
            using var result = new GfxEngine().RenderFile(samplePath, new RenderOptions
            {
                SeedOverride = 300,
                OutputDirectory = tempDir
            });

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().HaveCount(10);

            // Compare key frames
            GoldenImageAssert.MatchesBaseline(result.Artifacts[0].Bitmap, "evolving_clouds_frame_00");
            GoldenImageAssert.MatchesBaseline(result.Artifacts[4].Bitmap, "evolving_clouds_frame_04");
            GoldenImageAssert.MatchesBaseline(result.Artifacts[9].Bitmap, "evolving_clouds_frame_09");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Sample_LoopingPortal_MatchesBaseline()
    {
        var samplePath = FindRepositoryFile(Path.Combine("samples", "looping_portal.gfx"));
        var tempDir = CreateTemporaryDirectory();
        try
        {
            using var result = new GfxEngine().RenderFile(samplePath, new RenderOptions
            {
                SeedOverride = 400,
                OutputDirectory = tempDir
            });

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            var gifArtifact = result.Artifacts.Single(a => a.FilePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase));
            GoldenImageAssert.MatchesBaselineGif(gifArtifact.EncodedBytes, "looping_portal");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Sample_ScriptedCards_MatchesBaseline()
    {
        var samplePath = FindRepositoryFile(Path.Combine("samples", "scripted_cards.gfx"));
        var tempDir = CreateTemporaryDirectory();
        try
        {
            using var result = new GfxEngine().RenderFile(samplePath, new RenderOptions
            {
                SeedOverride = 500,
                OutputDirectory = tempDir
            });

            result.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, result.Diagnostics));
            result.Artifacts.Should().ContainSingle();
            var pngArtifact = result.Artifacts[0];

            GoldenImageAssert.MatchesBaseline(pngArtifact.Bitmap, "scripted_cards_atlas");
            result.MetadataFilePath.Should().NotBeNull();
            File.Exists(result.MetadataFilePath!).Should().BeTrue();
            File.ReadAllText(result.MetadataFilePath!).Should().Contain("\"tile_0011\"");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find repository test fixture '{relativePath}'.");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "gfxstudio-golden-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }
}
