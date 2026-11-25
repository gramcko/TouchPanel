using System.Globalization;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Font-size stepper behavior (user-requested addition): the −/+ commands and the Up/Down keys step
/// the live font size by one DIP, clamp to the existing 8–96 range, fall back to the last valid value
/// on empty/invalid text, canonicalise the field, and share the live-edit undo-group policy (one
/// undo step per focused sequence, undo restoring the value from before the first step).
/// </summary>
public sealed class FontSizeStepperTests
{
    private const string PageId = "main";

    [Fact]
    public void Increase_StepsOneFromTheParsedTextAndAppliesLive()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        Assert.True(editor.CanIncreaseFontSize);
        Assert.True(editor.IncreaseFontSize());

        Assert.Equal("25", editor.FontSizeText);
        Assert.Equal(25, context.Widgets[0].FontSize);
        Assert.Equal(25, context.Config.Pages[0].Widgets[0].FontSize);
        Assert.True(context.Service.IsDirty);
        Assert.Equal(1, context.Service.UndoDepth);
    }

    [Fact]
    public void Decrease_StepsOneFromTheParsedTextAndAppliesLive()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        Assert.True(editor.CanDecreaseFontSize);
        Assert.True(editor.DecreaseFontSize());

        Assert.Equal("23", editor.FontSizeText);
        Assert.Equal(23, context.Widgets[0].FontSize);
        Assert.Equal(23, context.Config.Pages[0].Widgets[0].FontSize);
    }

    [Fact]
    public void Stepper_IsDisabledAndIgnoredAtTheUpperBound()
    {
        var context = CreateContext(Label("l1", WidgetAppearance.MaxFontSize));
        context.EnterSelect();
        var editor = context.Editor;

        Assert.False(editor.CanIncreaseFontSize);
        Assert.True(editor.CanDecreaseFontSize);
        Assert.False(editor.IncreaseFontSize());

        Assert.Equal("96", editor.FontSizeText);
        Assert.Equal(WidgetAppearance.MaxFontSize, context.Widgets[0].FontSize);
        Assert.Equal(0, context.Service.UndoDepth);

        // Stepping back is still allowed at the bound.
        Assert.True(editor.DecreaseFontSize());
        Assert.Equal("95", editor.FontSizeText);
    }

    [Fact]
    public void Stepper_IsDisabledAndIgnoredAtTheLowerBound()
    {
        var context = CreateContext(Label("l1", WidgetAppearance.MinFontSize));
        context.EnterSelect();
        var editor = context.Editor;

        Assert.False(editor.CanDecreaseFontSize);
        Assert.True(editor.CanIncreaseFontSize);
        Assert.False(editor.DecreaseFontSize());

        Assert.Equal("8", editor.FontSizeText);
        Assert.Equal(WidgetAppearance.MinFontSize, context.Widgets[0].FontSize);
        Assert.Equal(0, context.Service.UndoDepth);

        Assert.True(editor.IncreaseFontSize());
        Assert.Equal("9", editor.FontSizeText);
    }

    [Fact]
    public void Stepper_InvalidTextFallsBackToTheLastValidValue()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        editor.BeginLiveEdit();
        editor.FontSizeText = "30";
        editor.FontSizeText = "abc";

        Assert.True(editor.HasValidationError);

        Assert.True(editor.IncreaseFontSize());

        Assert.Equal("31", editor.FontSizeText);
        Assert.Equal(31, context.Widgets[0].FontSize);
        Assert.False(editor.HasValidationError);
    }

    [Fact]
    public void Stepper_EmptyTextFallsBackToTheLastValidValue()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        editor.BeginLiveEdit();
        editor.FontSizeText = string.Empty;

        Assert.True(editor.DecreaseFontSize());

        Assert.Equal("23", editor.FontSizeText);
        Assert.Equal(23, context.Widgets[0].FontSize);
    }

    [Fact]
    public void Stepper_InvalidTextOutsideALiveSessionFallsBackToTheAppliedValue()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        // The non-live setter keeps an invalid entry visible (with feedback) without mutating.
        editor.FontSizeText = "abc";
        Assert.True(editor.HasValidationError);
        Assert.Equal(24, context.Widgets[0].FontSize);

        Assert.True(editor.IncreaseFontSize());

        Assert.Equal("25", editor.FontSizeText);
        Assert.Equal(25, context.Widgets[0].FontSize);
        Assert.False(editor.HasValidationError);
    }

    [Fact]
    public void Stepper_FromAnInheritedThemeDefaultStepsFromTheDisplayedValue()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;

        var themeValue = double.Parse(editor.ThemeFontSizeText, CultureInfo.InvariantCulture);
        Assert.True(editor.IncreaseFontSize());

        Assert.Equal(
            (themeValue + PropertyEditorViewModel.FontSizeStepValue).ToString("0.###", CultureInfo.InvariantCulture),
            editor.FontSizeText);
        Assert.Equal(
            (themeValue + PropertyEditorViewModel.FontSizeStepValue).ToString("0.###", CultureInfo.InvariantCulture),
            context.Config.Pages[0].Widgets[0].FontSize!.Value.ToString("0.###", CultureInfo.InvariantCulture));
        Assert.Equal(themeValue + PropertyEditorViewModel.FontSizeStepValue, context.Widgets[0].FontSize);
    }

    [Fact]
    public void ConsecutiveStepsInOneFocusedSessionAreOneUndoStep()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        // The field is focused: the live session is open, and every step joins its group.
        editor.BeginLiveEdit();
        Assert.True(editor.IncreaseFontSize());
        Assert.True(editor.IncreaseFontSize());
        Assert.True(editor.IncreaseFontSize());

        Assert.Equal("27", editor.FontSizeText);
        Assert.Equal(27, context.Widgets[0].FontSize);
        Assert.Equal(1, context.Service.UndoDepth);

        editor.CommitLiveEdit();
        Assert.Equal(1, context.Service.UndoDepth);

        // Undo restores the value from before the first step, not the previous tap.
        Assert.True(context.Service.TryUndo());
        Assert.Equal(24, context.Service.Widgets[0].FontSize);
        Assert.Equal(24, context.Config.Pages[0].Widgets[0].FontSize);
        Assert.Equal(0, context.Service.UndoDepth);
    }

    [Fact]
    public void ConsecutiveStepsWithoutFocusStillGroupAndTheNextSessionStartsANewGroup()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        // A touch tap does not focus the field; the first step opens the session so the following
        // taps stay in the same group instead of piling up one undo step each.
        Assert.True(editor.IncreaseFontSize());
        Assert.True(editor.IncreaseFontSize());
        Assert.Equal("26", editor.FontSizeText);
        Assert.Equal(1, context.Service.UndoDepth);

        // LostFocus/Enter/selection change ends the group (EndLiveEdit is what those paths call).
        editor.CommitLiveEdit();

        Assert.True(editor.IncreaseFontSize());
        Assert.Equal("27", editor.FontSizeText);
        Assert.Equal(2, context.Service.UndoDepth);

        Assert.True(context.Service.TryUndo());
        Assert.Equal(26, context.Service.Widgets[0].FontSize);
        Assert.True(context.Service.TryUndo());
        Assert.Equal(24, context.Service.Widgets[0].FontSize);
    }

    [Fact]
    public void StepperAndTypingShareTheSameUndoGroup()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        editor.BeginLiveEdit();
        editor.FontSizeText = "30";
        Assert.True(editor.IncreaseFontSize());

        Assert.Equal("31", editor.FontSizeText);
        Assert.Equal(1, context.Service.UndoDepth);

        Assert.True(context.Service.TryUndo());
        Assert.Equal(24, context.Service.Widgets[0].FontSize);
    }

    [Fact]
    public void StepperCommandsExecuteTheSameSteps()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        Assert.True(editor.IncreaseFontSizeCommand.CanExecute(null));
        editor.IncreaseFontSizeCommand.Execute(null);
        Assert.Equal("25", editor.FontSizeText);

        Assert.True(editor.DecreaseFontSizeCommand.CanExecute(null));
        editor.DecreaseFontSizeCommand.Execute(null);
        Assert.Equal("24", editor.FontSizeText);

        Assert.Equal(24, context.Widgets[0].FontSize);
        Assert.Equal(1, context.Service.UndoDepth);
    }

    [Fact]
    public void StepperEnablementFollowsTheTypedTextAndTheAppliedBounds()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        editor.BeginLiveEdit();
        editor.FontSizeText = "1";
        Assert.False(editor.CanDecreaseFontSize, "A typed value below the minimum previews the bound, so it cannot decrease.");
        Assert.True(editor.CanIncreaseFontSize);

        editor.FontSizeText = "120";
        Assert.False(editor.CanIncreaseFontSize, "A typed value above the maximum previews the bound, so it cannot increase.");
        Assert.True(editor.CanDecreaseFontSize);

        editor.FontSizeText = "8";
        Assert.False(editor.CanDecreaseFontSize);
        Assert.True(editor.CanIncreaseFontSize);

        editor.FontSizeText = "96";
        Assert.True(editor.CanDecreaseFontSize);
        Assert.False(editor.CanIncreaseFontSize);
    }

    [Fact]
    public void Stepper_ClampsTypedOutOfRangeTextBeforeStepping()
    {
        var context = CreateContext(Label("l1", 24));
        context.EnterSelect();
        var editor = context.Editor;

        editor.BeginLiveEdit();
        editor.FontSizeText = "120";
        Assert.True(editor.DecreaseFontSize());

        // Stepping starts from the applied 96 bound, not from the literal 120.
        Assert.Equal("95", editor.FontSizeText);
        Assert.Equal(95, context.Widgets[0].FontSize);

        editor.FontSizeText = "1";
        Assert.True(editor.IncreaseFontSize());
        Assert.Equal("9", editor.FontSizeText);

        Assert.False(editor.StepFontSize(0));
    }

    private static TestContext CreateContext(params WidgetConfig[] widgets)
    {
        var config = ConfigWith(widgets);
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var store = new RecordingConfigStore();
        var service = new EditModeService(config, page, store, factory: registry, palette: registry);
        return new TestContext(config, page, store, service);
    }

    private static PanelConfig ConfigWith(params WidgetConfig[] widgets) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = PageId,
        Pages = [new PageConfig { Id = PageId, Widgets = [.. widgets] }],
    };

    private static LabelWidgetConfig Label(string id, double? fontSize = null) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 200,
        Height = 100,
        Label = id,
        FontSize = fontSize,
    };

    private sealed record TestContext(
        PanelConfig Config,
        PageViewModel Page,
        RecordingConfigStore Store,
        EditModeService Service)
    {
        public IReadOnlyList<WidgetViewModelBase> Widgets => Page.Widgets;

        public PropertyEditorViewModel Editor => Service.PropertyEditor!;

        public void EnterSelect()
        {
            Service.Enter(1000, 500);
            Service.SelectWidget(Widgets[0]);
            Service.OpenPropertyEditor();
        }
    }
}
