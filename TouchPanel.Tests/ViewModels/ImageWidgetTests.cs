using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TouchPanel.Config;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.ViewModels;

/// <summary>
/// Image widget view model (user-requested addition): loads a real generated PNG, bounds the decode
/// of a large image, projects the fit mode, and degrades to an explicit placeholder (logged once,
/// never a crash) for an empty, relative, missing, or undecodable path.
/// </summary>
public sealed class ImageWidgetTests
{
    [Fact]
    public async Task LoadsARealPngAndExposesTheFrozenImage()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = WritePng(temp.DirectoryPath, "logo.png", 64, 32);

            using var viewModel = new ImageWidgetViewModel(CreateConfig(path));

            Assert.True(viewModel.HasImage);
            Assert.False(viewModel.IsUnavailable);
            var image = Assert.IsAssignableFrom<BitmapSource>(viewModel.Image);
            Assert.True(image.IsFrozen);
            Assert.Equal(64, image.PixelWidth);
            Assert.Equal(32, image.PixelHeight);
            Assert.True(viewModel.IsEnabled);
        });
    }

    [Fact]
    public async Task LargeImage_IsDecodedBoundedToTheCap()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            // Wider than the cap so the decoder must down-scale rather than allocate at native size.
            var path = WritePng(temp.DirectoryPath, "huge.png", ImageWidgetViewModel.MaxDecodeDimension + 512, 100);

            using var viewModel = new ImageWidgetViewModel(CreateConfig(path));

            var image = Assert.IsAssignableFrom<BitmapSource>(viewModel.Image);
            Assert.Equal(ImageWidgetViewModel.MaxDecodeDimension, image.PixelWidth);
            Assert.True(image.PixelWidth <= ImageWidgetViewModel.MaxDecodeDimension);
        });
    }

    [Fact]
    public async Task MissingFile_ShowsPlaceholderAndLogsOnce()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var missing = Path.Combine(temp.DirectoryPath, "nope.png");
            var (logger, sink) = TestLoggerFactory.Create();

            using var viewModel = new ImageWidgetViewModel(CreateConfig(missing), logger: logger);

            Assert.False(viewModel.HasImage);
            Assert.True(viewModel.IsUnavailable);
            Assert.Null(viewModel.Image);
            Assert.Equal(ImageWidgetViewModel.PlaceholderText, viewModel.Placeholder);
            Assert.Single(sink.Events);

            // Re-applying the same config must not log again (once per resolved path).
            viewModel.ApplyConfig(CreateConfig(missing));
            Assert.Single(sink.Events);
        });
    }

    [Fact]
    public async Task EmptyPath_ShowsPlaceholderAndLogsOnceWithoutCrashing()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, sink) = TestLoggerFactory.Create();

            using var viewModel = new ImageWidgetViewModel(CreateConfig(null), logger: logger);

            Assert.False(viewModel.HasImage);
            Assert.True(viewModel.IsUnavailable);
            Assert.False(viewModel.IsEnabled);
            Assert.NotNull(viewModel.DisabledReason);
            Assert.Single(sink.Events);
        });
    }

    [Fact]
    public async Task RelativePath_IsRejectedAndShowsPlaceholder()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            var (logger, sink) = TestLoggerFactory.Create();

            using var viewModel = new ImageWidgetViewModel(CreateConfig(@"Pictures\logo.png"), logger: logger);

            Assert.False(viewModel.HasImage);
            Assert.True(viewModel.IsUnavailable);
            Assert.False(viewModel.IsEnabled);
            Assert.Single(sink.Events);
        });
    }

    [Fact]
    public async Task UndecodableFile_ShowsPlaceholderAndLogsOnce()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.DirectoryPath, "not-an-image.png");
            File.WriteAllText(path, "this is not a png");
            var (logger, sink) = TestLoggerFactory.Create();

            using var viewModel = new ImageWidgetViewModel(CreateConfig(path), logger: logger);

            Assert.False(viewModel.HasImage);
            Assert.True(viewModel.IsUnavailable);
            Assert.True(viewModel.IsEnabled); // a valid absolute path is configured; only the file is bad
            Assert.Single(sink.Events);
        });
    }

    [Fact]
    public async Task ApplyConfig_NewValidPathLoadsAndEnablesWithoutARebuild()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = WritePng(temp.DirectoryPath, "later.png", 48, 48);
            var (logger, sink) = TestLoggerFactory.Create();

            using var viewModel = new ImageWidgetViewModel(CreateConfig(null), logger: logger);
            Assert.False(viewModel.HasImage);
            Assert.False(viewModel.IsEnabled);

            viewModel.ApplyConfig(CreateConfig(path));

            Assert.True(viewModel.HasImage);
            Assert.True(viewModel.IsEnabled);
            Assert.Null(viewModel.DisabledReason);
            Assert.Single(sink.Events); // the initial empty-path notice; the successful load logs nothing
        });
    }

    [Fact]
    public async Task Stretch_ProjectsTheConfiguredFitMode()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            using var temp = new TempDirectory();
            var path = WritePng(temp.DirectoryPath, "fit.png", 20, 20);

            using var uniform = new ImageWidgetViewModel(CreateConfig(path, ImageStretchMode.Uniform));
            using var fill = new ImageWidgetViewModel(CreateConfig(path, ImageStretchMode.Fill));
            using var uniformToFill = new ImageWidgetViewModel(CreateConfig(path, ImageStretchMode.UniformToFill));
            using var none = new ImageWidgetViewModel(CreateConfig(path, ImageStretchMode.None));
            using var unknown = new ImageWidgetViewModel(CreateConfig(path, ImageStretchMode.Unknown));

            Assert.Equal(Stretch.Uniform, uniform.Stretch);
            Assert.Equal(Stretch.Fill, fill.Stretch);
            Assert.Equal(Stretch.UniformToFill, uniformToFill.Stretch);
            Assert.Equal(Stretch.None, none.Stretch);
            Assert.Equal(Stretch.Uniform, unknown.Stretch);
        });
    }

    private static ImageWidgetConfig CreateConfig(string? path, ImageStretchMode stretch = ImageStretchMode.Uniform) => new()
    {
        Id = "image-1",
        X = 0,
        Y = 0,
        Width = 240,
        Height = 160,
        Path = path,
        Stretch = stretch,
    };

    private static string WritePng(string directory, string fileName, int width, int height)
    {
        var path = Path.Combine(directory, fileName);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x40;
            pixels[i + 1] = 0x80;
            pixels[i + 2] = 0xC0;
            pixels[i + 3] = 0xFF;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(stream);
        return path;
    }
}
