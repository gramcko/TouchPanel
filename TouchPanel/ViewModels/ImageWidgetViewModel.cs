using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;
using TouchPanel.Config;

namespace TouchPanel.ViewModels;

/// <summary>
/// Image widget view model (user-requested addition): displays a raster image (PNG and the other
/// formats WPF <see cref="BitmapImage"/> can decode) loaded once from the configured absolute file
/// path and scaled into the widget bounds by the template's <see cref="Stretch"/> mode. Resizing is
/// the existing edit-mode behavior and needs no per-instance code.
/// </summary>
/// <remarks>
/// <para><b>Untrusted input (security).</b> The path comes from configuration. Only the file is
/// opened read-only and decoded; it is never executed and no shell or process is involved. A
/// relative path is rejected by <c>ConfigValidator</c> and also defensively here, so a relative
/// value can never be resolved against the current directory.</para>
/// <para><b>Bounded decode (memory).</b> The image is decoded at most
/// <see cref="MaxDecodeDimension"/> pixels on its longest side (a cheap metadata probe decides
/// whether to down-scale), so a very large file cannot exhaust memory. The bitmap is frozen and the
/// file handle is released by <see cref="BitmapCacheOption.OnLoad"/>.</para>
/// <para><b>Failure containment (RF-012).</b> An empty, non-absolute, missing, or undecodable path
/// leaves the widget visible with an explicit placeholder; the failure is logged exactly once per
/// resolved path and never throws.</para>
/// </remarks>
public sealed class ImageWidgetViewModel : WidgetViewModelBase
{
    /// <summary>The text shown when no image is available (never a blank surface).</summary>
    public const string PlaceholderText = "No image";

    /// <summary>
    /// Largest decoded dimension in pixels (user-requested bound). A larger source image is decoded
    /// down to this on its longest side so the in-memory bitmap stays bounded.
    /// </summary>
    public const int MaxDecodeDimension = 2048;

