using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TouchPanel.Config;
using TouchPanel.Services.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels.Settings;
using TouchPanel.Views.Settings;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Touch pages-management surface (user-requested pages management): the view binds the page rows,
/// the add/rename/remove controls meet the 40 DIP touch minimum, and the inline remove confirmation
/// is shown without a modal window. XAML is instantiated on an STA thread but never shown.
/// </summary>
public sealed class PagesEditorViewTests
{
    [Fact]
    public async Task PagesEditor_BindsRowsAndMeetsTheTouchMinimum()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateContext();
            var view = new PagesEditorView { DataContext = context.ViewModel };
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var items = Assert.IsType<ItemsControl>(view.FindName("PageItems"));
            var add = Assert.IsType<Button>(view.FindName("AddPageButton"));
            var rename = Assert.IsType<Button>(view.FindName("RenamePageButton"));
            var newName = Assert.IsType<TextBox>(view.FindName("NewPageNameBox"));
            var status = Assert.IsType<TextBlock>(view.FindName("PagesStatusText"));
            var confirmation = Assert.IsType<Border>(view.FindName("RemoveConfirmation"));
            var moveUp = Assert.IsType<Button>(view.FindName("MovePageUpButton"));
            var moveDown = Assert.IsType<Button>(view.FindName("MovePageDownButton"));
            var dropIndicator = Assert.IsType<Border>(view.FindName("PageDropIndicator"));
            var buttonStyle = Assert.IsType<Style>(view.Resources["PagesButtonStyle"]);
            var rowStyle = Assert.IsType<Style>(view.Resources["PagesRowContainerStyle"]);

            return new
            {
                ItemCount = items.Items.Count,
                AddHeight = add.MinHeight,
                RenameHeight = rename.MinHeight,
                NewNameHeight = newName.MinHeight,
                MoveUpHeight = moveUp.MinHeight,
                MoveDownHeight = moveDown.MinHeight,
                StyleHeight = ReadDoubleSetter(buttonStyle, FrameworkElement.MinHeightProperty),
                RowStyleHeight = ReadDoubleSetter(rowStyle, FrameworkElement.MinHeightProperty),
                StatusVisibility = status.Visibility,
                ConfirmationVisibility = confirmation.Visibility,
                DropIndicatorVisibility = dropIndicator.Visibility,
                DragEnabled = view.IsReorderDragEnabled,
            };
        });

        Assert.Equal(2, result.ItemCount);
        Assert.True(result.AddHeight >= 40);
        Assert.True(result.RenameHeight >= 40);
        Assert.True(result.NewNameHeight >= 40);
        Assert.True(result.MoveUpHeight >= 40);
        Assert.True(result.MoveDownHeight >= 40);
        Assert.True(result.StyleHeight >= 40);
        Assert.True(result.RowStyleHeight >= 40);
        Assert.Equal(Visibility.Collapsed, result.StatusVisibility);
        Assert.Equal(Visibility.Collapsed, result.ConfirmationVisibility);
        Assert.Equal(Visibility.Collapsed, result.DropIndicatorVisibility);
        Assert.True(result.DragEnabled);
    }

    [Fact]
    public async Task PagesEditor_MoveButtonsReorderTheSelectedPageThroughTheViewModel()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateContext();
            var view = new PagesEditorView { DataContext = context.ViewModel };
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            context.ViewModel.SelectPage("main");
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            var up = Assert.IsType<Button>(view.FindName("MovePageUpButton"));
            var down = Assert.IsType<Button>(view.FindName("MovePageDownButton"));
            var upEnabled = up.IsEnabled;
            var downEnabled = down.IsEnabled;
            var boundToViewModel = ReferenceEquals(down.Command, context.ViewModel.MovePageDownCommand);

            down.Command.Execute(null);
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

            return (
                upEnabled,
                downEnabled,
                boundToViewModel,
                order: context.ViewModel.Pages.Select(page => page.Id).ToArray());
        });

        Assert.False(result.upEnabled);
        Assert.True(result.downEnabled);
        Assert.True(result.boundToViewModel);
        Assert.Equal(["second", "main"], result.order);
    }

    [Fact]
    public async Task PagesEditor_ShowsTheInlineRemoveConfirmation_AndRefreshesAfterAnAdd()
    {
        using var sta = new StaThread();

        var result = await sta.InvokeAsync(() =>
        {
            var context = CreateContext();
            var view = new PagesEditorView { DataContext = context.ViewModel };
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            context.ViewModel.SelectPage("second");
            context.ViewModel.RequestRemoveSelected();
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var confirmationVisible = Assert.IsType<Border>(view.FindName("RemoveConfirmation")).Visibility;
            var pendingName = context.ViewModel.PendingRemovePageName;

            context.ViewModel.CancelRemove();
            var added = context.ViewModel.AddPageAsync().GetAwaiter().GetResult();
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var items = Assert.IsType<ItemsControl>(view.FindName("PageItems"));

            return (confirmationVisible, pendingName, added, addCount: items.Items.Count);
        });

        Assert.Equal(Visibility.Visible, result.confirmationVisible);
        Assert.Equal("Second", result.pendingName);
        Assert.True(result.added);
        Assert.Equal(3, result.addCount);
    }

    private static double ReadDoubleSetter(Style style, DependencyProperty property)
    {
        foreach (var setterBase in style.Setters)
        {
            if (setterBase is Setter { Property: var candidate, Value: double value } && candidate == property)
            {
                return value;
            }
        }

        return 0;
    }

    private static Context CreateContext()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages =
            [
                new PageConfig { Id = "main", Widgets = [] },
                new PageConfig { Id = "second", Name = "Second", Widgets = [] },
            ],
            Appearance = new AppearanceConfig { Theme = "dark" },
        };

        var settings = new FakeSettingsService();
        settings.Handler = change =>
        {
            if (change.Pages is { } pages)
            {
                config.Pages = [.. pages];
                config.ActivePageId = change.ActivePageId ?? config.ActivePageId;
            }

            return SettingsApplyResult.Success(config);
        };

        var pageSwitch = new FakePageSwitchService();
        var viewModel = new PagesEditorViewModel(
            settings,
            pageSwitch,
            () => config);

        return new Context(viewModel);
    }

    private sealed record Context(PagesEditorViewModel ViewModel);
}
