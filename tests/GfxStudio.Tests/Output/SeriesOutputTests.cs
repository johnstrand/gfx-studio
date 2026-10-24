using FluentAssertions;
using GfxStudio.Core.Output;

namespace GfxStudio.Tests.Output;

public sealed class SeriesOutputTests
{
    [Fact]
    public void RunCountOverride_RendersDistinctNumberGfxFilesWithParallelDeterminism()
    {
        var sourcePath = FindRepositoryFile("number.gfx");
        var serialDirectory = CreateTemporaryDirectory();
        var parallelDirectory = CreateTemporaryDirectory();
        var isolatedDirectory = CreateTemporaryDirectory();
        try
        {
            using var serial = new GfxEngine().RenderFile(sourcePath, new RenderOptions
            {
                SeedOverride = 1337,
                RunCountOverride = 10,
                MaxDegreeOfParallelism = 1,
                OutputDirectory = serialDirectory
            });
            using var parallel = new GfxEngine().RenderFile(sourcePath, new RenderOptions
            {
                SeedOverride = 1337,
                RunCountOverride = 10,
                MaxDegreeOfParallelism = 4,
                OutputDirectory = parallelDirectory
            });

            serial.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, serial.Diagnostics));
            parallel.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, parallel.Diagnostics));
            serial.Artifacts.Should().HaveCount(10);
            parallel.Artifacts.Should().HaveCount(10);

            for (var index = 0; index < 10; index++)
            {
                Path.GetFileName(serial.Artifacts[index].FilePath).Should().Be($"worn_number_{index:0000}.png");
                serial.Artifacts[index].Bitmap.GetPixelSpan().ToArray()
                    .Should().Equal(parallel.Artifacts[index].Bitmap.GetPixelSpan().ToArray());
                serial.Artifacts[index].EncodedBytes.Should().Equal(parallel.Artifacts[index].EncodedBytes);
            }

            serial.Artifacts.Select(artifact => Convert.ToHexString(artifact.EncodedBytes)).Distinct().Should().HaveCountGreaterThan(1);

            using var isolated = new GfxEngine().RenderFile(sourcePath, new RenderOptions
            {
                SeedOverride = 1337,
                RunCountOverride = 1,
                StartIndexOverride = 7,
                OutputDirectory = isolatedDirectory
            });
            isolated.HasErrors.Should().BeFalse(string.Join(Environment.NewLine, isolated.Diagnostics));
            Path.GetFileName(isolated.Artifacts.Single().FilePath).Should().Be("worn_number_0007.png");
            isolated.Artifacts.Single().EncodedBytes.Should().Equal(serial.Artifacts[7].EncodedBytes);
        }
        finally
        {
            Directory.Delete(serialDirectory, recursive: true);
            Directory.Delete(parallelDirectory, recursive: true);
            Directory.Delete(isolatedDirectory, recursive: true);
        }
    }

    [Fact]
    public void StaticFilenameWithMultipleRuns_ReturnsCollisionDiagnostic()
    {
        const string source = """
            layers = {
              layer0 = {
                type = fill
                color = NAMED_COLORS.WHITE
              }
            }
            output = {
              mode = series
              count = 5
              dimensions = { width = 2, height = 2 }
              filename = "same.png"
            }
            """;
        var outputDirectory = CreateTemporaryDirectory();
        try
        {
            using var result = new GfxEngine().RenderSource(source, new RenderOptions { OutputDirectory = outputDirectory });

            result.HasErrors.Should().BeTrue();
            result.Artifacts.Should().BeEmpty();
            result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "OUT016");
            Directory.GetFiles(outputDirectory).Should().BeEmpty();
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
