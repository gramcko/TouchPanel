using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json;
using TouchPanel.Config;
using TouchPanel.Monitoring;
using TouchPanel.Tests.Monitoring;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Property-editor behavior (T-13, RF-043/M2-RF-433): the editor is created only for an editable
/// widget selected in edit mode, exposes the applicable type-specific fields, validates every input
/// with the config validator's bounds (blocking or clamping invalid values with visible feedback),
/// updates the live widget without a rebuild, marks the layout dirty, and persists through the
/// existing single-writer save path. No file, hardware, or user profile is touched.
/// </summary>
public sealed class PropertyEditorTests
{
    private const string PageId = "main";

    [Fact]
    public void SelectWidget_DoesNotOpenTheEditor_OpenPropertyEditorDoes()
    {
        var context = CreateContext(Label("l1"));

        // Outside edit mode the editor never appears.
        context.Service.SelectWidget(context.Widgets[0]);
        Assert.False(context.Service.HasPropertyEditor);
        Assert.Null(context.Service.PropertyEditor);

        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widgets[0]);

        // UX fix #1: selecting a widget only enables move/resize and its affordances; it must not
        // auto-open the edit menu.
        Assert.False(context.Service.HasPropertyEditor);
        Assert.Null(context.Service.PropertyEditor);

        // The per-widget Edit affordance opens the editor for the selected widget.
        Assert.True(context.Service.OpenPropertyEditor());
        var editor = context.Service.PropertyEditor;
        Assert.NotNull(editor);
        Assert.True(context.Service.HasPropertyEditor);
        Assert.Equal("l1", editor!.WidgetId);

        // Re-opening for the same selection keeps the editor instance (field state survives).
        Assert.True(context.Service.OpenPropertyEditor());
        Assert.Same(editor, context.Service.PropertyEditor);

