using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;
using TouchPanel.Widgets;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Per-page background presentation (user-requested addition): a color becomes a frozen brush, an
/// absolute image is decoded bounded to 2048 px and cached, unset/invalid values stay empty so the
/// theme background shows through, and the page view model exposes the result behind the widgets.
/// </summary>
public sealed class PageBackgroundViewModelTests
{
    public PageBackgroundViewModelTests() => PageBackgroundViewModel.ClearImageCacheForTests();

    [Fact]
    public async Task Color_BecomesAFrozenSolidBrush()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var background = PageBackgroundViewModel.Create("#11223344", null);

            var brush = Assert.IsType<SolidColorBrush>(background.ColorBrush);
            Assert.Equal(Color.FromArgb(0x11, 0x22, 0x33, 0x44), brush.Color);
            Assert.True(brush.IsFrozen);
            Assert.True(background.HasColor);
            Assert.False(background.HasImage);
            Assert.Equal("#11223344", background.Color);
        });
    }

    [Fact]
    public async Task Image_IsDecodedBoundedAndFrozen()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = TestPng.Write(temp.DirectoryPath, "huge.png", PageBackgroundViewModel.MaxDecodeDimension + 512, 64);

            var background = PageBackgroundViewModel.Create(null, path);

            var image = Assert.IsAssignableFrom<BitmapSource>(background.Image);
            Assert.True(image.IsFrozen);
            Assert.Equal(PageBackgroundViewModel.MaxDecodeDimension, image.PixelWidth);
            Assert.True(image.PixelWidth <= PageBackgroundViewModel.MaxDecodeDimension);
            Assert.True(background.HasImage);
            Assert.Equal(path, background.ImagePath);
            Assert.Equal(Stretch.UniformToFill, background.ImageStretch);
        });
    }

    [Fact]
    public async Task Image_IsCachedPerPathAndFileStamp()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = TestPng.Write(temp.DirectoryPath, "wall.png", 16, 16);

            var first = PageBackgroundViewModel.Create(null, path);
            var second = PageBackgroundViewModel.Create(null, path);

            Assert.Same(first.Image, second.Image);
        });
    }

    [Fact]
    public async Task ColorAndImage_AreBothExposed()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = TestPng.Write(temp.DirectoryPath, "wall.png");

            var background = PageBackgroundViewModel.Create("#FF102030", path);

            Assert.True(background.HasColor);
            Assert.True(background.HasImage);
            Assert.False(background.IsEmpty);
        });
    }

    [Fact]
    public void UnsetOrInvalidValues_StayEmptySoTheThemeShowsThrough()
    {
        Assert.True(PageBackgroundViewModel.Create(null, null).IsEmpty);
        Assert.True(PageBackgroundViewModel.Create("not-a-color", null).IsEmpty);
        Assert.True(PageBackgroundViewModel.Create(null, @"relative\wall.png").IsEmpty);
        Assert.Same(PageBackgroundViewModel.Empty, PageBackgroundViewModel.Create(null, null));
    }

    [Fact]
    public async Task MissingOrUndecodableImage_DoesNotThrow_AndKeepsTheColor()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var missing = Path.Combine(temp.DirectoryPath, "missing.png");
            var bogus = Path.Combine(temp.DirectoryPath, "bogus.png");
            File.WriteAllText(bogus, "not an image");

            var missingBackground = PageBackgroundViewModel.Create("#FF0000", missing);
            var bogusBackground = PageBackgroundViewModel.Create(null, bogus);

            Assert.False(missingBackground.HasImage);
            Assert.True(missingBackground.HasColor);
            Assert.True(bogusBackground.IsEmpty);
        });
    }

    [Fact]
    public async Task PageViewModel_ExposesTheBackgroundBehindTheWidgets()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = TestPng.Write(temp.DirectoryPath, "wall.png", 24, 24);
            var config = new PanelConfig
            {
                SchemaVersion = PanelConfig.CurrentSchemaVersion,
                ActivePageId = "main",
                Pages =
                [
                    new PageConfig
                    {
                        Id = "main",
                        Background = "#FF203040",
                        BackgroundImage = path,
                        Widgets = [new LabelWidgetConfig { Id = "l", Width = 100, Height = 40 }],
                    },
                ],
            };

            var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());

            Assert.True(page.Background.HasColor);
            Assert.True(page.Background.HasImage);
            Assert.Single(page.Widgets);
        });
    }

    [Fact]
    public void PageViewModel_WithoutBackground_UsesTheSharedEmptyBackground()
    {
        var config = new PanelConfig
        {
            SchemaVersion = PanelConfig.CurrentSchemaVersion,
            ActivePageId = "main",
            Pages = [new PageConfig { Id = "main" }],
        };

        var page = PageViewModel.CreateForActivePage(config, WidgetRegistry.CreateDefault());

        Assert.Same(PageBackgroundViewModel.Empty, page.Background);
        Assert.True(page.Background.IsEmpty);
    }
}
