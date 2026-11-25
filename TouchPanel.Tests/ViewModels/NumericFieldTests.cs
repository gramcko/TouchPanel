using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Monitoring;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Generalized numeric-field UX (user-requested addition): the pure numeric grammar (typing/paste),
/// the step/clamp/format rules for every numeric family, the documented magnitude step for the range
/// bounds, the deterministic press-and-hold repeat controller, and the editor's live-preview,
/// single-undo-group and empty/invalid-fallback behaviour for each family.
/// </summary>
public sealed class NumericFieldTests
{
    private const string PageId = "main";

    [Theory]
    [InlineData("", 0, false, true)]
    [InlineData("12", 0, false, true)]
    [InlineData("-12", 0, false, false)]
    [InlineData("-12", 0, true, true)]
    [InlineData("1.5", 0, false, false)]
    [InlineData("1.5", 1, false, true)]
    [InlineData("1,5", 1, false, true)]
    [InlineData("1.55", 1, false, false)]
    [InlineData("1.55", 2, false, true)]
    [InlineData("1.5.5", 2, false, false)]
    [InlineData(".", 0, false, false)]
    [InlineData(".", 1, false, true)]
    [InlineData("-", 0, true, true)]
    [InlineData("--1", 0, true, false)]
    [InlineData("1-", 0, true, false)]
    [InlineData("+1", 0, false, false)]
    [InlineData("abc", 0, false, false)]
    [InlineData("1 2", 0, false, false)]
    [InlineData("1#", 0, false, false)]
    public void NumericFilter_AcceptsThePerFieldKindGrammar(string text, int decimals, bool allowNegative, bool expected) =>
        Assert.Equal(expected, NumericTextFilter.IsValidText(text, decimals, allowNegative));

    [Fact]
    public void NumericFilter_PasteReplacesTheSelectionOrInsertsAtTheCaret()
    {
        Assert.True(NumericTextFilter.TryApplyEdit("12", 0, 2, "34.5", 1, false, out var replaced));
        Assert.Equal("34.5", replaced);

        Assert.True(NumericTextFilter.TryApplyEdit("12", 1, 0, ".", 1, false, out var inserted));
        Assert.Equal("1.2", inserted);

        // Invalid whole pastes are rejected without a partial insertion.
        Assert.False(NumericTextFilter.TryApplyEdit("12", 0, 2, "abc", 1, false, out _));
        Assert.False(NumericTextFilter.TryApplyEdit("12", 0, 2, "1.5", 0, false, out _));
        Assert.False(NumericTextFilter.TryApplyEdit("12", 0, 2, "1\r\n2", 1, false, out _));
        Assert.False(NumericTextFilter.TryApplyEdit("12", 0, 2, "-1", 1, false, out _));
    }

    [Fact]
    public void FontSizeRule_StepsByOneAndClampsToEightToNinetySix()
    {
        var spec = new NumericFieldSpec(1, 8, 96, 0, AllowNegative: false, AllowsEmpty: true);

        Assert.True(NumericFieldMath.TryStep(24, 1, spec, out var up));
        Assert.Equal(25, up);
        Assert.True(NumericFieldMath.TryStep(24, -1, spec, out var down));
        Assert.Equal(23, down);

        Assert.False(NumericFieldMath.TryStep(96, 1, spec, out _));
        Assert.False(NumericFieldMath.TryStep(8, -1, spec, out _));

        Assert.Equal(96, NumericFieldMath.Normalize(120, spec));
        Assert.Equal(8, NumericFieldMath.Normalize(1, spec));
        Assert.Equal("24", NumericFieldMath.Format(24, 0));
    }