        // Selecting away (or clearing) closes the menu.
        context.Service.SelectWidget(null);
        Assert.Null(context.Service.PropertyEditor);
        Assert.False(context.Service.HasPropertyEditor);
    }

    [Fact]
    public void OpenPropertyEditor_OutsideEditModeOrWithoutSelection_IsNoOp()
    {
        var context = CreateContext(Label("l1"));

        Assert.False(context.Service.OpenPropertyEditor());

        context.Service.Enter(1000, 500);
        Assert.False(context.Service.OpenPropertyEditor());

        context.Service.SelectWidget(context.Widgets[0]);
        Assert.True(context.Service.OpenPropertyEditor());
    }

    [Fact]
    public async Task ExitEditMode_ClearsTheEditor()
    {
        var context = CreateContext(Label("l1"));
        context.Service.Enter(1000, 500);
        context.Service.SelectWidget(context.Widgets[0]);
        Assert.True(context.Service.OpenPropertyEditor());
        Assert.True(context.Service.HasPropertyEditor);

        await context.Service.ExitAsync();

        Assert.Null(context.Service.PropertyEditor);
        Assert.False(context.Service.HasPropertyEditor);
    }

    [Fact]
    public void UnknownWidgetType_HasNoEditor()
    {
        using var raw = JsonDocument.Parse("{}");
        var config = ConfigWith(new UnknownWidgetConfig("heatmap", raw.RootElement.Clone()));
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
        service.Enter(1000, 500);
        service.SelectWidget(page.Widgets[0]);

        Assert.Null(service.PropertyEditor);
        Assert.False(service.HasPropertyEditor);
    }

    [Fact]
    public void Editor_ExposesOnlyTheApplicableTypeSpecificSections()
    {
        Assert.True(EditorFor(Slider("s1")).HasRange);
        Assert.True(EditorFor(Slider("s1")).HasStep);
        Assert.True(EditorFor(Slider("s1")).HasInitialValue);
        Assert.True(EditorFor(Slider("s1")).HasOrientation);
        Assert.False(EditorFor(Slider("s1")).HasSensor);

        Assert.True(EditorFor(Gauge("g1")).HasSensor);
        Assert.True(EditorFor(Gauge("g1")).HasRange);
        Assert.False(EditorFor(Gauge("g1")).HasStep);
        Assert.False(EditorFor(Gauge("g1")).HasInitialValue);

        Assert.True(EditorFor(Sparkline("sp1")).HasSensor);
        Assert.True(EditorFor(Sparkline("sp1")).HasWindow);
        Assert.True(EditorFor(Sparkline("sp1")).HasStyle);
        Assert.False(EditorFor(Sparkline("sp1")).HasRange);

        Assert.True(EditorFor(Toggle("t1")).HasPersistState);
        Assert.False(EditorFor(Label("l1")).HasRange);
    }

    [Fact]
    public void LabelEdit_UpdatesConfigLiveWidgetAndDirty()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        string? changed = null;
        ((INotifyPropertyChanged)widget).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WidgetViewModelBase.Label))
            {
                changed = nameof(WidgetViewModelBase.Label);
            }
        };

        editor.LabelText = "Hello";

        Assert.Equal("Hello", context.Config.Pages[0].Widgets[0].Label);
        Assert.Equal("Hello", widget.Label);
        Assert.Equal(nameof(WidgetViewModelBase.Label), changed);
        Assert.True(context.Service.IsDirty);
        Assert.False(editor.HasValidationError);
    }

    [Fact]
    public void LabelOverMaxLength_IsBlockedWithFeedbackAndNoMutation()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.LabelText = new string('a', PropertyEditorViewModel.MaxLabelLength + 1);

        Assert.True(editor.HasValidationError);
        Assert.NotNull(editor.ValidationError);
        Assert.Equal("l1", context.Config.Pages[0].Widgets[0].Label);
        Assert.Equal("l1", context.Widgets[0].Label);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void GaugeRangeEdit_ValidUpdatesLiveAndInvalidIsBlocked()
    {
        var context = CreateContext(Gauge("g1"));
        context.EnterSelect();
        var editor = context.Editor;
        var gauge = Assert.IsType<GaugeWidgetViewModel>(context.Widgets[0]);

        // A span below the minimum is blocked and leaves the configuration untouched.
        editor.MaximumText = "0.5";
        Assert.True(editor.HasValidationError);
        Assert.Equal(100, context.Config.Pages[0].Widgets[0] is GaugeWidgetConfig g ? g.Maximum : 0);
        Assert.False(context.Service.IsDirty);

        // A valid range commits and is projected live.
        editor.MaximumText = "200";
        Assert.False(editor.HasValidationError);
        Assert.Equal(200, ((GaugeWidgetConfig)context.Config.Pages[0].Widgets[0]).Maximum);
        Assert.Equal(200, gauge.Maximum);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void SliderRangeEdit_BlocksInvalidStepAndRangeAndAcceptsValidValues()
    {
        var context = CreateContext(Slider("s1"));
        context.EnterSelect();
        var editor = context.Editor;
        var slider = Assert.IsType<SliderWidgetViewModel>(context.Widgets[0]);

        editor.StepText = "0";
        Assert.True(editor.HasValidationError);

        editor.StepText = "500";
        Assert.True(editor.HasValidationError);

        editor.MinimumText = "150";
        Assert.True(editor.HasValidationError);

        editor.StepText = "5";
        editor.MaximumText = "50";
        editor.MinimumText = "10";

        Assert.False(editor.HasValidationError);
        var config = (SliderWidgetConfig)context.Config.Pages[0].Widgets[0];
        Assert.Equal(10, config.Minimum);
        Assert.Equal(50, config.Maximum);
        Assert.Equal(5, config.Step);
        Assert.Equal(10, slider.Minimum);
        Assert.Equal(50, slider.Maximum);
        Assert.Equal(5, slider.Step);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void SliderOrientationEdit_UpdatesConfigAndLiveWidget()
    {
        var context = CreateContext(Slider("s1"));
        context.EnterSelect();
        var editor = context.Editor;
        var slider = Assert.IsType<SliderWidgetViewModel>(context.Widgets[0]);

        Assert.Equal(System.Windows.Controls.Orientation.Horizontal, slider.Orientation);

        editor.SelectedOrientation = SliderOrientation.Vertical;

        Assert.Equal(SliderOrientation.Vertical, ((SliderWidgetConfig)context.Config.Pages[0].Widgets[0]).Orientation);
        Assert.Equal(System.Windows.Controls.Orientation.Vertical, slider.Orientation);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void SliderRangeChange_ClampsTheLiveValueIntoTheEditedRange()
    {
        var context = CreateContext(Slider("s1"));
        context.EnterSelect();
        var editor = context.Editor;
        var slider = Assert.IsType<SliderWidgetViewModel>(context.Widgets[0]);
        Assert.Equal(50, slider.Value);

        editor.MaximumText = "10";

        Assert.Equal(10, slider.Maximum);
        Assert.Equal(10, slider.Value);
    }

    [Fact]
    public void SliderInitialValueEdit_ValidUpdatesConfigLiveWidgetAndDirty()
    {
        var context = CreateContext(Slider("s1"));
        context.EnterSelect();
        var editor = context.Editor;
        var slider = Assert.IsType<SliderWidgetViewModel>(context.Widgets[0]);

        Assert.Equal("50", editor.InitialValueText);

        editor.InitialValueText = "70";

        Assert.False(editor.HasValidationError);
        Assert.Equal(70, ((SliderWidgetConfig)context.Config.Pages[0].Widgets[0]).Value);
        Assert.Equal(70, slider.Value);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void SliderInitialValueEdit_OutOfRangeClampsAndStepMismatchSnapsLikeTheValidator()
    {
        var context = CreateContext(Slider("s1"));
        context.EnterSelect();
        var editor = context.Editor;

        // Out of range is clamped to the maximum (ConfigValidator semantics), not blocked.
        editor.InitialValueText = "150";
        Assert.False(editor.HasValidationError);
        Assert.Equal(100, ((SliderWidgetConfig)context.Config.Pages[0].Widgets[0]).Value);
        Assert.Equal("100", editor.InitialValueText);

        // A step mismatch snaps to the nearest step away from zero (7 -> 8 for step 2).
        editor.StepText = "2";
        editor.InitialValueText = "7";
        Assert.False(editor.HasValidationError);
        Assert.Equal(8, ((SliderWidgetConfig)context.Config.Pages[0].Widgets[0]).Value);
        Assert.Equal("8", editor.InitialValueText);
    }

    [Fact]
    public void SliderInitialValueEdit_NonNumericIsRejectedWithoutMutation()
    {
        var context = CreateContext(Slider("s1"));
        context.EnterSelect();
        var editor = context.Editor;
        var slider = Assert.IsType<SliderWidgetViewModel>(context.Widgets[0]);

        editor.InitialValueText = "abc";

        Assert.True(editor.HasValidationError);
        Assert.Equal("Value must be a number.", editor.ValidationError);
        Assert.Equal(50, ((SliderWidgetConfig)context.Config.Pages[0].Widgets[0]).Value);
        Assert.Equal(50, slider.Value);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void SliderRangeEdit_PreservesTheTransientRuntimeValue()
    {
        var context = CreateContext(Slider("s1"));
        context.EnterSelect();
        var editor = context.Editor;
        var slider = Assert.IsType<SliderWidgetViewModel>(context.Widgets[0]);

        // Drag the live value away from the configured initial value (50).
        slider.Value = 70;

        // Editing the range re-clamps the transient value instead of resetting it to the initial value.
        editor.MaximumText = "80";

        Assert.Equal(70, slider.Value);
        Assert.Equal(50, ((SliderWidgetConfig)context.Config.Pages[0].Widgets[0]).Value);
    }

    [Fact]
    public async Task SliderInitialValueEdit_PersistsThroughTheSingleWriterOnExit()
    {
        var context = CreateContext(Slider("s1"));
        context.EnterSelect();
        context.Editor.InitialValueText = "70";

        var result = await context.Service.ExitAsync();

        Assert.True(result.Succeeded);
        var saved = Assert.IsType<SliderWidgetConfig>(context.Store.LastSavedConfig!.Pages[0].Widgets[0]);
        Assert.Equal(70, saved.Value);
    }

    [Fact]
    public void SparklineWindowEdit_ClampsToTheConfiguredBounds()
    {
        var context = CreateContext(Sparkline("sp1"));
        context.EnterSelect();
        var editor = context.Editor;
        var sparkline = Assert.IsType<SparklineWidgetViewModel>(context.Widgets[0]);

        editor.WindowText = "5";
        Assert.Equal(SparklineWidgetConfig.MinWindowSeconds, ((SparklineWidgetConfig)context.Config.Pages[0].Widgets[0]).WindowSeconds);
        Assert.Equal(SparklineWidgetConfig.MinWindowSeconds, sparkline.WindowSeconds);

        editor.WindowText = "99999";
        Assert.Equal(SparklineWidgetConfig.MaxWindowSeconds, sparkline.WindowSeconds);

        editor.WindowText = "120";
        Assert.Equal(120, sparkline.WindowSeconds);
    }

    [Fact]
    public void SparklineSensorEdit_UpdatesLiveAndBlankIsBlocked()
    {
        var context = CreateContext(Sparkline("sp1"));
        context.EnterSelect();
        var editor = context.Editor;
        var sparkline = Assert.IsType<SparklineWidgetViewModel>(context.Widgets[0]);

        editor.SensorText = "  ";
        Assert.True(editor.HasValidationError);
        Assert.Equal(SensorKeys.CpuTotal, ((SparklineWidgetConfig)context.Config.Pages[0].Widgets[0]).Sensor);

        editor.SensorText = SensorKeys.GpuUsage;
        Assert.False(editor.HasValidationError);
        Assert.Equal(SensorKeys.GpuUsage, sparkline.SensorKey);
    }

    [Fact]
    public void TogglePersistAndInitialState_UpdateConfigAndLiveWidget()
    {
        var context = CreateContext(Toggle("t1"));
        context.EnterSelect();
        var editor = context.Editor;
        var toggle = Assert.IsType<ToggleWidgetViewModel>(context.Widgets[0]);

        Assert.False(toggle.IsOn);
        editor.InitialOn = true;
        Assert.True(toggle.IsOn);
        Assert.True(((ToggleWidgetConfig)context.Config.Pages[0].Widgets[0]).InitialOn);

        editor.PersistState = true;
        Assert.True(((ToggleWidgetConfig)context.Config.Pages[0].Widgets[0]).PersistState);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void GeometryEdit_ClampsThroughTheEditService()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];

        editor.XText = "-50";
        Assert.Equal(0, widget.X);

        editor.WidthText = "5";
        Assert.Equal(EditModeService.MinimumWidgetSize, widget.Width);

        editor.XText = "999999";
        Assert.True(widget.X + widget.Width <= context.Service.PageWidth);
        Assert.True(context.Service.IsDirty);

        // Non-numeric geometry input is blocked with feedback and not written.
        var before = widget.Width;
        editor.WidthText = "abc";
        Assert.True(editor.HasValidationError);
        Assert.Equal(before, widget.Width);
    }

    [Fact]
    public async Task PropertyEdits_PersistThroughTheSingleWriterOnExit()
    {
        var context = CreateContext(Slider("s1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.LabelText = "Volume";
        editor.MaximumText = "80";
        editor.SelectedOrientation = SliderOrientation.Vertical;

        var result = await context.Service.ExitAsync();

        Assert.True(result.Succeeded);
        var saved = Assert.IsType<SliderWidgetConfig>(context.Store.LastSavedConfig!.Pages[0].Widgets[0]);
        Assert.Equal("Volume", saved.Label);
        Assert.Equal(80, saved.Maximum);
        Assert.Equal(SliderOrientation.Vertical, saved.Orientation);
    }

    [Fact]
    public void Editor_FormatsExistingValuesUsingInvariantCulture()
    {
        var context = CreateContext(SliderMinimumFraction());
        context.EnterSelect();

        Assert.Equal("2.5", context.Editor.MinimumText);
    }

    // ---- M4-RF-432: the property editor covers Clock / ImageButton / Bar (reverses M3-RF-468) ----

    [Fact]
    public void Editor_ExposesTheM3TypeSpecificSections()
    {
        var clock = EditorFor(Clock("c1"));
        Assert.True(clock.HasMode);
        Assert.True(clock.HasFormat);
        Assert.False(clock.HasRange);
        Assert.False(clock.HasActionPicker);

        var imageButton = EditorFor(ImageButton("ib1"));
        Assert.True(imageButton.HasShowLabel);
        Assert.True(imageButton.HasActionPicker);
        Assert.False(imageButton.HasRange);

        var bar = EditorFor(Bar("bar1"));
        Assert.True(bar.HasSensor);
        Assert.True(bar.HasRange);
        Assert.True(bar.HasShowValue);
        Assert.False(bar.HasStep);
        Assert.False(bar.HasInitialValue);

        var image = EditorFor(Image("img1"));
        Assert.True(image.HasImagePath);
        Assert.True(image.HasStretch);

        // A widget without the fields never exposes them.
        Assert.False(EditorFor(Label("l1")).HasMode);
        Assert.False(EditorFor(Label("l1")).HasFormat);
        Assert.False(EditorFor(Label("l1")).HasShowLabel);
        Assert.False(EditorFor(Label("l1")).HasShowValue);
        Assert.False(EditorFor(Label("l1")).HasImagePath);
        Assert.False(EditorFor(Label("l1")).HasStretch);
    }

    [Fact]
    public void ClockModeAndFormat_UpdateConfigLiveProjectionAndDirty()
    {
        var context = CreateContext(Clock("c1"));
        context.EnterSelect();
        var editor = context.Editor;
        var clock = Assert.IsType<ClockWidgetViewModel>(context.Widgets[0]);

        Assert.Equal(ClockMode.Time24, editor.SelectedMode);

        editor.SelectedMode = ClockMode.Time12;
        Assert.Equal(ClockMode.Time12, ((ClockWidgetConfig)context.Config.Pages[0].Widgets[0]).Mode);
        Assert.Equal("h:mm tt", clock.EffectiveFormat);

        editor.FormatText = "HH:mm:ss";
        Assert.False(editor.HasValidationError);
        Assert.Equal("HH:mm:ss", ((ClockWidgetConfig)context.Config.Pages[0].Widgets[0]).Format);
        Assert.Equal("HH:mm:ss", clock.EffectiveFormat);
        Assert.True(context.Service.IsDirty);

        // A blank format clears the custom format so the selected preset applies again.
        editor.FormatText = "  ";
        Assert.Null(((ClockWidgetConfig)context.Config.Pages[0].Widgets[0]).Format);
        Assert.Equal("h:mm tt", clock.EffectiveFormat);
    }

    [Fact]
    public void ClockFormatOverMaxLength_IsBlockedWithFeedbackAndNoMutation()
    {
        var context = CreateContext(Clock("c1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.FormatText = new string('y', ClockWidgetConfig.MaxFormatLength + 1);

        Assert.True(editor.HasValidationError);
        Assert.Null(((ClockWidgetConfig)context.Config.Pages[0].Widgets[0]).Format);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void ImageButtonShowLabel_UpdatesConfigAndLiveWidget()
    {
        var context = CreateContext(ImageButton("ib1"));
        context.EnterSelect();
        var editor = context.Editor;
        var imageButton = Assert.IsType<ImageButtonWidgetViewModel>(context.Widgets[0]);

        Assert.True(imageButton.ShowLabel);

        editor.ShowLabel = false;

        Assert.False(((ImageButtonWidgetConfig)context.Config.Pages[0].Widgets[0]).ShowLabel);
        Assert.False(imageButton.ShowLabel);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void ImageButton_WithNoAction_IsDisabledWithReasonThroughThePicker()
    {
        var context = CreateContext(ImageButton("ib1"));
        context.EnterSelect();
        var editor = context.Editor;
        var picker = editor.ActionPicker;
        Assert.NotNull(picker);
        var imageButton = Assert.IsType<ImageButtonWidgetViewModel>(context.Widgets[0]);
        Assert.True(imageButton.IsEnabled);

        picker!.SelectedActionOption = picker.ActionTypeOptions.First(option => option.Type == ActionPickerViewModel.NoneType);

        Assert.False(imageButton.IsEnabled);
        Assert.True(picker.HasValidationError);
        Assert.False(string.IsNullOrWhiteSpace(imageButton.DisabledReason));
    }

    [Fact]
    public void BarSensorRangeAndShowValue_UpdateConfigLiveWidgetAndDirty()
    {
        var context = CreateContext(Bar("bar1"));
        context.EnterSelect();
        var editor = context.Editor;
        var bar = Assert.IsType<BarWidgetViewModel>(context.Widgets[0]);

        editor.SensorText = SensorKeys.GpuUsage;
        Assert.Equal(SensorKeys.GpuUsage, ((BarWidgetConfig)context.Config.Pages[0].Widgets[0]).Sensor);

        editor.MinimumText = "10";
        editor.MaximumText = "50";
        Assert.False(editor.HasValidationError);
        Assert.Equal(10, ((BarWidgetConfig)context.Config.Pages[0].Widgets[0]).Minimum);
        Assert.Equal(50, ((BarWidgetConfig)context.Config.Pages[0].Widgets[0]).Maximum);
        Assert.Equal(10, bar.Minimum);
        Assert.Equal(50, bar.Maximum);

        editor.ShowValue = false;
        Assert.False(((BarWidgetConfig)context.Config.Pages[0].Widgets[0]).ShowValue);
        Assert.False(bar.ShowValue);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void BarRangeEdit_BlocksAnInvalidSpanWithoutMutation()
    {
        var context = CreateContext(Bar("bar1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.MinimumText = "200"; // at or above the maximum
        Assert.True(editor.HasValidationError);
        Assert.Equal(0, ((BarWidgetConfig)context.Config.Pages[0].Widgets[0]).Minimum);
        Assert.False(context.Service.IsDirty);

        editor.MaximumText = "-5"; // at or below the minimum
        Assert.True(editor.HasValidationError);
        Assert.Equal(100, ((BarWidgetConfig)context.Config.Pages[0].Widgets[0]).Maximum);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void BarUnknownSensor_DisablesTheLiveWidgetWithReason()
    {
        var context = CreateContext(Bar("bar1"));
        context.EnterSelect();
        var editor = context.Editor;
        var bar = Assert.IsType<BarWidgetViewModel>(context.Widgets[0]);
        Assert.True(bar.IsEnabled);

        editor.SensorText = "definitely.not.a.sensor";

        Assert.Equal("definitely.not.a.sensor", ((BarWidgetConfig)context.Config.Pages[0].Widgets[0]).Sensor);
        Assert.False(bar.IsEnabled);
        Assert.False(string.IsNullOrWhiteSpace(bar.DisabledReason));
    }

    [Fact]
    public void ImageEditor_UpdatesPathStretchLiveAndDirty()
    {
        var context = CreateContext(Image("img1"));
        context.EnterSelect();
        var editor = context.Editor;
        var image = Assert.IsType<ImageWidgetViewModel>(context.Widgets[0]);

        Assert.True(editor.HasImagePath);
        Assert.True(editor.HasStretch);
        Assert.Equal(ImageStretchMode.Uniform, editor.SelectedStretch);

        // A valid absolute path is committed and the live widget reloads (the file is absent, so the
        // explicit placeholder stays; the point is the path was accepted and projected).
        var absolute = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "touchpanel-missing.png");
        editor.ImagePathText = absolute;
        Assert.False(editor.HasValidationError);
        Assert.Equal(absolute, ((ImageWidgetConfig)context.Config.Pages[0].Widgets[0]).Path);
        Assert.False(image.HasImage);

        editor.SelectedStretch = ImageStretchMode.Fill;
        Assert.Equal(ImageStretchMode.Fill, ((ImageWidgetConfig)context.Config.Pages[0].Widgets[0]).Stretch);
        Assert.Equal(System.Windows.Media.Stretch.Fill, image.Stretch);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void ImageEditor_BlocksARelativePathWithoutMutation()
    {
        var context = CreateContext(Image("img1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.ImagePathText = @"Pictures\logo.png";

        Assert.True(editor.HasValidationError);
        Assert.Null(((ImageWidgetConfig)context.Config.Pages[0].Widgets[0]).Path);
        Assert.False(context.Service.IsDirty);
    }

    // ---- User-requested addition: image-file browse + byte/rate display unit ----

    [Fact]
    public void ImageEditor_BrowseFillsTheAbsolutePathAndCommitsLive()
    {
        var absolute = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "touchpanel-picked.png");
        var picker = new FakeImageFilePicker { Result = absolute };
        var context = CreateContextWithPicker(Image("img1"), picker);
        context.EnterSelect();
        var editor = context.Editor;

        Assert.True(editor.CanBrowseImage);
        Assert.True(editor.BrowseImageCommand.CanExecute(null));

        editor.BrowseImageCommand.Execute(null);

        Assert.Equal(1, picker.CallCount);
        Assert.Equal(absolute, editor.ImagePathText);
        Assert.Equal(absolute, ((ImageWidgetConfig)context.Config.Pages[0].Widgets[0]).Path);
        Assert.False(editor.HasValidationError);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void ImageEditor_CancelledBrowseLeavesThePathAndDirtyUnchanged()
    {
        var picker = new FakeImageFilePicker { Result = null };
        var context = CreateContextWithPicker(Image("img1"), picker);
        context.EnterSelect();
        var editor = context.Editor;

        editor.BrowseImageCommand.Execute(null);

        Assert.Equal(1, picker.CallCount);
        Assert.Equal(string.Empty, editor.ImagePathText);
        Assert.Null(((ImageWidgetConfig)context.Config.Pages[0].Widgets[0]).Path);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void ImageBrowseCommand_IsDisabledWithoutAPickerOrForOtherWidgets()
    {
        var context = CreateContext(Image("img1"));
        context.EnterSelect();
        Assert.False(context.Editor.CanBrowseImage);
        Assert.False(context.Editor.BrowseImageCommand.CanExecute(null));

        var picker = new FakeImageFilePicker { Result = "x" };
        var label = CreateContextWithPicker(Label("l1"), picker);
        label.EnterSelect();
        Assert.False(label.Editor.CanBrowseImage);
    }

    [Fact]
    public void Editor_ExposesTheDisplayUnitForGaugeBarAndSparkline()
    {
        Assert.True(EditorFor(GaugeWithSensor("g1", SensorKeys.RamUsed)).HasDisplayUnit);
        Assert.True(EditorFor(Bar("bar1")).HasDisplayUnit);
        Assert.True(EditorFor(Sparkline("sp1")).HasDisplayUnit);
        Assert.False(EditorFor(Label("l1")).HasDisplayUnit);
    }

    [Fact]
    public void Editor_ExposesTheShowValueFlagForBarAndSparkline()
    {
        Assert.True(EditorFor(Bar("bar1")).HasShowValue);
        Assert.True(EditorFor(Sparkline("sp1")).HasShowValue);
        Assert.False(EditorFor(GaugeWithSensor("g1", SensorKeys.RamUsed)).HasShowValue);
        Assert.False(EditorFor(Label("l1")).HasShowValue);
    }

    [Fact]
    public void SparklineShowValueAndDisplayUnit_UpdateConfigLiveAndDirty()
    {
        var context = CreateContext(Sparkline("sp1"));
        context.EnterSelect();
        var editor = context.Editor;
        var sparkline = Assert.IsType<SparklineWidgetViewModel>(context.Widgets[0]);

        // ShowValue defaults to false and flips live.
        Assert.False(editor.ShowValue);
        editor.ShowValue = true;
        Assert.True(((SparklineWidgetConfig)context.Config.Pages[0].Widgets[0]).ShowValue);
        Assert.True(sparkline.ShowValue);
        Assert.True(context.Service.IsDirty);

        // The unit dropdown is offered and commits to the Sparkline config.
        Assert.Equal(DisplayUnit.Auto, editor.SelectedDisplayUnit.Unit);
        editor.SelectedDisplayUnit = editor.DisplayUnitOptions.First(option => option.Unit == DisplayUnit.Megabytes);
        Assert.Equal(DisplayUnit.Megabytes, ((SparklineWidgetConfig)context.Config.Pages[0].Widgets[0]).DisplayUnit);

        // Selecting the already-current unit is a no-op.
        editor.SelectedDisplayUnit = editor.DisplayUnitOptions.First(option => option.Unit == DisplayUnit.Megabytes);
        Assert.Equal(DisplayUnit.Megabytes, ((SparklineWidgetConfig)context.Config.Pages[0].Widgets[0]).DisplayUnit);
    }

    [Fact]
    public void DisplayUnit_UpdatesConfigLiveAndDirty()
    {
        var context = CreateContext(GaugeWithSensor("g1", SensorKeys.RamUsed));
        context.EnterSelect();
        var editor = context.Editor;

        Assert.Equal(DisplayUnit.Auto, editor.SelectedDisplayUnit.Unit);
        Assert.Equal(5, editor.DisplayUnitOptions.Count);

        editor.SelectedDisplayUnit = editor.DisplayUnitOptions.First(option => option.Unit == DisplayUnit.Megabytes);

        Assert.Equal(DisplayUnit.Megabytes, ((GaugeWidgetConfig)context.Config.Pages[0].Widgets[0]).DisplayUnit);
        Assert.False(editor.HasValidationError);
        Assert.True(context.Service.IsDirty);

        // Selecting the already-current unit is a no-op.
        editor.SelectedDisplayUnit = editor.DisplayUnitOptions.First(option => option.Unit == DisplayUnit.Megabytes);
        Assert.Equal(DisplayUnit.Megabytes, ((GaugeWidgetConfig)context.Config.Pages[0].Widgets[0]).DisplayUnit);
    }

    [Fact]
    public void DisplayUnit_BarSelectionUpdatesConfig()
    {
        var context = CreateContext(Bar("bar1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.SelectedDisplayUnit = editor.DisplayUnitOptions.First(option => option.Unit == DisplayUnit.Kilobytes);

        Assert.Equal(DisplayUnit.Kilobytes, ((BarWidgetConfig)context.Config.Pages[0].Widgets[0]).DisplayUnit);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void ImageButtonIcon_IsRestrictedToTheBuiltInCatalog()
    {
        var editor = EditorFor(ImageButton("ib1"));

        // The picker only offers "(none)" and the built-in catalog entries.
        Assert.All(
            editor.IconOptions,
            option => Assert.True(option.Id.Length == 0 || IconCatalog.TryGet(option.Id, out _)));
    }

    [Fact]
    public void SelectedIcon_RejectsANonCatalogIdWithoutMutation()
    {
        var context = CreateContext(ImageButton("ib1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.SelectedIcon = new IconOption(@"C:\evil.png", "\uE700", "evil");

        Assert.True(editor.HasValidationError);
        Assert.Equal("power", ((ImageButtonWidgetConfig)context.Config.Pages[0].Widgets[0]).Icon);
        Assert.False(context.Service.IsDirty);
    }

    [Fact]
    public void Palette_OffersSliderAndEveryBuiltInType()
    {
        var context = CreateContext(Label("l1"));

        var types = context.Service.PaletteItems.Select(item => item.Type).ToArray();

        Assert.Contains(WidgetTypes.Slider, types);
        Assert.Contains(WidgetTypes.Clock, types);
        Assert.Contains(WidgetTypes.ImageButton, types);
        Assert.Contains(WidgetTypes.Bar, types);
        Assert.Contains(WidgetTypes.Image, types);
        Assert.Equal(WidgetRegistry.CreateDefault().BuiltInWidgets.Count, types.Length);
    }

    [Fact]
    public async Task NewTypeFieldEdits_PersistThroughTheSingleWriterOnExit()
    {
        var context = CreateContext(Bar("bar1"));
        context.EnterSelect();
        context.Editor.MaximumText = "80";
        context.Editor.ShowValue = false;

        var result = await context.Service.ExitAsync();

        Assert.True(result.Succeeded);
        var saved = Assert.IsType<BarWidgetConfig>(context.Store.LastSavedConfig!.Pages[0].Widgets[0]);
        Assert.Equal(80, saved.Maximum);
        Assert.False(saved.ShowValue);
    }

    [Fact]
    public void ApplyCommand_IsIdempotentAndKeepsTheWidgetSelectedAndPresent()
    {
        var context = CreateContext(Label("l1"));
        context.EnterSelect();
        var editor = context.Editor;
        var widget = context.Widgets[0];
        editor.LabelText = "Hello";

        // M5-RF-531: Apply is an explicit, idempotent commit; it never removes the widget nor
        // changes the selection, whatever the field state.
        Assert.True(editor.ApplyCommand.CanExecute(null));
        editor.ApplyCommand.Execute(null);
        editor.ApplyCommand.Execute(null);

        Assert.Single(context.Page.Widgets);
        Assert.Same(widget, context.Widgets[0]);
        Assert.Same(widget, context.Service.SelectedWidget);
        Assert.True(context.Service.HasPropertyEditor);
        Assert.Equal("Hello", context.Config.Pages[0].Widgets[0].Label);
        Assert.Equal("Hello", widget.Label);
        Assert.True(context.Service.IsDirty);
        Assert.False(editor.HasValidationError);
    }

    [Fact]
    public async Task ApplyCommand_ThenExit_PersistsThroughTheSingleWriterWithoutLosingTheWidget()
    {
        var context = CreateContext(Slider("s1"));
        context.EnterSelect();
        context.Editor.MaximumText = "80";

        context.Editor.ApplyCommand.Execute(null);

        var result = await context.Service.ExitAsync();

        Assert.True(result.Succeeded);
        var saved = Assert.IsType<SliderWidgetConfig>(Assert.Single(context.Store.LastSavedConfig!.Pages[0].Widgets));
        Assert.Equal(80, saved.Maximum);
    }

    [Fact]
    public void SensorPicker_OffersOptionsForSensorTypesOnly()
    {
        var gauge = EditorFor(Gauge("g1"));
        Assert.True(gauge.HasSensor);
        Assert.NotEmpty(gauge.SensorOptions);
        Assert.Contains(gauge.SensorOptions, option => option.Key == SensorKeys.CpuTotal);
        Assert.NotNull(gauge.SelectedSensor);
        Assert.Equal(SensorKeys.CpuTotal, gauge.SelectedSensor!.Key);

        Assert.True(EditorFor(Sparkline("sp1")).HasSensor);
        Assert.True(EditorFor(Bar("bar1")).HasSensor);

        // A widget without a sensor never exposes the picker (M5-RF-513).
        var label = EditorFor(Label("l1"));
        Assert.False(label.HasSensor);
        Assert.Null(label.SelectedSensor);
    }

    [Fact]
    public void SensorPicker_SelectingAKnownKeyWritesItWithValidation()
    {
        var context = CreateContext(Gauge("g1"));
        context.EnterSelect();
        var editor = context.Editor;

        editor.SelectedSensor = editor.SensorOptions.First(option => option.Key == SensorKeys.GpuUsage);

        Assert.False(editor.HasValidationError);
        Assert.Equal(SensorKeys.GpuUsage, ((GaugeWidgetConfig)context.Config.Pages[0].Widgets[0]).Sensor);
        Assert.Equal(SensorKeys.GpuUsage, editor.SensorText);
        Assert.True(context.Service.IsDirty);
    }

    [Fact]
    public void SensorPicker_SelectsAFullyQualifiedKeyAndKeepsTheSelection()
    {
        var context = CreateContext(Gauge("g1"));
        context.EnterSelect();
        var editor = context.Editor;

        // The catalog shows each option's unit and key, so a percent key cannot be mistaken for a
        // temperature, and picking the temperature option sticks.
        var temperature = editor.SensorOptions.First(option => option.Key == SensorKeys.CpuTemperature);
        Assert.Contains("°C", temperature.DisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain("%", temperature.DisplayName, StringComparison.Ordinal);

        editor.SelectedSensor = temperature;

        Assert.False(editor.HasValidationError);
        Assert.Equal(SensorKeys.CpuTemperature, ((GaugeWidgetConfig)context.Config.Pages[0].Widgets[0]).Sensor);
        Assert.Equal(SensorKeys.CpuTemperature, editor.SelectedSensor!.Key);
        Assert.Equal(SensorKeys.CpuTemperature, editor.SensorText);
    }

    [Fact]
    public void SensorPicker_PreservesAnUnknownConfiguredKeyAndNeverClearsIt()
    {
        var context = CreateContext(GaugeWithSensor("g1", "custom.sensor"));
        context.EnterSelect();
        var editor = context.Editor;

        Assert.Contains(editor.SensorOptions, option => option.Key == "custom.sensor");
        Assert.Equal("custom.sensor", editor.SelectedSensor?.Key);

        // Choosing an unrelated known key changes it; an unknown key was never cleared implicitly.
        editor.SelectedSensor = editor.SensorOptions.First(option => option.Key == SensorKeys.RamPercent);
        Assert.Equal(SensorKeys.RamPercent, ((GaugeWidgetConfig)context.Config.Pages[0].Widgets[0]).Sensor);
    }

    [Fact]
    public void SensorPicker_WithASnapshotCatalog_IncludesPerCoreAndFanKeys()
    {
        var source = new FakeSnapshotSource();
        source.Publish(new SensorSnapshot
        {
            CapturedAt = DateTimeOffset.UnixEpoch,
            CpuCorePercents = ImmutableArray.Create<double?>(10, 20),
            FanSpeedsRpm = ImmutableArray.Create(1000d),
        });

        var config = ConfigWith(Gauge("g1"));
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(
            config,
            page,
            new RecordingConfigStore(),
            factory: registry,
            palette: registry,
            sensorCatalog: new SensorCatalog(source));
        service.Enter(1000, 500);
        service.SelectWidget(page.Widgets[0]);
        Assert.True(service.OpenPropertyEditor());

        var editor = service.PropertyEditor!;
        Assert.Contains(editor.SensorOptions, option => option.Key == SensorKeys.CpuCore(0));
        Assert.Contains(editor.SensorOptions, option => option.Key == SensorKeys.CpuCore(1));
        Assert.Contains(editor.SensorOptions, option => option.Key == SensorKeys.Fan(0));
    }

    private static PropertyEditorViewModel EditorFor(WidgetConfig widget)
    {
        var context = CreateContext(widget);
        context.EnterSelect();
        return context.Editor;
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

    private static TestContext CreateContextWithPicker(WidgetConfig widget, IImageFilePicker picker)
    {
        var config = ConfigWith(widget);
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var store = new RecordingConfigStore();
        var service = new EditModeService(
            config,
            page,
            store,
            factory: registry,
            palette: registry,
            imageFilePicker: picker);
        return new TestContext(config, page, store, service);
    }

    private static PanelConfig ConfigWith(params WidgetConfig[] widgets) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = PageId,
        Pages = [new PageConfig { Id = PageId, Widgets = [.. widgets] }],
    };

    private static LabelWidgetConfig Label(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 200,
        Height = 100,
        Label = id,
    };

    private static SliderWidgetConfig Slider(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 240,
        Height = 120,
        Minimum = 0,
        Maximum = 100,
        Step = 1,
        Value = 50,
        Orientation = SliderOrientation.Horizontal,
        Action = new VolumeActionConfig { Mode = VolumeMode.Set, Level = 50 },
        Label = id,
    };

    private static SliderWidgetConfig SliderMinimumFraction() => new()
    {
        Id = "s-frac",
        X = 0,
        Y = 0,
        Width = 240,
        Height = 120,
        Minimum = 2.5,
        Maximum = 100,
        Step = 0.5,
        Value = 25,
        Orientation = SliderOrientation.Horizontal,
        Action = new VolumeActionConfig { Mode = VolumeMode.Set, Level = 50 },
    };

    private static GaugeWidgetConfig Gauge(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 240,
        Height = 180,
        Sensor = SensorKeys.CpuTotal,
        Minimum = 0,
        Maximum = 100,
        Label = id,
    };

    private static GaugeWidgetConfig GaugeWithSensor(string id, string sensor) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 240,
        Height = 180,
        Sensor = sensor,
        Minimum = 0,
        Maximum = 100,
        Label = id,
    };

    private static SparklineWidgetConfig Sparkline(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 240,
        Height = 120,
        Sensor = SensorKeys.CpuTotal,
        WindowSeconds = SparklineWidgetConfig.DefaultWindowSeconds,
        Style = SparklineStyle.Line,
        Label = id,
    };

    private static ToggleWidgetConfig Toggle(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 160,
        Height = 120,
        InitialOn = false,
        PersistState = false,
        Action = new MuteActionConfig(),
        Label = id,
    };

    private static ClockWidgetConfig Clock(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 240,
        Height = 120,
        Mode = ClockMode.Time24,
        Label = id,
    };

    private static ImageButtonWidgetConfig ImageButton(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 120,
        Height = 120,
        ShowLabel = true,
        Icon = "power",
        Action = new MuteActionConfig(),
        Label = id,
    };

    private static BarWidgetConfig Bar(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 240,
        Height = 64,
        Sensor = SensorKeys.CpuTotal,
        Minimum = BarWidgetConfig.DefaultMinimum,
        Maximum = BarWidgetConfig.DefaultMaximum,
        ShowValue = true,
        Label = id,
    };

    private static ImageWidgetConfig Image(string id) => new()
    {
        Id = id,
        X = 0,
        Y = 0,
        Width = 240,
        Height = 160,
        Stretch = ImageStretchMode.Uniform,
        Label = id,
    };

    /// <summary>Convenience bundle for the service under test and its collaborators.</summary>
    private sealed record TestContext(
        PanelConfig Config,
        PageViewModel Page,
        RecordingConfigStore Store,
        EditModeService Service)
    {
        /// <summary>Gets the widget view models of the edited page.</summary>
        public IReadOnlyList<WidgetViewModelBase> Widgets => Page.Widgets;

        /// <summary>Gets the property editor for the first widget.</summary>
        public PropertyEditorViewModel Editor => Service.PropertyEditor!;

        /// <summary>Enters edit mode, selects the first widget, and opens its edit menu (UX fix #1).</summary>
        public void EnterSelect()
        {
            Service.Enter(1000, 500);
            Service.SelectWidget(Widgets[0]);
            Service.OpenPropertyEditor();
        }
    }
}