    private readonly ILogger _logger;
    private string? _path;
    private ImageStretchMode _stretchMode;
    private BitmapSource? _image;
    private bool _hasImage;
    private bool _loadFailureLogged;
    private bool _disabledByInvalidPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageWidgetViewModel"/> class and attempts to
    /// load the configured image.
    /// </summary>
    /// <param name="config">The validated image config; must not be <see langword="null"/>.</param>
    /// <param name="timeProvider">Clock used by the base error-state support; unused by images.</param>
    /// <param name="dispatch">UI-thread marshalling delegate; <see langword="null"/> runs inline (tests).</param>
    /// <param name="logger">Logger for the one-time load-failure notice.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public ImageWidgetViewModel(
        ImageWidgetConfig config,
        TimeProvider? timeProvider = null,
        Action<Action>? dispatch = null,
        ILogger? logger = null)
        : base(config, WidgetTemplateKeys.Image, actionExecutor: null, timeProvider, dispatch, logger)
    {
        ArgumentNullException.ThrowIfNull(config);

        _logger = logger ?? Log.Logger;
        _path = NormalizePath(config.Path);
        _stretchMode = NormalizeStretch(config.Stretch);

        TryLoadImage();
        ReconcilePath();
    }

    /// <summary>Gets the decoded, frozen image, or <see langword="null"/> when unavailable.</summary>
    public BitmapSource? Image => _image;

    /// <summary>Gets a value indicating whether an image is currently displayed.</summary>
    public bool HasImage => _hasImage;

    /// <summary>Gets a value indicating whether the explicit placeholder is shown (never a blank).</summary>
    public bool IsUnavailable => !_hasImage;

    /// <summary>Gets the placeholder text shown while no image is available.</summary>
    public string Placeholder => PlaceholderText;

    /// <summary>Gets the configured fit mode (the Unknown sentinel is never exposed).</summary>
    public ImageStretchMode StretchMode => _stretchMode;

    /// <summary>Gets the WPF <see cref="Stretch"/> the template binds directly.</summary>
    public Stretch Stretch => _stretchMode switch
    {
        ImageStretchMode.Fill => Stretch.Fill,
        ImageStretchMode.UniformToFill => Stretch.UniformToFill,
        ImageStretchMode.None => Stretch.None,
        _ => Stretch.Uniform,
    };

    /// <inheritdoc />
    /// <remarks>
    /// Refreshes the path and fit mode live: a changed path is reloaded (with the one-time failure
    /// notice reset) and the fit mode is re-projected, all without a rebuild.
    /// </remarks>
    public override void ApplyConfig(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        base.ApplyConfig(config);

        if (config is not ImageWidgetConfig image)
        {
            return;
        }

        var newPath = NormalizePath(image.Path);
        var newStretch = NormalizeStretch(image.Stretch);
        var pathChanged = !string.Equals(_path, newPath, StringComparison.Ordinal);
        var stretchChanged = _stretchMode != newStretch;

        _path = newPath;
        _stretchMode = newStretch;

        if (pathChanged)
        {
            _loadFailureLogged = false;
            TryLoadImage();
        }

        if (stretchChanged)
        {
            OnPropertyChanged(nameof(StretchMode));
            OnPropertyChanged(nameof(Stretch));
        }

        ReconcilePath();
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        // The image is a frozen, self-contained bitmap with no live subscription or file handle.
        _image = null;
        _hasImage = false;
    }

    private void TryLoadImage()
    {
        var path = _path;
        if (string.IsNullOrEmpty(path) || !IsAbsolutePath(path))
        {
            SetImage(null, hasImage: false);
            LogLoadFailure(exception: null, path);
            return;
        }

        try
        {
            var image = LoadBounded(path);
            SetImage(image, hasImage: image is not null);
            if (image is null)
            {
                LogLoadFailure(exception: null, path);
            }
        }
        catch (Exception exception)
        {
            SetImage(null, hasImage: false);
            LogLoadFailure(exception, path);
        }
    }

    private void SetImage(BitmapSource? image, bool hasImage)
    {
        var changed = _hasImage != hasImage || !ReferenceEquals(_image, image);
        _image = image;
        _hasImage = hasImage;

        if (!changed)
        {
            return;
        }

        OnPropertyChanged(nameof(Image));
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(IsUnavailable));
    }

    private void LogLoadFailure(Exception? exception, string? path)
    {
        if (_loadFailureLogged)
        {
            return;
        }

        _loadFailureLogged = true;
        var displayPath = string.IsNullOrEmpty(path) ? "(none)" : path;
        const string message =
            "Image {WidgetId}: could not load an image from '{Path}'; showing the placeholder";
        if (exception is null)
        {
            _logger.Warning(message, WidgetId, displayPath);
        }
        else
        {
            _logger.Warning(exception, message, WidgetId, displayPath);
        }
    }

    /// <summary>
    /// Re-evaluates the invalid-path containment: an empty or non-absolute path disables the widget
    /// with a visible reason (matching the load-time validation), and a valid path restores it. The
    /// enablement and reason are a projection of the path and never persisted.
    /// </summary>
    private void ReconcilePath()
    {
        var valid = _path is { Length: > 0 } && IsAbsolutePath(_path);

        if (valid)
        {
            if (_disabledByInvalidPath)
            {
                _disabledByInvalidPath = false;
                IsEnabled = true;
                DisabledReason = null;
                NotifyEnablement();
            }

            return;
        }

        if (_disabledByInvalidPath)
        {
            return;
        }

        _disabledByInvalidPath = true;
        IsEnabled = false;
        DisabledReason = $"Image '{WidgetId}': no absolute image path is configured.";
        NotifyEnablement();
    }

    private void NotifyEnablement()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(DisabledReason));
        OnPropertyChanged(nameof(ShowsDisabledIndicator));
        OnPropertyChanged(nameof(IsInteractable));
    }

    /// <summary>
    /// Loads the image bounded to <see cref="MaxDecodeDimension"/> on its longest side. A cheap
    /// metadata probe (no pixel decode) decides whether to down-scale; when the probe cannot report
    /// dimensions, the decode is still bounded to the cap.
    /// </summary>
    /// <param name="path">The absolute image path.</param>
    /// <returns>The frozen decoded bitmap, or <see langword="null"/> when the file has no frame.</returns>
    private static BitmapSource LoadBounded(string path)
    {
        var (pixelWidth, pixelHeight) = ProbeDimensions(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;

        if (pixelWidth > 0 && pixelHeight > 0)
        {
            if (pixelWidth > MaxDecodeDimension || pixelHeight > MaxDecodeDimension)
            {
                if (pixelWidth >= pixelHeight)
                {
                    image.DecodePixelWidth = MaxDecodeDimension;
                }
                else
                {
                    image.DecodePixelHeight = MaxDecodeDimension;
                }
            }
        }
        else
        {
            // The probe could not report dimensions; bound the decode unconditionally.
            image.DecodePixelWidth = MaxDecodeDimension;
        }

        image.EndInit();
        image.Freeze();
        return image;
    }

    private static (int Width, int Height) ProbeDimensions(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception)
        {
            // Some formats may not expose dimensions during a delayed probe; the caller still bounds.
            return (0, 0);
        }
    }

    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    private static ImageStretchMode NormalizeStretch(ImageStretchMode stretch) =>
        stretch == ImageStretchMode.Unknown ? ImageStretchMode.Uniform : stretch;

    private static bool IsAbsolutePath(string path)
    {
        try
        {
            return Path.IsPathRooted(path);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
