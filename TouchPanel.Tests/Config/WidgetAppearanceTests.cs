using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Validation;

namespace TouchPanel.Tests.Config;

/// <summary>
/// Appearance helper and validation behavior (T-14, RF-043/M2-RF-435/M2-RF-436): the built-in icon
/// catalog is bounded and unique; color values are validated/normalized as <c>#RRGGBB</c> or
/// <c>#AARRGGBB</c>; font size and opacity are clamped; and the validator falls back to the theme
/// default (or clamps to the bound) with a diagnostic when hand-edited configuration is hostile.
/// </summary>
public sealed class WidgetAppearanceTests
{
    [Fact]
    public void IconCatalog_IsBoundedAndHasUniqueStableIds()
    {
        Assert.InRange(IconCatalog.BuiltIn.Count, 1, 64);
        Assert.All(IconCatalog.BuiltIn, option => Assert.False(string.IsNullOrWhiteSpace(option.Id)));
        Assert.All(IconCatalog.BuiltIn, option => Assert.True(
            option.HasGlyph != option.IsVector,
            $"Icon '{option.Id}' must be exactly one of glyph or vector."));

        var ids = IconCatalog.BuiltIn.Select(option => option.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void IconCatalog_TryGetResolvesOnlyKnownIds()
    {
        Assert.True(IconCatalog.TryGet("power", out var power));
        Assert.Equal("power", power.Id);
        Assert.False(IconCatalog.TryGet("not-a-real-icon", out _));
        Assert.False(IconCatalog.TryGet(null, out _));
        Assert.Null(IconCatalog.GlyphFor("nope"));
    }

    [Theory]
    [InlineData("#ff8800", "#FF8800")]
    [InlineData("#AABBCC", "#AABBCC")]
    [InlineData("#80ff8800", "#80FF8800")]
    [InlineData("  #123456  ", "#123456")]
    public void TryNormalizeColor_AcceptsRgbAndArgb(string input, string expected)
    {
        Assert.True(WidgetAppearance.TryNormalizeColor(input, out var normalized));
        Assert.Equal(expected, normalized);
        Assert.True(WidgetAppearance.IsValidColor(input));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#GG0000")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("123456")]
    public void TryNormalizeColor_RejectsMalformedValues(string input)
    {
        Assert.False(WidgetAppearance.TryNormalizeColor(input, out var normalized));
        Assert.Null(normalized);
        Assert.False(WidgetAppearance.IsValidColor(input));
    }

    [Fact]
    public void UnsetColor_IsValidAndNormalizesToNull()
    {
        Assert.True(WidgetAppearance.IsValidColor(null));
        Assert.True(WidgetAppearance.IsValidColor("   "));
        Assert.Null(WidgetAppearance.NormalizeColor(null));
        Assert.Null(WidgetAppearance.NormalizeColor("oops"));
    }

    [Theory]
    [InlineData(4d, 8d)]
    [InlineData(200d, 96d)]
    [InlineData(16d, 16d)]
    public void NormalizeFontSize_ClampsIntoBounds(double input, double expected)
    {
        Assert.Equal(expected, WidgetAppearance.NormalizeFontSize(input));
    }

    [Fact]
    public void NormalizeFontSize_NonFiniteDegradesToThemeDefault()
    {
        Assert.Null(WidgetAppearance.NormalizeFontSize(null));
        Assert.Null(WidgetAppearance.NormalizeFontSize(double.NaN));
        Assert.Null(WidgetAppearance.NormalizeFontSize(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(2d, 1d)]
    [InlineData(0.5d, 0.5d)]
    public void NormalizeOpacity_ClampsIntoBounds(double input, double expected)
    {
        Assert.Equal(expected, WidgetAppearance.NormalizeOpacity(input));
    }

    [Fact]
    public void NormalizeOpacity_NonFiniteDegradesToThemeDefault()
    {
        Assert.Null(WidgetAppearance.NormalizeOpacity(null));
        Assert.Null(WidgetAppearance.NormalizeOpacity(double.NaN));
    }

    [Fact]
    public void AppearanceFields_RoundTripThroughTheConfigConverter()
    {
        var original = new LabelWidgetConfig
        {
            Id = "l1",
            X = 0,
            Y = 0,
            Width = 100,
            Height = 40,
            Label = "Hi",
            Icon = "power",
            Foreground = "#112233",
            Background = "#80112233",
            Accent = "#AABBCC",
            FontSize = 14.5,
            Opacity = 0.5,
        };

        var json = JsonSerializer.Serialize<WidgetConfig>(original, ConfigJson.Options);
        var result = Assert.IsType<LabelWidgetConfig>(
            JsonSerializer.Deserialize<WidgetConfig>(json, ConfigJson.Options));

        Assert.Equal("power", result.Icon);
        Assert.Equal("#112233", result.Foreground);
        Assert.Equal("#80112233", result.Background);
        Assert.Equal("#AABBCC", result.Accent);
        Assert.Equal(14.5, result.FontSize);
        Assert.Equal(0.5, result.Opacity);
    }

    [Fact]
    public void Validator_FallsBackAndClampsHostileAppearanceWithDiagnostics()
    {
        var widget = new LabelWidgetConfig
        {
            Id = "l1",
            X = 0,
            Y = 0,
            Width = 100,
            Height = 40,
            Label = new string('a', ConfigValidator.MaxLabelLength + 10),
            Icon = "not-a-real-icon",
            Foreground = "red",
            Background = "#GG0000",
            Accent = "#12345678",
            FontSize = 500,
            Opacity = -3,
        };

        var diagnostics = ConfigValidator.Validate(ConfigWith(widget));

        Assert.Equal(ConfigValidator.MaxLabelLength, widget.Label!.Length);
        Assert.Null(widget.Icon);
        Assert.Null(widget.Foreground);
        Assert.Null(widget.Background);
        Assert.Equal("#12345678", widget.Accent);
        Assert.Equal(WidgetAppearance.MaxFontSize, widget.FontSize);
        Assert.Equal(WidgetAppearance.MinOpacity, widget.Opacity);

        Assert.Contains(diagnostics, d => d.Code == "config.widget.label");
        Assert.Contains(diagnostics, d => d.Code == "config.widget.icon");
        Assert.Contains(diagnostics, d => d.Code == "config.widget.appearance.foreground");
        Assert.Contains(diagnostics, d => d.Code == "config.widget.appearance.background");
        Assert.Contains(diagnostics, d => d.Code == "config.widget.appearance.fontSize");
        Assert.Contains(diagnostics, d => d.Code == "config.widget.appearance.opacity");
    }

    [Fact]
    public void Validator_LeavesValidAppearanceUntouchedAndNormalizesCase()
    {
        var widget = new LabelWidgetConfig
        {
            Id = "l1",
            X = 0,
            Y = 0,
            Width = 100,
            Height = 40,
            Label = "ok",
            Icon = "power",
            Foreground = "#aabbcc",
            FontSize = 20,
            Opacity = 0.75,
        };

        var diagnostics = ConfigValidator.Validate(ConfigWith(widget));

        Assert.Equal("power", widget.Icon);
        Assert.Equal("#AABBCC", widget.Foreground);
        Assert.Equal(20, widget.FontSize);
        Assert.Equal(0.75, widget.Opacity);
        Assert.DoesNotContain(diagnostics, d => d.Code.StartsWith("config.widget.appearance", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == "config.widget.icon");
    }

    private static PanelConfig ConfigWith(params WidgetConfig[] widgets) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = "main",
        Pages = [new PageConfig { Id = "main", Widgets = [.. widgets] }],
    };
}
