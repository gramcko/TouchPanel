using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Views;
using TouchPanel.Views.Controls;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Font-size stepper UI (user-requested addition): the property panel renders the −/+ touch targets
/// beside the live Font size field, both at least 40x40 DIP, Focusable=false (so a tap keeps the
/// field's focus and live-edit session), themed like the field, bound to the editor commands with
/// accessible names, and laid out inside the 300 DIP panel at 1024x600 and 512x300. The field's
/// Up/Down keys step exactly like the buttons while Enter keeps committing.
/// </summary>
public sealed class FontSizeStepperUiTests
{
    private const string PageId = "main";

    [Fact]
    public async Task StepperButtons_AreTouchSizedThemedFocuslessAndBound()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var editorContent = CreateEditorContent(24);
            var editor = editorContent.Editor;
            var content = editorContent.Content;
            var fontSizeBox = editorContent.FontSizeBox;
            var decrease = editorContent.Decrease;
            var increase = editorContent.Increase;

            var decreaseSurface = FindDescendant<Border>(decrease, _ => true);
            var increaseSurface = FindDescendant<Border>(increase, _ => true);

            return new
            {
                DecreaseWidth = decrease.ActualWidth,
                DecreaseHeight = decrease.ActualHeight,
                IncreaseWidth = increase.ActualWidth,
                IncreaseHeight = increase.ActualHeight,
                DecreaseFocusable = decrease.Focusable,
                IncreaseFocusable = increase.Focusable,
                DecreaseTabStop = decrease.IsTabStop,
                IncreaseTabStop = increase.IsTabStop,
                DecreaseName = AutomationProperties.GetName(decrease),
                IncreaseName = AutomationProperties.GetName(increase),
                DecreaseTooltip = decrease.ToolTip as string,
                IncreaseTooltip = increase.ToolTip as string,
                DecreaseContent = decrease.Content as string,
                IncreaseContent = increase.Content as string,
                DecreaseField = (decrease as NumericStepperButton)?.Field,
                IncreaseField = (increase as NumericStepperButton)?.Field,
                DecreaseDirection = (decrease as NumericStepperButton)?.Direction ?? 0,
                IncreaseDirection = (increase as NumericStepperButton)?.Direction ?? 0,
                DecreaseEnabled = decrease.IsEnabled,
                IncreaseEnabled = increase.IsEnabled,
                FieldWidth = fontSizeBox.ActualWidth,
                FieldHeight = fontSizeBox.ActualHeight,
                FieldFocusable = fontSizeBox.Focusable,
                ThemedSurface = decreaseSurface is not null
                    && increaseSurface is not null
                    && Equals(decreaseSurface.Background, fontSizeBox.Background)
                    && Equals(increaseSurface.Background, fontSizeBox.Background),
                FieldBounds = BoundsWithin(fontSizeBox, content),
                DecreaseBounds = BoundsWithin(decrease, content),
                IncreaseBounds = BoundsWithin(increase, content),
                ContentWidth = content.ActualWidth,
            };
        });

        Assert.True(result.DecreaseWidth >= 40, $"The decrease button must be at least 40 DIP wide; was {result.DecreaseWidth}.");
        Assert.True(result.DecreaseHeight >= 40, $"The decrease button must be at least 40 DIP tall; was {result.DecreaseHeight}.");
        Assert.True(result.IncreaseWidth >= 40, $"The increase button must be at least 40 DIP wide; was {result.IncreaseWidth}.");
        Assert.True(result.IncreaseHeight >= 40, $"The increase button must be at least 40 DIP tall; was {result.IncreaseHeight}.");
        Assert.False(result.DecreaseFocusable, "Tapping the stepper must not pull keyboard focus from the field.");
        Assert.False(result.IncreaseFocusable, "Tapping the stepper must not pull keyboard focus from the field.");
        Assert.False(result.DecreaseTabStop);
        Assert.False(result.IncreaseTabStop);
        Assert.Equal("Decrease font size", result.DecreaseName);
        Assert.Equal("Increase font size", result.IncreaseName);
        Assert.Equal("Decrease font size", result.DecreaseTooltip);
        Assert.Equal("Increase font size", result.IncreaseTooltip);
        Assert.Equal("−", result.DecreaseContent);
        Assert.Equal("+", result.IncreaseContent);
        Assert.Equal("FontSizeText", result.DecreaseField);
        Assert.Equal("FontSizeText", result.IncreaseField);
        Assert.Equal(-1, result.DecreaseDirection);
        Assert.Equal(1, result.IncreaseDirection);
        Assert.True(result.DecreaseEnabled);
        Assert.True(result.IncreaseEnabled);
        Assert.True(result.FieldWidth >= 120, $"The field must take the flexible column beside the steppers; width was {result.FieldWidth}.");
        Assert.True(result.FieldHeight >= 40, $"The field must keep its 40 DIP touch target; height was {result.FieldHeight}.");
        Assert.True(result.FieldFocusable, "The field must stay focusable so its live-edit session can start.");
        Assert.True(result.ThemedSurface, "The stepper surfaces must use the same themed background as the property fields.");

        // One row: field, then −, then +, flush with no gap and inside the panel content.
        Assert.True(
            result.FieldBounds.Right <= result.DecreaseBounds.Left + 0.5,
            $"The field {result.FieldBounds} must not overlap the stepper {result.DecreaseBounds}.");
        Assert.True(
            result.DecreaseBounds.Right <= result.IncreaseBounds.Left + 0.5,
            $"The − button {result.DecreaseBounds} must sit left of the + button {result.IncreaseBounds}.");
        Assert.True(
            Math.Abs(result.DecreaseBounds.Top - result.FieldBounds.Top) <= 0.5
            && Math.Abs(result.IncreaseBounds.Top - result.FieldBounds.Top) <= 0.5,
            "The field and both steppers must share one 40 DIP row.");
        Assert.True(
            result.IncreaseBounds.Right <= result.ContentWidth + 0.5,
            $"The stepper pair {result.IncreaseBounds} must stay inside the property panel content (width {result.ContentWidth}).");
    }

    [Fact]
    public async Task StepperTap_AppliesLiveAndKeepsTheSessionAndItsUndoGroup()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var editorContent = CreateEditorContent(24);
            var editor = editorContent.Editor;
            var decrease = editorContent.Decrease;
            var increase = editorContent.Increase;

            // Simulate the field being focused (its GotFocus handler begins the live session).
            editor.BeginLiveEdit();
            Assert.True(editor.IsLiveEditingFontSize);

            Tap(decrease);
            var afterDecrease = (editor.FontSizeText, editor.IsLiveEditingFontSize, editorContent.Widget.FontSize);

            Tap(increase);
            Tap(increase);

            return new
            {
                AfterDecrease = afterDecrease,
                FinalText = editor.FontSizeText,
                FinalSession = editor.IsLiveEditingFontSize,
                FinalWidget = editorContent.Widget.FontSize,
                UndoDepth = editorContent.Service.UndoDepth,
            };
        });

        Assert.Equal(("23", true, (double?)23), result.AfterDecrease);
        Assert.Equal("25", result.FinalText);
        Assert.True(result.FinalSession, "Tapping a stepper must not end the field's live-edit session.");
        Assert.Equal(25, result.FinalWidget);
        Assert.Equal(1, result.UndoDepth);
    }

    [Fact]
    public async Task StepperButtons_DisableAtTheBoundsAndTheFieldTextStaysCanonical()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var editorContent = CreateEditorContent(24);
            var editor = editorContent.Editor;
            var fontSizeBox = editorContent.FontSizeBox;
            var decrease = editorContent.Decrease;
            var increase = editorContent.Increase;

            editor.BeginLiveEdit();

            editor.FontSizeText = "96";
            var atMax = (increase.IsEnabled, decrease.IsEnabled, editor.FontSizeText, fontSizeBox.Text);

            editor.FontSizeText = "8";
            var atMin = (increase.IsEnabled, decrease.IsEnabled, editor.FontSizeText, fontSizeBox.Text);

            return new
            {
                AtMax = atMax,
                AtMin = atMin,
                DecreaseFocusable = decrease.Focusable,
                IncreaseFocusable = increase.Focusable,
            };
        });

        Assert.False(result.AtMax.Item1, "Increase must be disabled at 96.");
        Assert.True(result.AtMax.Item2);
        Assert.Equal("96", result.AtMax.Item3);
        Assert.Equal("96", result.AtMax.Item4);
        Assert.True(result.AtMin.Item1);
        Assert.False(result.AtMin.Item2, "Decrease must be disabled at 8.");
        Assert.Equal("8", result.AtMin.Item3);
        Assert.Equal("8", result.AtMin.Item4);
        Assert.False(result.DecreaseFocusable);
        Assert.False(result.IncreaseFocusable);
    }

    [Fact]
    public async Task FontSizeField_UpDownStepAndEnterStillCommits()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var editorContent = CreateEditorContent(24);
            var editor = editorContent.Editor;
            var fontSizeBox = editorContent.FontSizeBox;

            var parameters = new HwndSourceParameters("TouchPanel.FontSizeStepperKeyTest")
            {
                Width = 1,
                Height = 1,
                WindowStyle = 0,
            };

            using var source = new HwndSource(parameters);

            KeyEventArgs CreateKey(RoutedEvent routedEvent, Key key) => new(Keyboard.PrimaryDevice, source, 0, key)
            {
                RoutedEvent = routedEvent,
            };

            editor.BeginLiveEdit();

            var up = CreateKey(UIElement.PreviewKeyDownEvent, Key.Up);
            fontSizeBox.RaiseEvent(up);
            var afterUp = (editor.FontSizeText, up.Handled);

            var down = CreateKey(UIElement.PreviewKeyDownEvent, Key.Down);
            fontSizeBox.RaiseEvent(down);
            var afterDown = (editor.FontSizeText, down.Handled);

            // At the upper bound the key is ignored (unhandled, no movement).
            editor.FontSizeText = "96";
            var upAtMax = CreateKey(UIElement.PreviewKeyDownEvent, Key.Up);
            fontSizeBox.RaiseEvent(upAtMax);
            var atMax = (editor.FontSizeText, upAtMax.Handled);

            // Enter still commits and normalizes a valid value, then starts a new session.
            editor.FontSizeText = "120";
            var enter = CreateKey(UIElement.KeyDownEvent, Key.Enter);
            fontSizeBox.RaiseEvent(enter);
            var afterEnter = (editor.FontSizeText, enter.Handled, editor.IsLiveEditingFontSize);

            // Enter with invalid text restores the last valid text as before.
            editor.FontSizeText = "abc";
            var badEnter = CreateKey(UIElement.KeyDownEvent, Key.Enter);
            fontSizeBox.RaiseEvent(badEnter);
            var afterBadEnter = (editor.FontSizeText, editor.HasValidationError);

            return new
            {
                AfterUp = afterUp,
                AfterDown = afterDown,
                AtMax = atMax,
                AfterEnter = afterEnter,
                AfterBadEnter = afterBadEnter,
            };
        });

        Assert.Equal(("25", true), result.AfterUp);        Assert.Equal(("24", true), result.AfterDown);
        Assert.Equal(("96", false), result.AtMax);
        Assert.Equal(("96", true, true), result.AfterEnter);
        Assert.Equal("96", result.AfterBadEnter.Item1);
        Assert.True(result.AfterBadEnter.Item2);
    }

    [Fact]
    public async Task StepperButtons_StayInsideThePropertyPanelAtBothPanelSizes()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var overlay = new EditOverlay();
            var config = ConfigWithLabel(24);
            var registry = WidgetRegistry.CreateDefault();
            var page = PageViewModel.CreateForActivePage(config, registry);
            var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
            overlay.DataContext = service;
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            service.Enter(1024, 600);
            service.SelectWidget(page.Widgets[0]);
            service.OpenPropertyEditor();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
            var host = new Grid();
            host.Children.Add(overlay);

            var measurements = new List<(
                string Label,
                bool ButtonsExist,
                double DecreaseSize,
                double IncreaseSize,
                double FieldWidth,
                bool ButtonsInsidePanel,
                bool FieldLeftOfButtons,
                bool ButtonsAdjacentInOneRow,
                bool Hittable,
                Rect PanelBounds,
                Rect DecreaseBounds,
                Rect IncreaseBounds,
                double ViewportHeight,
                double VerticalOffset,
                double ExtentHeight)>();

            foreach (var (label, width, height) in new[]
            {
                ("1024x600", 1024d, 600d),
                ("512x300", 512d, 300d),
            })
            {
                host.Width = width;
                host.Height = height;
                host.Measure(new Size(width, height));
                host.Arrange(new Rect(0, 0, width, height));
                host.UpdateLayout();
                overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                host.UpdateLayout();

                var decrease = FindDescendant<Button>(panel, button => button.Name == "FontSizeDecreaseButton");
                var increase = FindDescendant<Button>(panel, button => button.Name == "FontSizeIncreaseButton");
                var fontSizeBox = FindDescendant<TextBox>(panel, box => box.Name == "FontSizeBox");

                if (decrease is not null)
                {
                    ScrollFontSizeRowIntoView(panel, decrease);
                    overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                    host.UpdateLayout();
                }

                var panelBounds = BoundsWithin(panel, overlay);
                var decreaseBounds = decrease is null ? Rect.Empty : BoundsWithin(decrease, overlay);
                var increaseBounds = increase is null ? Rect.Empty : BoundsWithin(increase, overlay);
                var fieldBounds = fontSizeBox is null ? Rect.Empty : BoundsWithin(fontSizeBox, overlay);

                var buttonsInsidePanel = decrease is not null
                    && increase is not null
                    && decreaseBounds.Left >= panelBounds.Left - 0.5
                    && decreaseBounds.Right <= panelBounds.Right + 0.5
                    && increaseBounds.Left >= panelBounds.Left - 0.5
                    && increaseBounds.Right <= panelBounds.Right + 0.5;

                var propertyScroll = FindDescendant<ScrollViewer>(panel, viewer => viewer.Name == "PropertyPanelScroll");

                measurements.Add((
                    label,
                    decrease is not null && increase is not null && fontSizeBox is not null,
                    decrease?.ActualWidth ?? 0,
                    increase?.ActualWidth ?? 0,
                    fontSizeBox?.ActualWidth ?? 0,
                    buttonsInsidePanel,
                    fieldBounds.Right <= decreaseBounds.Left + 0.5,
                    decreaseBounds.Right <= increaseBounds.Left + 0.5
                    && Math.Abs(decreaseBounds.Top - fieldBounds.Top) <= 0.5
                    && Math.Abs(increaseBounds.Top - fieldBounds.Top) <= 0.5,
                    decrease is not null && increase is not null && IsHittable(overlay, decrease) && IsHittable(overlay, increase),
                    panelBounds,
                    decreaseBounds,
                    increaseBounds,
                    propertyScroll?.ViewportHeight ?? 0,
                    propertyScroll?.VerticalOffset ?? 0,
                    propertyScroll?.ExtentHeight ?? 0));
            }

            return (measurements, panelVisibility: panel.Visibility);
        });

        Assert.Equal(Visibility.Visible, result.panelVisibility);

        foreach (var measurement in result.measurements)
        {
            Assert.True(measurement.ButtonsExist, $"[{measurement.Label}] Both stepper buttons and the Font size field must be rendered.");
            Assert.True(measurement.DecreaseSize >= 40, $"[{measurement.Label}] The − button must be at least 40 DIP; was {measurement.DecreaseSize}.");
            Assert.True(measurement.IncreaseSize >= 40, $"[{measurement.Label}] The + button must be at least 40 DIP; was {measurement.IncreaseSize}.");
            Assert.True(measurement.FieldWidth >= 120, $"[{measurement.Label}] The field must take the flexible column; width was {measurement.FieldWidth}.");
            Assert.True(measurement.ButtonsInsidePanel, $"[{measurement.Label}] The steppers must stay inside the 300 DIP property panel.");
            Assert.True(measurement.FieldLeftOfButtons, $"[{measurement.Label}] The steppers must not overlap the field.");
            Assert.True(measurement.ButtonsAdjacentInOneRow, $"[{measurement.Label}] The − and + steppers must sit adjacent in the field's row.");

            // At 1024x600 the property panel has room for the row, so both steppers must be tappable.
            // At 512x300 (the same panel at 200% scaling) the pre-existing edit toolbar consumes the
            // row and leaves the property panel only a few DIP tall, so only the horizontal layout is
            // assertable; the row still lives inside the 300 DIP column and scrolls with the panel.
            if (measurement.Label == "1024x600")
            {
                Assert.True(
                    measurement.Hittable,
                    $"[{measurement.Label}] Both steppers must be hit-testable (panel={measurement.PanelBounds}, decrease={measurement.DecreaseBounds}, increase={measurement.IncreaseBounds}).");
            }
        }
    }

    /// <summary>
    /// Renders the overlay and its property panel (the real BAML, including the merged theme
    /// resources) at 1024x600, opens the editor, scrolls the Font size row into view, and returns the
    /// live field and stepper elements for interaction checks.
    /// </summary>
    private static EditorContent CreateEditorContent(double fontSize)
    {
        var config = ConfigWithLabel(fontSize);
        var registry = WidgetRegistry.CreateDefault();
        var page = PageViewModel.CreateForActivePage(config, registry);
        var service = new EditModeService(config, page, new RecordingConfigStore(), factory: registry, palette: registry);
        var overlay = new EditOverlay { DataContext = service };
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

        service.Enter(1024, 600);
        service.SelectWidget(page.Widgets[0]);
        service.OpenPropertyEditor();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

        var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
        var host = new Grid { Width = 1024, Height = 600 };
        host.Children.Add(overlay);
        host.Measure(new Size(1024, 600));
        host.Arrange(new Rect(0, 0, 1024, 600));
        host.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        host.UpdateLayout();

        var fontSizeBox = FindDescendant<TextBox>(panel, box => box.Name == "FontSizeBox");
        var decrease = FindDescendant<Button>(panel, button => button.Name == "FontSizeDecreaseButton");
        var increase = FindDescendant<Button>(panel, button => button.Name == "FontSizeIncreaseButton");

        Assert.NotNull(fontSizeBox);
        Assert.NotNull(decrease);
        Assert.NotNull(increase);

        ScrollFontSizeRowIntoView(panel, decrease!);
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        host.UpdateLayout();

        return new EditorContent(service.PropertyEditor!, page.Widgets[0], service, panel, fontSizeBox!, decrease!, increase!);
    }

    /// <summary>Simulates a touch tap on a stepper through the automation invoke pattern (runs the bound command).</summary>
    private static void Tap(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        var invoke = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!;
        invoke.Invoke();

        // The automation invoke is dispatched asynchronously at Input priority; pump it so the
        // assertions observe the click's effect.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    /// <summary>Scrolls the property editor so the Font size row is inside the panel viewport.</summary>
    private static void ScrollFontSizeRowIntoView(Border panel, FrameworkElement anchor)
    {
        var scroll = FindDescendant<ScrollViewer>(panel, viewer => viewer.Name == "PropertyPanelScroll");
        if (scroll?.Content is not Visual content)
        {
            return;
        }

        var top = anchor.TransformToAncestor(content).Transform(new Point(0, 0)).Y;
        scroll.ScrollToVerticalOffset(Math.Max(0, top - 8));
        scroll.UpdateLayout();
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        if (root is T match && predicate(match))
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindDescendant(VisualTreeHelper.GetChild(root, i), predicate) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static Rect BoundsWithin(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHittable(Visual root, FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        var center = element.TransformToAncestor(root)
            .Transform(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        var hit = VisualTreeHelper.HitTest(root, center)?.VisualHit;
        return hit is not null && IsWithin(hit, element);
    }

    private static PanelConfig ConfigWithLabel(double fontSize) => new()
    {
        SchemaVersion = PanelConfig.CurrentSchemaVersion,
        ActivePageId = PageId,
        Pages =
        [
            new PageConfig
            {
                Id = PageId,
                Widgets =
                [
                    new LabelWidgetConfig
                    {
                        Id = "l1",
                        X = 0,
                        Y = 0,
                        Width = 200,
                        Height = 100,
                        Label = "l1",
                        FontSize = fontSize,
                    },
                ],
            },
        ],
    };

    private sealed record EditorContent(
        PropertyEditorViewModel Editor,
        WidgetViewModelBase Widget,
        EditModeService Service,
        FrameworkElement Content,
        TextBox FontSizeBox,
        Button Decrease,
        Button Increase);
}
