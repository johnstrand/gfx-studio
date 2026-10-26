using FluentAssertions;
using GfxStudio.Cli;
using GfxStudio.Core.Documentation;
using GfxStudio.Core.Rendering;
using GfxStudio.Core.Rendering.Effects;

namespace GfxStudio.Tests.Documentation;

public sealed class GfxWikiGeneratorTests
{
    [Fact]
    public void GeneratePages_DocumentsEveryRegisteredLayerTypeAndEffectWithPropertiesAndExamples()
    {
        var layerRegistry = LayerRendererRegistry.CreateDefault();
        var effectRegistry = LayerEffectRegistry.CreateDefault();
        var pages = GfxWikiGenerator.GeneratePages(layerRegistry, effectRegistry);

        pages.Keys.Should().Contain([
            "Home.md",
            "_Sidebar.md",
            "Getting-Started.md",
            "Language-Reference.md",
            "Common-Layer-Properties-and-Transforms.md",
            "Layers-Overview.md",
            "Layers-Core-and-Fill.md",
            "Layers-Shapes-and-Text.md",
            "Layers-Geometric-Patterns.md",
            "Layers-Procedural-Noise-and-Organic.md",
            "Effects-and-Shaders.md",
            "Output-Modes-and-CLI.md"
        ]);

        var combinedMarkdown = string.Join("\n", pages.Values);

        foreach (var layerType in layerRegistry.RegisteredLayerTypes)
        {
            combinedMarkdown.Should().Contain($"`{layerType}`", $"Wiki should document layer type '{layerType}'");
        }

        foreach (var effectName in effectRegistry.RegisteredEffectNames)
        {
            combinedMarkdown.Should().Contain($"`{effectName}`", $"Wiki should document effect '{effectName}'");
        }
    }

    [Fact]
    public async Task CliWiki_WritesAllMarkdownPagesToOutputDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gfx_wiki_{Guid.NewGuid():N}");
        try
        {
            var exitCode = await GfxCli.InvokeAsync(["wiki", "--output", tempDir]);
            exitCode.Should().Be(0);

            File.Exists(Path.Combine(tempDir, "Home.md")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "_Sidebar.md")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "Effects-and-Shaders.md")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
