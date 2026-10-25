using FluentAssertions;
using GfxStudio.Cli;
using SkiaSharp;

namespace GfxStudio.Tests.Cli;

[Collection("CLI tests")]
public sealed class CliCommandTests
{
    private const string ValidSource = """
        layers = {
          solid = {
            type = fill
            color = rgb(10, 20, 30)
          }
        }
        output = {
          dimensions = { width = 4, height = 4 }
          filename = "test_${run.number:0000}.png"
          seed = 123
        }
        """;

    [Fact]
    public async Task Validate_ReturnsSuccessWithoutWritingOutput()
    {
        using var fixture = new CliFixture(ValidSource);

        var result = await fixture.InvokeAsync("validate", fixture.SourcePath);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("Valid:");
        Directory.GetFiles(fixture.DirectoryPath, "*.png").Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_PrintsSourceSnippetForInvalidExpression()
    {
        using var fixture = new CliFixture(ValidSource.Replace("rgb(10, 20, 30)", "NAMED_COLOURS.WHITE", StringComparison.Ordinal));

        var result = await fixture.InvokeAsync("validate", fixture.SourcePath);

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain("NAMED_COLOURS").And.Contain("Did you mean 'NAMED_COLORS'?").And.Contain("--> ").And.Contain("^");
    }

    [Fact]
    public async Task Validate_ReportsLayerPropertyErrorsWithoutWritingOutput()
    {
        var source = ValidSource.Replace("color = rgb(10, 20, 30)", "color = 123", StringComparison.Ordinal);
        using var fixture = new CliFixture(source);

        var result = await fixture.InvokeAsync("validate", fixture.SourcePath);

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain("RND012").And.Contain("must be a color");
        Directory.GetFiles(fixture.DirectoryPath, "*.png").Should().BeEmpty();
    }

    [Fact]
    public async Task InspectJson_PrintsResolvedOutputAndOrderedLayers()
    {
        using var fixture = new CliFixture(ValidSource);

        var result = await fixture.InvokeAsync("inspect", fixture.SourcePath, "--json", "--seed", "42");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("\"masterSeed\": 42").And.Contain("\"filename\": \"test_0000.png\"").And.Contain("\"id\": \"solid\"");
    }

    [Fact]
    public async Task Render_AppliesOverridesWritesFileAndPrintsSeed()
    {
        using var fixture = new CliFixture(ValidSource);
        var outputDirectory = Path.Combine(fixture.DirectoryPath, "rendered");

        var result = await fixture.InvokeAsync(
            "render", fixture.SourcePath, "--output-dir", outputDirectory, "--seed", "42", "--mode", "series", "--runs", "2");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("Master seed: 42").And.Contain("Elapsed:");
        Directory.GetFiles(outputDirectory, "*.png").Should().HaveCount(2);
    }

    [Fact]
    public async Task Render_ImageMapWithNineRunsCreatesThreeByThreeAtlas()
    {
        using var fixture = new CliFixture(ValidSource);
        var outputDirectory = Path.Combine(fixture.DirectoryPath, "atlas");

        var result = await fixture.InvokeAsync(
            "render", fixture.SourcePath, "--output-dir", outputDirectory, "--mode", "image_map", "--runs", "9");

        result.ExitCode.Should().Be(0);
        using var bitmap = SKBitmap.Decode(Path.Combine(outputDirectory, "test_0000.png"));
        bitmap.Width.Should().Be(12);
        bitmap.Height.Should().Be(12);
    }

    private sealed class CliFixture : IDisposable
    {
        public CliFixture(string source)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"gfx-cli-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            SourcePath = Path.Combine(DirectoryPath, "fixture.gfx");
            File.WriteAllText(SourcePath, source);
        }

        public string DirectoryPath { get; }

        public string SourcePath { get; }

        public async Task<InvocationResult> InvokeAsync(params string[] args)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var standardOutput = new StringWriter();
            using var standardError = new StringWriter();
            try
            {
                Console.SetOut(standardOutput);
                Console.SetError(standardError);
                var exitCode = await GfxCli.InvokeAsync(args);
                return new InvocationResult(exitCode, standardOutput.ToString(), standardError.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(DirectoryPath))
            {
                File.Delete(file);
            }

            foreach (var directory in Directory.EnumerateDirectories(DirectoryPath))
            {
                Directory.Delete(directory, recursive: true);
            }

            Directory.Delete(DirectoryPath);
        }
    }

    private sealed record InvocationResult(int ExitCode, string StandardOutput, string StandardError);
}

[CollectionDefinition("CLI tests", DisableParallelization = true)]
public sealed class CliTestsCollection;
