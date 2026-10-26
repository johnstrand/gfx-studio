using FluentAssertions;
using GfxStudio.Cli;
using SkiaSharp;

namespace GfxStudio.Tests.Cli;

[Collection("CLI tests")]
public sealed class VariablesCliTests
{
    private const string VariableSource = """
        variables = {
          base_size = 16
          bg_color = hex("#00ff00")
        }

        layers = {
          bg = {
            type = fill
            color = bg_color
          }
        }

        output = {
          dimensions = { width = base_size, height = variables.base_size }
          filename = "test.png"
          seed = 1
        }
        """;

    [Fact]
    public async Task Render_WithVarOverride_OverridesBothBareAndQualifiedVariables()
    {
        using var fixture = new CliFixture(VariableSource);
        var outputDirectory = Path.Combine(fixture.DirectoryPath, "rendered");

        var result = await fixture.InvokeAsync(
            "render", fixture.SourcePath, "--output-dir", outputDirectory,
            "--var", "base_size=32", "--var", "bg_color=hex(\"#ff0000\")");

        result.ExitCode.Should().Be(0);
        var outputPath = Path.Combine(outputDirectory, "test.png");
        File.Exists(outputPath).Should().BeTrue();

        using var bitmap = SKBitmap.Decode(outputPath);
        bitmap.Width.Should().Be(32);
        bitmap.Height.Should().Be(32);
        var pixel = bitmap.GetPixel(0, 0);
        pixel.Red.Should().Be(255);
        pixel.Green.Should().Be(0);
        pixel.Blue.Should().Be(0);
    }

    [Fact]
    public async Task Render_WithShortFlagV_OverridesVariables()
    {
        using var fixture = new CliFixture(VariableSource);
        var outputDirectory = Path.Combine(fixture.DirectoryPath, "rendered");

        var result = await fixture.InvokeAsync(
            "render", fixture.SourcePath, "--output-dir", outputDirectory,
            "-v", "base_size=8");

        result.ExitCode.Should().Be(0);
        var outputPath = Path.Combine(outputDirectory, "test.png");
        File.Exists(outputPath).Should().BeTrue();

        using var bitmap = SKBitmap.Decode(outputPath);
        bitmap.Width.Should().Be(8);
        bitmap.Height.Should().Be(8);
    }

    [Fact]
    public async Task Validate_WithVarOverride_Succeeds()
    {
        using var fixture = new CliFixture(VariableSource);

        var result = await fixture.InvokeAsync("validate", fixture.SourcePath, "--var", "base_size=64");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("Valid:");
    }

    [Fact]
    public async Task Inspect_WithVarOverride_ReflectsOverriddenValues()
    {
        using var fixture = new CliFixture(VariableSource);

        var result = await fixture.InvokeAsync("inspect", fixture.SourcePath, "--json", "--var", "base_size=48");

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("\"width\": 48").And.Contain("\"height\": 48");
    }

    [Fact]
    public async Task Render_WithInvalidVarSyntax_ExitsWithCode2()
    {
        using var fixture = new CliFixture(VariableSource);

        var result = await fixture.InvokeAsync("render", fixture.SourcePath, "--var", "no_equals_sign");

        result.ExitCode.Should().Be(2);
        result.StandardError.Should().Contain("Invalid variable override 'no_equals_sign'. Expected format is 'key=value'.");
    }

    [Fact]
    public async Task Validate_TypoInVariable_SuggestsFromNameInVariablesBlock()
    {
        const string typoSource = """
            variables = {
              highlight_color = hex("#ffffff")
            }

            layers = {
              bg = {
                type = fill
                color = variables.hilight_color
              }
            }

            output = {
              dimensions = { width = 16, height = 16 }
              filename = "test.png"
            }
            """;

        using var fixture = new CliFixture(typoSource);

        var result = await fixture.InvokeAsync("validate", fixture.SourcePath);

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain("hilight_color").And.Contain("Did you mean 'highlight_color'?");
    }

    private sealed class CliFixture : IDisposable
    {
        public string DirectoryPath { get; }
        public string SourcePath { get; }

        public CliFixture(string source)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "gfx-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            SourcePath = Path.Combine(DirectoryPath, "input.gfx");
            File.WriteAllText(SourcePath, source);
        }

        public async Task<CliResult> InvokeAsync(params string[] args)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var outWriter = new StringWriter();
            using var errorWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errorWriter);

            try
            {
                var exitCode = await GfxCli.InvokeAsync(args).ConfigureAwait(false);
                return new CliResult(exitCode, outWriter.ToString(), errorWriter.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup in tests
            }
        }
    }

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
}
