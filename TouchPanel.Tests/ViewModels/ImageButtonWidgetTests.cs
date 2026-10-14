using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// ImageButton widget view model (RF-046/M3-RF-464/465): exactly one action per completed tap,
/// transient failure state on failure, disabled-when-invalid-action (not dropped), no execution in
/// edit mode, and the icon/label projection (including the neutral default glyph).
/// </summary>
public sealed class ImageButtonWidgetTests
{
    [Fact]
    public async Task ActivateAsync_OneTap_InvokesTheBoundActionExactlyOnce()
    {
        var executor = new RecordingActionExecutor();
        var action = new MuteActionConfig();
        var viewModel = new ImageButtonWidgetViewModel(CreateConfig(action), executor);

        await viewModel.ActivateAsync();

        var invocation = Assert.Single(executor.Invocations);
        Assert.Same(action, invocation.Action);
        Assert.Equal("image-button-1", invocation.WidgetId);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task ActivateAsync_FailedResult_ShowsTransientError()
    {
        var executor = new RecordingActionExecutor
        {
            ResultFactory = (_, _) => ActionExecutionResult.Failed("boom"),
        };
        var viewModel = new ImageButtonWidgetViewModel(
            CreateConfig(new MuteActionConfig()),
            executor,
            new FakeTimeProvider());

        await viewModel.ActivateAsync();

        Assert.True(viewModel.HasError);
        Assert.Equal("boom", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ActivateAsync_DisabledWidget_ExecutesNothing()
    {
        var executor = new RecordingActionExecutor();
        var config = CreateConfig(action: null);
        config.IsEnabled = false;
        config.DisabledReason = "Widget has no action bound and was disabled.";
        var viewModel = new ImageButtonWidgetViewModel(config, executor);

        await viewModel.ActivateAsync();

        Assert.Empty(executor.Invocations);
        Assert.False(viewModel.IsEnabled);
        Assert.True(viewModel.ShowsDisabledIndicator);
    }

    [Fact]
    public async Task ActivateAsync_InEditMode_DoesNotExecute()
    {
        var executor = new RecordingActionExecutor();
        var viewModel = new ImageButtonWidgetViewModel(CreateConfig(new MuteActionConfig()), executor);
        viewModel.SetEditMode(true);

        await viewModel.ActivateAsync();

        Assert.Empty(executor.Invocations);
    }

    [Fact]
    public void Projection_IconPresent_ShowsIconAndOptionalLabel()
    {
        var viewModel = new ImageButtonWidgetViewModel(CreateConfig(new MuteActionConfig(), icon: "power", showLabel: true));

        Assert.True(viewModel.HasIcon);
        Assert.Equal(IconCatalog.GlyphFor("power"), viewModel.DisplayGlyph);
        Assert.True(viewModel.ShowIcon);
        Assert.True(viewModel.ShowLabelText);
    }

    [Fact]
    public void Projection_NoIconWithLabel_ShowsLabelInsteadOfTheDefaultGlyph()
    {
        var viewModel = new ImageButtonWidgetViewModel(CreateConfig(new MuteActionConfig(), icon: null, showLabel: true));

        Assert.False(viewModel.HasIcon);
        Assert.False(viewModel.ShowIcon);
        Assert.True(viewModel.ShowLabelText);
    }

    [Fact]
    public void Projection_NoIconAndNoLabel_ShowsTheNeutralDefaultGlyph()
    {
        var viewModel = new ImageButtonWidgetViewModel(CreateConfig(new MuteActionConfig(), icon: null, showLabel: true, label: null));

        Assert.False(viewModel.HasIcon);
        Assert.True(viewModel.ShowIcon);
        Assert.Equal(ImageButtonWidgetViewModel.DefaultGlyph, viewModel.DisplayGlyph);
        Assert.False(viewModel.ShowLabelText);
    }

    [Fact]
    public void Projection_ShowLabelFalse_HidesTheLabelEvenWhenPresent()
    {
        var viewModel = new ImageButtonWidgetViewModel(CreateConfig(new MuteActionConfig(), icon: null, showLabel: false));

        Assert.False(viewModel.ShowLabelText);
        Assert.True(viewModel.ShowIcon);
    }

    private static ImageButtonWidgetConfig CreateConfig(
        ActionConfig? action,
        string? icon = "power",
        bool showLabel = true,
        string? label = "Go") => new()
        {
            Id = "image-button-1",
            X = 0,
            Y = 0,
            Width = 120,
            Height = 120,
            Label = label,
            Icon = icon,
            ShowLabel = showLabel,
            Action = action,
        };
}
