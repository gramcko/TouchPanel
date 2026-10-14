using System.Windows;
using System.Windows.Media;
using TouchPanel.ViewModels;

namespace TouchPanel.Widgets;

/// <summary>
/// The display-only plotting surface of the Sparkline widget (T-07, M2-RF-413): a lightweight
/// <see cref="FrameworkElement"/> that draws the view model's retained samples with gaps and an
/// optional fill. It contains no business logic — scale and gaps come from the view model — and it
/// invalidates only when the bound points or scale change, so rendering happens at most once per
/// snapshot and never on an animation timer.
/// </summary>
/// <remarks>
/// WPF calls <see cref="OnRender"/> only when an <c>AffectsRender</c> property changes or the
/// element is resized, so a steady history produces no redraw work (M2-RF-413). The element is
/// never hit-testable (the template sets <c>IsHitTestVisible=false</c>), keeping the widget
/// display-only like the Gauge.
/// </remarks>
public sealed class SparklinePlot : FrameworkElement
{
    /// <summary>Identifies the <see cref="Points"/> dependency property.</summary>
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IReadOnlyList<double?>),
        typeof(SparklinePlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="ScaleMinimum"/> dependency property.</summary>
    public static readonly DependencyProperty ScaleMinimumProperty = DependencyProperty.Register(
        nameof(ScaleMinimum),
        typeof(double),
        typeof(SparklinePlot),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="ScaleMaximum"/> dependency property.</summary>
    public static readonly DependencyProperty ScaleMaximumProperty = DependencyProperty.Register(
        nameof(ScaleMaximum),
        typeof(double),
        typeof(SparklinePlot),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="IsFilled"/> dependency property.</summary>
    public static readonly DependencyProperty IsFilledProperty = DependencyProperty.Register(
        nameof(IsFilled),
        typeof(bool),
        typeof(SparklinePlot),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="LineBrush"/> dependency property.</summary>
    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush),
        typeof(Brush),
        typeof(SparklinePlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="FillBrush"/> dependency property.</summary>
    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush),
        typeof(Brush),
        typeof(SparklinePlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush DefaultLineBrush = CreateFrozenBrush(Color.FromRgb(0x3D, 0x7E, 0xFF));
    private static readonly Brush DefaultFillBrush = CreateFrozenBrush(
        Color.FromArgb(AppearanceAccentFillConverter.DefaultFillAlpha, 0x3D, 0x7E, 0xFF));

    /// <summary>Gets or sets the retained samples, oldest → newest, with <see langword="null"/> gaps.</summary>
    public IReadOnlyList<double?>? Points
    {
        get => (IReadOnlyList<double?>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>Gets or sets the lower bound of the vertical scale.</summary>
    public double ScaleMinimum
    {
        get => (double)GetValue(ScaleMinimumProperty);
        set => SetValue(ScaleMinimumProperty, value);
    }

    /// <summary>Gets or sets the upper bound of the vertical scale.</summary>
    public double ScaleMaximum
    {
        get => (double)GetValue(ScaleMaximumProperty);
        set => SetValue(ScaleMaximumProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the area under the line is filled.</summary>
    public bool IsFilled
    {
        get => (bool)GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    /// <summary>Gets or sets the line brush; <see langword="null"/> uses the theme default.</summary>
    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>Gets or sets the fill brush; <see langword="null"/> uses the theme default.</summary>
    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var points = Points;
        if (points is null || points.Count == 0)
        {
            return;
        }

        var width = ActualWidth;
        var height = ActualHeight;
        if (!(width > 0) || !(height > 0))
        {
            return;
        }

        var minimum = ScaleMinimum;
        var maximum = ScaleMaximum;
        var span = maximum - minimum;
        if (!double.IsFinite(span) || span <= 0)
        {
            return;
        }

        var lineBrush = LineBrush ?? DefaultLineBrush;
        var fillBrush = IsFilled ? FillBrush ?? DefaultFillBrush : null;
        var pen = new Pen(lineBrush, 1.5)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        var count = points.Count;
        var stepX = count > 1 ? width / (count - 1) : 0d;

        StreamGeometry? geometry = null;
        StreamGeometryContext? context = null;
        double? lastX = null;

        void Flush()
        {
            if (context is null || geometry is null)
            {
                return;
            }

            // A filled area returns to the baseline so each run is a closed shape over the axis.
            if (fillBrush is not null && lastX is double closingX)
            {
                context.LineTo(new Point(closingX, height), isStroked: false, isSmoothJoin: false);
            }

            context.Close();
            geometry.Freeze();
            drawingContext.DrawGeometry(fillBrush, pen, geometry);
            geometry = null;
            context = null;
            lastX = null;
        }

        for (var i = 0; i < count; i++)
        {
            var sample = points[i];
            if (sample is not { } value || !double.IsFinite(value))
            {
                // M2-RF-414: an unavailable sample ends the current run, producing a visible gap.
                Flush();
                continue;
            }

            var x = count > 1 ? i * stepX : width / 2d;
            var y = Math.Clamp(height - ((value - minimum) / span * height), 0d, height);

            if (context is null)
            {
                geometry = new StreamGeometry();
                context = geometry.Open();
                context.BeginFigure(
                    new Point(x, fillBrush is not null ? height : y),
                    isFilled: fillBrush is not null,
                    isClosed: fillBrush is not null);

                if (fillBrush is not null)
                {
                    context.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: false);
                }
            }
            else
            {
                context.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: false);
            }

            lastX = x;
        }

        Flush();
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
