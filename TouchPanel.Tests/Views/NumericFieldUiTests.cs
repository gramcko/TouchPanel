using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Monitoring;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.ViewModels.Edit;
using TouchPanel.Views;
using TouchPanel.Views.Controls;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Generalized numeric stepper UI (user-requested addition): every listed numeric field renders the
/// field in a flexible column with two integrated 40x40 steppers flush to its right, all in one row;
/// the pair stays inside the 300 DIP property panel at 1024x600 and 512x300; the buttons disable at
/// the relevant bound; a quick tap applies exactly one step; and the numeric input behavior rejects
/// non-numeric typing without touching the bound value.
/// </summary>
public sealed class NumericFieldUiTests
{
    private const string PageId = "main";

    [Fact]
    public async Task EveryNumericFamily_RendersTouchSizedSteppersInOneIntegratedRow()
    {
        using var sta = new StaThread();

        var failures = await sta.InvokeAsync(() =>
        {
            var results = new List<string>();

            foreach (var family in Families())
            {
                var context = Open(family.Widget, 1024, 600);
                foreach (var row in family.Rows)
                {
                    var box = FindDescendant<TextBox>(context.Panel, element => element.Name == row.BoxName);
                    var decrease = FindDescendant<NumericStepperButton>(context.Panel, element => element.Name == row.DecreaseName);
                    var increase = FindDescendant<NumericStepperButton>(context.Panel, element => element.Name == row.IncreaseName);

                    if (box is null || decrease is null || increase is null)
                    {
                        results.Add($"{family.Name}/{row.Field}: missing field or stepper (box={box is not null}, −={decrease is not null}, +={increase is not null}).");
                        continue;
                    }

                    ScrollIntoView(context.Panel, decrease);
                    Pump(context.Overlay);

                    var fieldBounds = BoundsWithin(box, context.Content);
                    var decreaseBounds = BoundsWithin(decrease, context.Content);
                    var increaseBounds = BoundsWithin(increase, context.Content);
                    var grid = VisualTreeHelper.GetParent(box) as Grid;

                    if (box.ActualWidth < 120)
                    {
                        results.Add($"{family.Name}/{row.Field}: field width {box.ActualWidth:0.#} is too small for the flexible column.");
                    }

                    if (box.ActualHeight < 40 || decrease.ActualHeight < 40 || increase.ActualHeight < 40)
                    {
                        results.Add($"{family.Name}/{row.Field}: 40 DIP touch target lost (field={box.ActualHeight:0.#}, −={decrease.ActualHeight:0.#}, +={increase.ActualHeight:0.#}).");
                    }

                    if (decrease.ActualWidth < 40 || increase.ActualWidth < 40)
                    {
                        results.Add($"{family.Name}/{row.Field}: stepper is narrower than 40 DIP (−={decrease.ActualWidth:0.#}, +={increase.ActualWidth:0.#}).");
                    }

                    if (grid is null || Grid.GetColumn(box) != 0 || Grid.GetColumn(decrease) != 1 || Grid.GetColumn(increase) != 2)
                    {
                        results.Add($"{family.Name}/{row.Field}: field/steppers are not in columns 0/1/2.");
                    }
                    else if (grid.ColumnDefinitions.Count < 3 || !grid.ColumnDefinitions[0].Width.IsStar)
                    {
                        results.Add($"{family.Name}/{row.Field}: the field column is not a Star column.");
                    }

                    if (fieldBounds.Right > decreaseBounds.Left + 0.5
                        || decreaseBounds.Right > increaseBounds.Left + 0.5)
                    {
                        results.Add($"{family.Name}/{row.Field}: field/steppers overlap ({fieldBounds}, {decreaseBounds}, {increaseBounds}).");
                    }

                    if (Math.Abs(decreaseBounds.Top - fieldBounds.Top) > 0.5
                        || Math.Abs(increaseBounds.Top - fieldBounds.Top) > 0.5
                        || Math.Abs(decreaseBounds.Bottom - fieldBounds.Bottom) > 0.5)
                    {
                        results.Add($"{family.Name}/{row.Field}: field and steppers are not one 40 DIP row.");
                    }

                    if (increaseBounds.Right > context.Content.ActualWidth + 0.5)
                    {
                        results.Add($"{family.Name}/{row.Field}: steppers clip outside the panel content ({increaseBounds.Right:0.#} > {context.Content.ActualWidth:0.#}).");
                    }

                    if (decrease.Field != row.Field || increase.Field != row.Field
                        || decrease.Direction != -1 || increase.Direction != 1)
                    {
                        results.Add($"{family.Name}/{row.Field}: stepper field/direction is wrong (−={decrease.Field}/{decrease.Direction}, +={increase.Field}/{increase.Direction}).");
                    }

                    if (decrease.Focusable || increase.Focusable || decrease.IsTabStop || increase.IsTabStop)
                    {
                        results.Add($"{family.Name}/{row.Field}: steppers must not steal focus or tab stops.");
                    }

                    var decreaseSurface = FindDescendant<Border>(decrease, _ => true);
                    var increaseSurface = FindDescendant<Border>(increase, _ => true);
                    if (decreaseSurface is null || increaseSurface is null
                        || !Equals(decreaseSurface.Background, box.Background)
                        || !Equals(increaseSurface.Background, box.Background))
                    {
                        results.Add($"{family.Name}/{row.Field}: stepper surface is not the field's themed surface.");
                    }

                    if (decreaseSurface is not null && decreaseSurface.BorderThickness.Left > 0.1)
                    {
                        results.Add($"{family.Name}/{row.Field}: the − button has a double left border against the field.");
                    }

                    if (!AutomationProperties.GetName(decrease).StartsWith("Decrease", StringComparison.Ordinal)
                        || !AutomationProperties.GetName(increase).StartsWith("Increase", StringComparison.Ordinal))
                    {
                        results.Add($"{family.Name}/{row.Field}: missing accessible stepper names.");
                    }
                }
            }

            return results;
        });

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task NumericRows_StayInsideThePanelAt1024x600And512x300()
    {
        using var sta = new StaThread();

        var results = await sta.InvokeAsync(() =>
        {
            var failures = new List<string>();
            var rows = Families()
                .SelectMany(family => family.Rows.Select(row => (family.Name, Row: row, family.Widget)))
                .ToArray();

            foreach (var (width, height, label) in new[]
            {
                (1024d, 600d, "1024x600"),
                (512d, 300d, "512x300"),
            })
            {
                foreach (var (familyName, row, widget) in rows)
                {
                    var context = Open(widget, width, height);
                    var box = FindDescendant<TextBox>(context.Panel, element => element.Name == row.BoxName);
                    var decrease = FindDescendant<NumericStepperButton>(context.Panel, element => element.Name == row.DecreaseName);
                    var increase = FindDescendant<NumericStepperButton>(context.Panel, element => element.Name == row.IncreaseName);
                    if (box is null || decrease is null || increase is null)
                    {
                        failures.Add($"[{label}] {familyName}/{row.Field}: missing field or stepper.");
                        continue;
                    }

                    ScrollIntoView(context.Panel, decrease);
                    Pump(context.Overlay);

                    var panelBounds = BoundsWithin(context.Panel, context.Overlay);
                    var fieldBounds = BoundsWithin(box, context.Content);
                    var decreaseBounds = BoundsWithin(decrease, context.Content);
                    var increaseBounds = BoundsWithin(increase, context.Content);

                    if (decreaseBounds.Left < -0.5 || decreaseBounds.Right > context.Content.ActualWidth + 0.5
                        || increaseBounds.Left < -0.5 || increaseBounds.Right > context.Content.ActualWidth + 0.5)
                    {
                        failures.Add($"[{label}] {familyName}/{row.Field}: steppers outside the {panelBounds.Width:0.#} DIP panel content.");
                    }

                    if (fieldBounds.Right > decreaseBounds.Left + 0.5 || decreaseBounds.Right > increaseBounds.Left + 0.5)
                    {
                        failures.Add($"[{label}] {familyName}/{row.Field}: horizontal overlap.");
                    }
                }
            }

            return failures;
        });

        Assert.True(results.Count == 0, string.Join(Environment.NewLine, results));
    }

    [Fact]
    public async Task Buttons_DisableAtTheFieldBounds()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var label = Open(Label(96), 1024, 600);
            var editor = label.Editor;
            var fontDecrease = FindDescendant<NumericStepperButton>(label.Panel, b => b.Name == "FontSizeDecreaseButton")!;
            var fontIncrease = FindDescendant<NumericStepperButton>(label.Panel, b => b.Name == "FontSizeIncreaseButton")!;
            var opacityDecrease = FindDescendant<NumericStepperButton>(label.Panel, b => b.Name == "OpacityDecreaseButton")!;
            var opacityIncrease = FindDescendant<NumericStepperButton>(label.Panel, b => b.Name == "OpacityIncreaseButton")!;
            var xDecrease = FindDescendant<NumericStepperButton>(label.Panel, b => b.Name == "XDecreaseButton")!;
            var xIncrease = FindDescendant<NumericStepperButton>(label.Panel, b => b.Name == "XIncreaseButton")!;

            var fontAtMax = (fontDecrease.IsEnabled, fontIncrease.IsEnabled);
            editor.FontSizeText = "8";
            var fontAtMin = (fontDecrease.IsEnabled, fontIncrease.IsEnabled);

            editor.OpacityText = "1";
            var opacityAtMax = (opacityDecrease.IsEnabled, opacityIncrease.IsEnabled);
            editor.OpacityText = "0";
            var opacityAtMin = (opacityDecrease.IsEnabled, opacityIncrease.IsEnabled);

            editor.XText = "0";
            var xAtMin = (xDecrease.IsEnabled, xIncrease.IsEnabled);
            editor.XText = "824";
            var xAtMax = (xDecrease.IsEnabled, xIncrease.IsEnabled);

            var button = Open(
                new ButtonWidgetConfig
                {
                    Id = "b1",
                    X = 0,
                    Y = 0,
                    Width = 200,
                    Height = 100,
                    Action = new VolumeActionConfig { Mode = VolumeMode.Set, Level = 50 },
                },
                1024,
                600);
            var levelDecrease = FindDescendant<NumericStepperButton>(button.Panel, b => b.Name == "ActionVolumeLevelDecreaseButton")!;
            var levelIncrease = FindDescendant<NumericStepperButton>(button.Panel, b => b.Name == "ActionVolumeLevelIncreaseButton")!;
            button.Editor.ActionPicker!.LevelText = "100";
            var levelAtMax = (levelDecrease.IsEnabled, levelIncrease.IsEnabled);
            button.Editor.ActionPicker.LevelText = "0";
            var levelAtMin = (levelDecrease.IsEnabled, levelIncrease.IsEnabled);

            return (fontAtMax, fontAtMin, opacityAtMax, opacityAtMin, xAtMin, xAtMax, levelAtMax, levelAtMin);
        });

        Assert.Equal((true, false), result.fontAtMax);
        Assert.Equal((false, true), result.fontAtMin);
        Assert.Equal((true, false), result.opacityAtMax);
        Assert.Equal((false, true), result.opacityAtMin);
        Assert.Equal((false, true), result.xAtMin);
        Assert.Equal((true, false), result.xAtMax);
        Assert.Equal((true, false), result.levelAtMax);
        Assert.Equal((false, true), result.levelAtMin);
    }

    [Fact]
    public async Task QuickTap_AppliesExactlyOneStep()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var label = Label(24);
            label.Opacity = 0.5;
            var context = Open(label, 1024, 600);
            var editor = context.Editor;
            var increase = FindDescendant<NumericStepperButton>(context.Panel, b => b.Name == "OpacityIncreaseButton")!;

            Tap(increase);
            var afterFirst = (editor.OpacityText, context.Widget.Opacity, context.Service.UndoDepth);

            Tap(increase);
            var afterSecond = (editor.OpacityText, context.Widget.Opacity, context.Service.UndoDepth);

            return (afterFirst, afterSecond);
        });

        Assert.Equal(("0.6", (double?)0.6, 1), result.afterFirst);
        Assert.Equal(("0.7", (double?)0.7, 2), result.afterSecond);
    }

    [Fact]
    public async Task Typing_RejectsLettersAtTheFieldWithoutChangingTheValue()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = Open(Label(24), 1024, 600);
            var editor = context.Editor;
            var xBox = FindDescendant<TextBox>(context.Panel, b => b.Name == "XBox")!;
            var opacityBox = FindDescendant<TextBox>(context.Panel, b => b.Name == "OpacityBox")!;

            var letter = Type(xBox, "a");
            var digit = Type(xBox, "1");
            var opacityLetter = Type(opacityBox, "x");
            var opacityDot = Type(opacityBox, ",");

            return new
            {
                LetterHandled = letter.Handled,
                DigitHandled = digit.Handled,
                OpacityLetterHandled = opacityLetter.Handled,
                OpacityDotHandled = opacityDot.Handled,
                EditorX = editor.XText,
                EditorOpacity = editor.OpacityText,
                WidgetX = context.Widget.X,
            };
        });

        Assert.True(result.LetterHandled, "A letter typed into the integer X field must be rejected.");
        Assert.False(result.DigitHandled, "A digit typed into the integer X field must be accepted.");
        Assert.True(result.OpacityLetterHandled, "A letter typed into the opacity field must be rejected.");
        Assert.False(result.OpacityDotHandled, "One decimal separator typed into the opacity field must be accepted.");
        Assert.Equal("0", result.EditorX);
        Assert.Equal(0, result.WidgetX);
    }

    private static TextCompositionEventArgs Type(TextBox box, string text)
    {
        var composition = new TextComposition(InputManager.Current, box, text);
        var args = new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice, composition)
        {
            RoutedEvent = UIElement.PreviewTextInputEvent,
        };
        box.RaiseEvent(args);
        return args;
    }

    private static IReadOnlyList<Family> Families() =>
    [
        new("Label", Label(24),
        [
            new("FontSizeText", "FontSizeBox", "FontSizeDecreaseButton", "FontSizeIncreaseButton"),
            new("OpacityText", "OpacityBox", "OpacityDecreaseButton", "OpacityIncreaseButton"),
            new("XText", "XBox", "XDecreaseButton", "XIncreaseButton"),
            new("YText", "YBox", "YDecreaseButton", "YIncreaseButton"),
            new("WidthText", "WidthBox", "WidthDecreaseButton", "WidthIncreaseButton"),
            new("HeightText", "HeightBox", "HeightDecreaseButton", "HeightIncreaseButton"),
        ]),
        new("Gauge", new GaugeWidgetConfig
        {
            Id = "g1",
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Sensor = SensorKeys.CpuTotal,
            Minimum = 0,
            Maximum = 100,
        },
        [
            new("MinimumText", "MinBox", "MinDecreaseButton", "MinIncreaseButton"),
            new("MaximumText", "MaxBox", "MaxDecreaseButton", "MaxIncreaseButton"),
        ]),
        new("Slider", new SliderWidgetConfig
        {
            Id = "s1",
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Minimum = 0,
            Maximum = 100,
            Step = 1,
            Value = 50,
        },
        [
            new("StepText", "StepBox", "StepDecreaseButton", "StepIncreaseButton"),
            new("InitialValueText", "InitialValueBox", "InitialValueDecreaseButton", "InitialValueIncreaseButton"),
        ]),
        new("Sparkline", new SparklineWidgetConfig
        {
            Id = "sp1",
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Sensor = SensorKeys.CpuTotal,
            WindowSeconds = 60,
        },
        [
            new("WindowText", "WindowBox", "WindowDecreaseButton", "WindowIncreaseButton"),
        ]),
        new("Button", new ButtonWidgetConfig
        {
            Id = "b1",
            X = 0,
            Y = 0,
            Width = 200,
            Height = 100,
            Action = new VolumeActionConfig { Mode = VolumeMode.Set, Level = 50 },
        },
        [
            new("LevelText", "ActionVolumeLevelBox", "ActionVolumeLevelDecreaseButton", "ActionVolumeLevelIncreaseButton"),
        ]),
    ];

    private static UiContext Open(WidgetConfig widget, double width, double height)
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
        var overlay = new EditOverlay { DataContext = service };
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

        service.Enter(1024, 600);
        service.SelectWidget(page.Widgets[0]);
        service.OpenPropertyEditor();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

        var panel = Assert.IsType<Border>(overlay.FindName("PropertyPanel"));
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(overlay);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        host.UpdateLayout();

        var scroll = FindDescendant<ScrollViewer>(panel, viewer => viewer.Name == "PropertyPanelScroll");
        Assert.NotNull(scroll);
        var content = Assert.IsType<StackPanel>(scroll!.Content);

        return new UiContext(overlay, panel, content, page.Widgets[0], service, service.PropertyEditor!);
    }

    private static void Tap(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        var invoke = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!;
        invoke.Invoke();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    private static void ScrollIntoView(Border panel, FrameworkElement anchor)
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

    private static void Pump(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        element.UpdateLayout();
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

    private static LabelWidgetConfig Label(double fontSize) => new()
    {
        Id = "l1",
        X = 0,
        Y = 0,
        Width = 200,
        Height = 100,
        Label = "l1",
        FontSize = fontSize,
    };

    private sealed record Row(string Field, string BoxName, string DecreaseName, string IncreaseName);

    private sealed record Family(string Name, WidgetConfig Widget, IReadOnlyList<Row> Rows);

    private sealed record UiContext(
        EditOverlay Overlay,
        Border Panel,
        StackPanel Content,
        WidgetViewModelBase Widget,
        EditModeService Service,
        PropertyEditorViewModel Editor);
}
