using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Reusable icon presenter for widget icons (user-requested brand and line-art icons): renders the
/// existing Segoe MDL2 glyph for a font icon id, a filled monochrome vector <see cref="Path"/> for a
/// brand id, or a stroked line-art <see cref="Path"/> for an id that declares a stroke style. The
/// control owns only presentation; icon-id resolution stays in
/// <see cref="TouchPanel.Config.WidgetIconResolver"/>, so the same presenter works for the widget
/// <c>Icon</c>, the label icon, the inline picker tiles, and the collapsed picker header.
/// </summary>
/// <remarks>
/// <para>Filled vectors are sized to <c>IconSize * 0.82</c> and vertically centred in the square
/// glyph box. A solid 24x24 path reads optically heavier than a font glyph at the same nominal size,
/// so the inset keeps the visual weight consistent and preserves the surrounding layout.</para>
/// <para>Stroke vectors use the full icon box and a pen width of
/// <c>IconSize * StrokeUnits / 24</c> (the catalog default is 2 units, ~8% of the box), with round
/// caps and joins so the line art matches the MDL2 glyph weight.</para>
/// <para><see cref="Foreground"/> inherits from the containing control, exactly like the
/// <see cref="TextBlock"/> it replaces, so the widget's normal foreground (or a per-widget
/// override layered by the templates) fills both representation types.</para>
/// </remarks>
public partial class WidgetIconPresenter : UserControl
{
    /// <summary>Fraction of the glyph box occupied by a filled vector path, keeping optical weight matched.</summary>
    public const double VectorScale = 0.82;

    /// <summary>The coordinate box the catalog vector paths are authored in.</summary>
    public const double StrokeViewbox = 24d;

    /// <summary>Fallback stroke width in viewbox units when a stroked icon omits a positive value.</summary>
    public const double DefaultStrokeUnits = 2d;

    /// <summary>Identifies the <see cref="Glyph"/> dependency property.</summary>
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(string),
        typeof(WidgetIconPresenter),
        new PropertyMetadata(null, OnVisualChanged));

    /// <summary>Identifies the <see cref="PathData"/> dependency property.</summary>
    public static readonly DependencyProperty PathDataProperty = DependencyProperty.Register(
        nameof(PathData),
        typeof(string),
        typeof(WidgetIconPresenter),
        new PropertyMetadata(null, OnVisualChanged));

    /// <summary>Identifies the <see cref="Stroke"/> dependency property.</summary>
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(bool),
        typeof(WidgetIconPresenter),
        new PropertyMetadata(false, OnVisualChanged));

    /// <summary>Identifies the <see cref="StrokeUnits"/> dependency property.</summary>
    public static readonly DependencyProperty StrokeUnitsProperty = DependencyProperty.Register(
        nameof(StrokeUnits),
        typeof(double),
        typeof(WidgetIconPresenter),
        new PropertyMetadata(0d, OnVisualChanged));

    /// <summary>Identifies the <see cref="IconSize"/> dependency property.</summary>
    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize),
        typeof(double),
        typeof(WidgetIconPresenter),
        new PropertyMetadata(20d, OnVisualChanged));

    /// <summary>Initializes a new instance of the <see cref="WidgetIconPresenter"/> class.</summary>
    public WidgetIconPresenter()
    {
        InitializeComponent();
        UpdateVisual();
    }

    static WidgetIconPresenter()
    {
        // The replaced TextBlocks inherited Foreground from the widget; make the presenter's
        // own Foreground inherit too so glyph and vector receive the same brush.
        ForegroundProperty.OverrideMetadata(
            typeof(WidgetIconPresenter),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    }

    /// <summary>Gets or sets the MDL2 glyph to render, or <see langword="null"/> for a vector icon.</summary>
    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Gets or sets the vector path data to render, or <see langword="null"/> for a glyph icon.</summary>
    public string? PathData
    {
        get => (string?)GetValue(PathDataProperty);
        set => SetValue(PathDataProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the vector path is line art drawn with a stroked outline instead of a
    /// filled shape. Filled brand vectors keep <see langword="false"/>.
    /// </summary>
    public bool Stroke
    {
        get => (bool)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke pen width in the 24-unit catalog viewbox. Values that are not
    /// finite/positive fall back to <see cref="DefaultStrokeUnits"/> for a stroked icon.
    /// </summary>
    public double StrokeUnits
    {
        get => (double)GetValue(StrokeUnitsProperty);
        set => SetValue(StrokeUnitsProperty, value);
    }

    /// <summary>
    /// Gets or sets the square glyph box size in DIPs (font size for a glyph, the outer box for a
    /// vector). Values that are not finite/positive fall back to 20 DIP.
    /// </summary>
    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WidgetIconPresenter)d).UpdateVisual();

    private void UpdateVisual()
    {
        if (GlyphText is null || VectorPath is null || StrokePath is null || IconBox is null)
        {
            // The change callback can run from a property set before InitializeComponent.
            return;
        }

        var size = double.IsFinite(IconSize) && IconSize > 0 ? IconSize : 20d;

        IconBox.Width = size;
        IconBox.Height = size;

        GlyphText.FontSize = size;
        GlyphText.Text = Glyph ?? string.Empty;
        GlyphText.Visibility = string.IsNullOrEmpty(Glyph) ? Visibility.Collapsed : Visibility.Visible;

        Geometry? geometry = null;
        if (!string.IsNullOrEmpty(PathData))
        {
            try
            {
                geometry = Geometry.Parse(PathData);
            }
            catch (FormatException)
            {
                // Defensive: catalog path data is trusted, but a malformed value must never crash a
                // template; the box simply renders nothing.
                geometry = null;
            }
        }

        var useStroke = geometry is not null && Stroke;

        // Filled vector (brand icons): unchanged 82% inset, filled with the widget foreground.
        VectorPath.Data = useStroke ? null : geometry;
        VectorPath.Visibility = geometry is null || useStroke ? Visibility.Collapsed : Visibility.Visible;
        VectorPath.Width = size * VectorScale;
        VectorPath.Height = size * VectorScale;

        // Line-art vector: full box, explicit stroke and thickness scaled to the icon size.
        StrokePath.Data = useStroke ? geometry : null;
        StrokePath.Visibility = useStroke ? Visibility.Visible : Visibility.Collapsed;
        StrokePath.Width = size;
        StrokePath.Height = size;
        if (useStroke)
        {
            var units = double.IsFinite(StrokeUnits) && StrokeUnits > 0
                ? StrokeUnits
                : DefaultStrokeUnits;
            StrokePath.StrokeThickness = size * (units / StrokeViewbox);
            StrokePath.StrokeStartLineCap = PenLineCap.Round;
            StrokePath.StrokeEndLineCap = PenLineCap.Round;
            StrokePath.StrokeLineJoin = PenLineJoin.Round;
        }
        else
        {
            StrokePath.StrokeThickness = 0;
        }
    }
}