    [Fact]
    public void OpacityRule_StepsByPointOneShowsOneDecimalAndClampsToZeroOne()
    {
        var spec = new NumericFieldSpec(0.1, 0, 1, 1, AllowNegative: false, AllowsEmpty: true);

        Assert.True(NumericFieldMath.TryStep(0.4, 1, spec, out var up));
        Assert.Equal(0.5, up);
        Assert.Equal("0.5", NumericFieldMath.Format(up, spec.Decimals));

        Assert.True(NumericFieldMath.TryStep(0.9, 1, spec, out var atOne));
        Assert.Equal(1, atOne);
        Assert.Equal("1.0", NumericFieldMath.Format(atOne, spec.Decimals));

        Assert.False(NumericFieldMath.TryStep(1, 1, spec, out _));
        Assert.False(NumericFieldMath.TryStep(0, -1, spec, out _));
        Assert.Equal(1, NumericFieldMath.Normalize(1.7, spec));
    }

    [Fact]
    public void GeometryRule_StepsByOneAndKeepsTheMinimumWidgetSize()
    {
        var width = new NumericFieldSpec(1, ConfigValidator.MinWidgetSize, 1024, 0, AllowNegative: false);

        Assert.False(NumericFieldMath.TryStep(ConfigValidator.MinWidgetSize, -1, width, out _));
        Assert.True(NumericFieldMath.TryStep(ConfigValidator.MinWidgetSize, 1, width, out var up));
        Assert.Equal(41, up);
        Assert.Equal(ConfigValidator.MinWidgetSize, NumericFieldMath.Normalize(10, width));
        Assert.Equal(1024, NumericFieldMath.Normalize(5000, width));
    }

    [Fact]
    public void MagnitudeStep_IsOneBelowHundredThenAboutOnePercentAsAPowerOfTen()
    {
        Assert.Equal(1, NumericFieldMath.MagnitudeStep(0));
        Assert.Equal(1, NumericFieldMath.MagnitudeStep(99));
        Assert.Equal(1, NumericFieldMath.MagnitudeStep(100));
        Assert.Equal(10, NumericFieldMath.MagnitudeStep(1_000));
        Assert.Equal(100, NumericFieldMath.MagnitudeStep(5_000));
        Assert.Equal(100_000, NumericFieldMath.MagnitudeStep(12_500_000));

        // The slider step field uses a finer sub-unit step so 0.5 can become 0.6, not 1.5.
        var step = new NumericFieldSpec(1, 0.001, 100, 3, AllowNegative: false, StepByMagnitude: true, FineStep: 0.1);
        Assert.True(NumericFieldMath.TryStep(0.5, 1, step, out var fine));
        Assert.Equal(0.6, fine);
        Assert.True(NumericFieldMath.TryStep(5, 1, step, out var whole));
        Assert.Equal(6, whole);
    }

    [Fact]
    public void LevelRule_IsWholeNumbersZeroToHundred()
    {
        var level = new NumericFieldSpec(1, 0, 100, 0, AllowNegative: false);

        Assert.True(NumericFieldMath.TryStep(99, 1, level, out var up));
        Assert.Equal(100, up);
        Assert.False(NumericFieldMath.TryStep(100, 1, level, out _));
        Assert.False(NumericFieldMath.TryStep(0, -1, level, out _));
        Assert.Equal(0, NumericFieldMath.Normalize(-5, level));
        Assert.Equal(100, NumericFieldMath.Normalize(500, level));
        Assert.Equal("50", NumericFieldMath.Format(50, 0));
    }

