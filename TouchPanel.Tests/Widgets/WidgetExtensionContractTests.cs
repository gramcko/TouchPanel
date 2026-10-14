using System.IO;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Widgets;

/// <summary>
/// RF-046 extension-contract proof (M3-RF-461/468/469): the three new widget types are ordinary
/// registry/template/config additions that required no change to the widget host, page model,
/// template selector, or edit-mode service. The host guard scans those sources for any reference to
/// the new types, so a future regression that special-cases a type in the host is caught.
/// </summary>
public sealed class WidgetExtensionContractTests
{
    private static readonly string[] NewTypeTokens =
    [
        "ClockWidgetConfig",
        "ImageButtonWidgetConfig",
        "BarWidgetConfig",
        "ClockWidgetViewModel",
        "ImageButtonWidgetViewModel",
        "BarWidgetViewModel",
        "WidgetTypes.Clock",
        "WidgetTypes.ImageButton",
        "WidgetTypes.Bar",
    ];

    [Theory]
    [InlineData("ViewModels/PageViewModel.cs")]
    [InlineData("Widgets/WidgetTemplateSelector.cs")]
    [InlineData("ViewModels/EditModeService.cs")]
    public void HostSources_DoNotSpecialCaseTheNewWidgetTypes(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "TouchPanel", relativePath));

        foreach (var token in NewTypeTokens)
        {
            Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Registry_ExposesAllThreeNewTypesWithDistinctTemplates()
    {
        var registry = WidgetRegistry.CreateDefault();

        Assert.True(registry.TryGet(WidgetTypes.Clock, out var clock));
        Assert.True(registry.TryGet(WidgetTypes.ImageButton, out var imageButton));
        Assert.True(registry.TryGet(WidgetTypes.Bar, out var bar));

        Assert.Equal(3, new[] { clock.TemplateKey, imageButton.TemplateKey, bar.TemplateKey }.Distinct().Count());
        Assert.NotNull(clock.CreateDefaultConfig);
        Assert.NotNull(imageButton.CreateDefaultConfig);
        Assert.NotNull(bar.CreateDefaultConfig);
    }

    [Fact]
    public void Converter_RoundTripsTheNewTypesAsAdditiveDiscriminators()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            Pages =
            [
                new PageConfig
                {
                    Id = "main",
                    Widgets =
                    [
                        new ClockWidgetConfig { Id = "clock", Mode = ClockMode.DateLong },
                        new ImageButtonWidgetConfig { Id = "ib", ShowLabel = false },
                        new BarWidgetConfig { Id = "bar", Sensor = "cpu.total" },
                    ],
                },
            ],
        };

        var json = System.Text.Json.JsonSerializer.Serialize(config, ConfigJson.Options);
        var restored = System.Text.Json.JsonSerializer.Deserialize<PanelConfig>(json, ConfigJson.Options)!;

        Assert.IsType<ClockWidgetConfig>(restored.Pages[0].Widgets[0]);
        Assert.IsType<ImageButtonWidgetConfig>(restored.Pages[0].Widgets[1]);
        Assert.IsType<BarWidgetConfig>(restored.Pages[0].Widgets[2]);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TouchPanel.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