    [Fact]
    public void RepeatController_HoldsForFourHundredThenRepeatsEveryEighty()
    {
        var repeat = new NumericRepeatController();
        repeat.Press();

        Assert.True(repeat.IsHolding);
        Assert.Equal(NumericRepeatController.HoldDelay, repeat.NextDelay);

        Assert.False(repeat.Advance(TimeSpan.FromMilliseconds(399)));
        Assert.True(repeat.Advance(TimeSpan.FromMilliseconds(1)));
        Assert.Equal(NumericRepeatController.RepeatInterval, repeat.NextDelay);

        Assert.False(repeat.Advance(TimeSpan.FromMilliseconds(79)));
        Assert.True(repeat.Advance(TimeSpan.FromMilliseconds(1)));
        Assert.False(repeat.Advance(TimeSpan.FromMilliseconds(79)));
        Assert.True(repeat.Advance(TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void RepeatController_QuickTapNeverRepeatsAndReleaseStopsTheCadence()
    {
        var repeat = new NumericRepeatController();
        repeat.Press();
        Assert.False(repeat.Advance(TimeSpan.FromMilliseconds(120)));
        repeat.Release();

        Assert.False(repeat.IsHolding);
        Assert.Null(repeat.NextDelay);
        Assert.False(repeat.Advance(TimeSpan.FromSeconds(10)));

        // A new hold restarts the full delay instead of inheriting the previous elapsed time.
        repeat.Press();
        Assert.False(repeat.Advance(TimeSpan.FromMilliseconds(399)));
        Assert.True(repeat.Advance(TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void GeometryFields_LivePreviewOneUndoGroupAndClampOnCommit()
    {
        var context = Open(Label());
        var editor = context.Editor;

        editor.BeginNumericLiveEdit("XText", fromFocus: true);
        editor.XText = "100";
        Assert.Equal(100, context.Widget.X);
        Assert.Equal("100", editor.XText);

        editor.XText = "200";
        Assert.Equal(200, context.Widget.X);
        Assert.Equal(1, context.Service.UndoDepth);

        editor.CommitNumericLiveEdit("XText");
        Assert.Equal("200", editor.XText);
        Assert.Equal(1, context.Service.UndoDepth);

        // A typed out-of-range value previews the clamp, keeps the typed text, and commits the bound.
        editor.BeginNumericLiveEdit("XText", fromFocus: true);
        editor.XText = "99999";
        Assert.Equal(824, context.Widget.X);
        Assert.Equal("99999", editor.XText);
        editor.CommitNumericLiveEdit("XText");
        Assert.Equal("824", editor.XText);

        editor.BeginNumericLiveEdit("WidthText", fromFocus: true);
        editor.WidthText = "10";
        Assert.Equal(ConfigValidator.MinWidgetSize, context.Widget.Width);
        editor.CommitNumericLiveEdit("WidthText");
        Assert.Equal("40", editor.WidthText);
    }

    [Fact]
    public void GeometryField_MultiKeystrokeGroupUndoesToTheValueBeforeTheGroup()
    {
        var context = Open(Label());
        var editor = context.Editor;

        editor.BeginNumericLiveEdit("XText", fromFocus: true);
        editor.XText = "100";
        editor.XText = "200";
        editor.XText = "300";
        editor.CommitNumericLiveEdit("XText");

        Assert.Equal(1, context.Service.UndoDepth);
        Assert.True(context.Service.TryUndo());
        Assert.Equal(0, context.Service.Widgets[0].X);
    }

    [Fact]
    public void GeometryFields_EmptyOrInvalidTextStepsFromAndRestoresTheLastValidValue()
    {
        var context = Open(Label());
        var editor = context.Editor;

        editor.BeginNumericLiveEdit("XText", fromFocus: true);
        editor.XText = "150";
        editor.XText = "abc";
        Assert.True(editor.HasValidationError);

        Assert.True(editor.StepNumericField("XText", 1));
        Assert.Equal("151", editor.XText);
        Assert.Equal(151, context.Widget.X);

        editor.XText = string.Empty;
        editor.CommitNumericLiveEdit("XText");
        Assert.Equal("151", editor.XText);
        Assert.True(editor.HasValidationError);

        editor.BeginNumericLiveEdit("YText", fromFocus: true);
        editor.YText = "40";
        editor.YText = "nope";
        editor.CommitNumericLiveEdit("YText");
        Assert.Equal("40", editor.YText);
        Assert.Equal(40, context.Widget.Y);
    }

    [Fact]
    public void OpacityField_HasTheSameLiveLifecycleAsFontSize()
    {
        var context = Open(Label());
        var editor = context.Editor;

        editor.BeginNumericLiveEdit("OpacityText", fromFocus: true);
        editor.OpacityText = "0.4";
        Assert.Equal(0.4, context.Widget.Opacity);

        Assert.True(editor.StepNumericField("OpacityText", 1));
        Assert.Equal("0.5", editor.OpacityText);
        Assert.Equal(0.5, context.Widget.Opacity);
        Assert.Equal(1, context.Service.UndoDepth);

        editor.CommitNumericLiveEdit("OpacityText");
        Assert.Equal("0.5", editor.OpacityText);
        Assert.Equal(1, context.Service.UndoDepth);

        // Clearing an appearance override stays inherited instead of pinning the theme default.
        editor.BeginNumericLiveEdit("OpacityText", fromFocus: true);
        editor.OpacityText = string.Empty;
        editor.CommitNumericLiveEdit("OpacityText");
        Assert.Null(context.Config.Pages[0].Widgets[0].Opacity);
        Assert.Equal(editor.ThemeOpacityText, editor.OpacityText);
    }

    [Fact]
    public void RangeFields_StepByMagnitudeAndClampToTheLegalOppositeBound()
    {
        var gauge = new GaugeWidgetConfig
        {
            Id = "g1",
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Sensor = SensorKeys.CpuTotal,
            Minimum = 0,
            Maximum = 12_500_000,
        };
        var context = Open(gauge);
        var editor = context.Editor;
        var config = (GaugeWidgetConfig)context.Config.Pages[0].Widgets[0];

        editor.BeginNumericLiveEdit("MinimumText", fromFocus: true);
        editor.MinimumText = "1000000";
        Assert.Equal(1_000_000, config.Minimum);

        Assert.True(editor.StepNumericField("MinimumText", 1));
        Assert.Equal(1_010_000, config.Minimum);
        Assert.Equal("1010000", editor.MinimumText);

        editor.CommitNumericLiveEdit("MinimumText");
        Assert.Equal("1010000", editor.MinimumText);

        // A typed maximum below minimum + the required span clamps to that legal bound on commit.
        editor.BeginNumericLiveEdit("MaximumText", fromFocus: true);
        editor.MaximumText = "1";
        editor.CommitNumericLiveEdit("MaximumText");
        Assert.Equal(1_010_001, config.Maximum);
        Assert.Equal("1010001", editor.MaximumText);
    }

    [Fact]
    public void SliderStepAndInitialValue_UseTheSliderGeometryAndClamp()
    {
        var slider = new SliderWidgetConfig
        {
            Id = "s1",
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Minimum = 0,
            Maximum = 100,
            Step = 5,
            Value = 50,
        };
        var context = Open(slider);
        var editor = context.Editor;
        var config = (SliderWidgetConfig)context.Config.Pages[0].Widgets[0];

        editor.BeginNumericLiveEdit("StepText", fromFocus: true);
        editor.StepText = "0.5";
        Assert.True(editor.StepNumericField("StepText", 1));
        Assert.Equal(0.6, config.Step);
        Assert.Equal("0.6", editor.StepText);
        editor.CommitNumericLiveEdit("StepText");

        editor.BeginNumericLiveEdit("InitialValueText", fromFocus: true);
        editor.InitialValueText = "70";
        Assert.True(editor.StepNumericField("InitialValueText", 1));
        Assert.Equal(70.8, config.Value, 3);
        Assert.Equal("70.8", editor.InitialValueText);

        editor.InitialValueText = "150";
        editor.CommitNumericLiveEdit("InitialValueText");
        Assert.Equal(100, config.Value);
        Assert.Equal("100", editor.InitialValueText);
    }

    [Fact]
    public void WindowField_StepsByOneAndClampsToTheConfiguredBounds()
    {
        var sparkline = new SparklineWidgetConfig
        {
            Id = "sp1",
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Sensor = SensorKeys.CpuTotal,
            WindowSeconds = 60,
        };
        var context = Open(sparkline);
        var editor = context.Editor;
        var config = (SparklineWidgetConfig)context.Config.Pages[0].Widgets[0];

        editor.BeginNumericLiveEdit("WindowText", fromFocus: true);
        Assert.True(editor.StepNumericField("WindowText", 1));
        Assert.Equal(61, config.WindowSeconds);

        editor.WindowText = "99999";
        Assert.Equal(SparklineWidgetConfig.MaxWindowSeconds, config.WindowSeconds);
        editor.CommitNumericLiveEdit("WindowText");
        Assert.Equal("3600", editor.WindowText);

        editor.BeginNumericLiveEdit("WindowText", fromFocus: true);
        editor.WindowText = "1";
        editor.CommitNumericLiveEdit("WindowText");
        Assert.Equal(SparklineWidgetConfig.MinWindowSeconds, config.WindowSeconds);
        Assert.Equal("10", editor.WindowText);
    }

    [Fact]
    public void VolumeLevel_StepsClampsAndUsesTheLiveUndoGroup()
    {
        var button = new ButtonWidgetConfig
        {
            Id = "b1",
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Action = new VolumeActionConfig { Mode = VolumeMode.Set, Level = 50 },
        };
        var context = Open(button);
        var editor = context.Editor;
        var volume = (VolumeActionConfig)context.Config.Pages[0].Widgets[0].Action!;

        editor.BeginNumericLiveEdit("LevelText", fromFocus: true);
        editor.ActionPicker!.LevelText = "50";
        Assert.True(editor.StepNumericField("LevelText", 1));
        Assert.Equal(51, volume.Level);
        Assert.Equal("51", editor.ActionPicker.LevelText);
        Assert.Equal(1, context.Service.UndoDepth);

        editor.CommitNumericLiveEdit("LevelText");
        Assert.Equal("51", editor.ActionPicker.LevelText);

        editor.ActionPicker.LevelText = "100";
        Assert.False(editor.CanStepNumericField("LevelText", 1));
        Assert.True(editor.CanStepNumericField("LevelText", -1));

        editor.BeginNumericLiveEdit("LevelText", fromFocus: true);
        editor.ActionPicker.LevelText = "abc";
        editor.CommitNumericLiveEdit("LevelText");
        Assert.Equal("100", editor.ActionPicker.LevelText);
        Assert.True(editor.ActionPicker.HasValidationError);
    }

    [Fact]
    public void StepperHolds_AreOneUndoGroupEachAndSwitchingFieldsCommitsThePreviousGroup()
    {
        var context = Open(Label());
        var editor = context.Editor;

        // No focus: the stepper owns the group and its release commits it.
        Assert.True(editor.StepNumericField("XText", 1));
        Assert.True(editor.StepNumericField("XText", 1));
        Assert.True(editor.StepNumericField("XText", 1));
        Assert.Equal(1, context.Service.UndoDepth);
        editor.CommitNumericStepHold("XText");
        Assert.Equal(1, context.Service.UndoDepth);

        Assert.True(editor.StepNumericField("XText", 1));
        editor.CommitNumericStepHold("XText");
        Assert.Equal(2, context.Service.UndoDepth);

        // A focused field group and a following stepper on another field are separate groups.
        editor.OpacityText = "0.5";
        Assert.Equal(3, context.Service.UndoDepth);
        editor.BeginNumericLiveEdit("FontSizeText", fromFocus: true);
        editor.FontSizeText = "30";
        Assert.Equal(4, context.Service.UndoDepth);

        Assert.True(editor.StepNumericField("OpacityText", 1));
        Assert.Equal(5, context.Service.UndoDepth);
        editor.CommitNumericStepHold("OpacityText");
        Assert.Equal(5, context.Service.UndoDepth);
    }

    private static Context Open(WidgetConfig widget)
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = PageId,
            Pages = [new PageConfig { Id = PageId, Widgets = [widget] }],
        };
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
        service.Enter(1024, 600);
        service.SelectWidget(page.Widgets[0]);
        service.OpenPropertyEditor();
        return new Context(config, service, service.PropertyEditor!, page.Widgets[0]);
    }

    private static LabelWidgetConfig Label() => new()
    {
        Id = "l1",
        X = 0,
        Y = 0,
        Width = 200,
        Height = 100,
        Label = "l1",
        FontSize = 24,
    };

    private sealed record Context(
        PanelConfig Config,
        EditModeService Service,
        PropertyEditorViewModel Editor,
        WidgetViewModelBase Widget);
}
