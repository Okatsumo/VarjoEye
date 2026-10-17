using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VarjoEye.Core.Models;
using Windows.Foundation;
using Windows.UI;

namespace VarjoEye.App.Controls;

public sealed class EyeVisualizationControl : UserControl
{
    public static readonly DependencyProperty EyeOpennessProperty =
        DependencyProperty.Register(nameof(EyeOpenness), typeof(double), typeof(EyeVisualizationControl),
            new PropertyMetadata(1d, OnVisualChanged));

    public static readonly DependencyProperty PupilXProperty =
        DependencyProperty.Register(nameof(PupilX), typeof(double), typeof(EyeVisualizationControl),
            new PropertyMetadata(0d, OnVisualChanged));

    public static readonly DependencyProperty PupilYProperty =
        DependencyProperty.Register(nameof(PupilY), typeof(double), typeof(EyeVisualizationControl),
            new PropertyMetadata(0d, OnVisualChanged));

    public static readonly DependencyProperty PupilSizeProperty =
        DependencyProperty.Register(nameof(PupilSize), typeof(double), typeof(EyeVisualizationControl),
            new PropertyMetadata(0.55d, OnVisualChanged));

    public static readonly DependencyProperty VarjoStateProperty =
        DependencyProperty.Register(nameof(VarjoState), typeof(EyeSourceVisual), typeof(EyeVisualizationControl),
            new PropertyMetadata(EyeSourceVisual.Neutral, OnVisualChanged));

    public static readonly DependencyProperty BaballoniaStateProperty =
        DependencyProperty.Register(nameof(BaballoniaState), typeof(EyeSourceVisual), typeof(EyeVisualizationControl),
            new PropertyMetadata(EyeSourceVisual.Neutral, OnVisualChanged));

    public static readonly DependencyProperty CombinedStateProperty =
        DependencyProperty.Register(nameof(CombinedState), typeof(EyeSourceVisual), typeof(EyeVisualizationControl),
            new PropertyMetadata(EyeSourceVisual.Neutral, OnVisualChanged));

    public static readonly DependencyProperty ShowVarjoLayerProperty =
        DependencyProperty.Register(nameof(ShowVarjoLayer), typeof(bool), typeof(EyeVisualizationControl),
            new PropertyMetadata(true, OnVisualChanged));

    public static readonly DependencyProperty ShowBaballoniaLayerProperty =
        DependencyProperty.Register(nameof(ShowBaballoniaLayer), typeof(bool), typeof(EyeVisualizationControl),
            new PropertyMetadata(true, OnVisualChanged));

    public static readonly DependencyProperty ShowCombinedLayerProperty =
        DependencyProperty.Register(nameof(ShowCombinedLayer), typeof(bool), typeof(EyeVisualizationControl),
            new PropertyMetadata(true, OnVisualChanged));

    private readonly Canvas _canvas = new();
    private readonly Canvas _globe = new();
    private readonly Microsoft.UI.Xaml.Shapes.Path _sclera = new();
    private readonly Ellipse _iris = new();
    private readonly Ellipse _innerIris = new();
    private readonly Ellipse _pupil = new();
    private readonly Ellipse _highlight = new();
    private readonly Ellipse _varjoRing = new();
    private readonly Ellipse _varjoDot = new();
    private readonly Ellipse _babbleRing = new();
    private readonly Ellipse _babbleDot = new();
    private readonly Ellipse _combinedRing = new();
    private readonly Ellipse _combinedDot = new();
    private readonly Microsoft.UI.Xaml.Shapes.Path _lids = new();

    public EyeVisualizationControl()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        MinHeight = 160;
        Content = _canvas;
        SizeChanged += (_, _) => Redraw();

        _sclera.Fill = null;
        _sclera.Stroke = Brush("#9DB5D2");
        _sclera.StrokeThickness = 1.6;
        _iris.Fill = Brush("#2A3F58");
        _iris.Stroke = Brush("#4E6F8C");
        _iris.StrokeThickness = 1.4;
        _innerIris.Fill = Brush("#1E3348");
        _pupil.Fill = Brush("#0A121C");
        _highlight.Fill = Brush("#B8D4E6");
        _varjoRing.Stroke = Brush("#347EFF");
        _varjoRing.StrokeThickness = 1.8;
        _varjoDot.Fill = Brush("#347EFF");
        _babbleRing.Stroke = Brush("#A83CFF");
        _babbleRing.StrokeThickness = 1.8;
        _babbleDot.Fill = Brush("#A83CFF");
        _combinedRing.Stroke = Brush("#18D57B");
        _combinedRing.StrokeThickness = 1.8;
        _combinedDot.Fill = Brush("#18D57B");
        _lids.Stroke = Brush("#18D57B");
        _lids.StrokeThickness = 1.5;
        _lids.Fill = null;

        _globe.Children.Add(_iris);
        _globe.Children.Add(_innerIris);
        _globe.Children.Add(_pupil);
        _globe.Children.Add(_highlight);
        _globe.Children.Add(_varjoRing);
        _globe.Children.Add(_varjoDot);
        _globe.Children.Add(_babbleRing);
        _globe.Children.Add(_babbleDot);
        _globe.Children.Add(_combinedRing);
        _globe.Children.Add(_combinedDot);
        _canvas.Children.Add(_sclera);
        _canvas.Children.Add(_globe);
        _canvas.Children.Add(_lids);
    }

    public double EyeOpenness
    {
        get => (double)GetValue(EyeOpennessProperty);
        set => SetValue(EyeOpennessProperty, value);
    }

    public double PupilX
    {
        get => (double)GetValue(PupilXProperty);
        set => SetValue(PupilXProperty, value);
    }

    public double PupilY
    {
        get => (double)GetValue(PupilYProperty);
        set => SetValue(PupilYProperty, value);
    }

    public double PupilSize
    {
        get => (double)GetValue(PupilSizeProperty);
        set => SetValue(PupilSizeProperty, value);
    }

    public EyeSourceVisual VarjoState
    {
        get => (EyeSourceVisual)GetValue(VarjoStateProperty);
        set => SetValue(VarjoStateProperty, value);
    }

    public EyeSourceVisual BaballoniaState
    {
        get => (EyeSourceVisual)GetValue(BaballoniaStateProperty);
        set => SetValue(BaballoniaStateProperty, value);
    }

    public EyeSourceVisual CombinedState
    {
        get => (EyeSourceVisual)GetValue(CombinedStateProperty);
        set => SetValue(CombinedStateProperty, value);
    }

    public bool ShowVarjoLayer
    {
        get => (bool)GetValue(ShowVarjoLayerProperty);
        set => SetValue(ShowVarjoLayerProperty, value);
    }

    public bool ShowBaballoniaLayer
    {
        get => (bool)GetValue(ShowBaballoniaLayerProperty);
        set => SetValue(ShowBaballoniaLayerProperty, value);
    }

    public bool ShowCombinedLayer
    {
        get => (bool)GetValue(ShowCombinedLayerProperty);
        set => SetValue(ShowCombinedLayerProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EyeVisualizationControl control)
        {
            control.Redraw();
        }
    }

    private void Redraw()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 8 || h < 8)
        {
            return;
        }

        var cx = w / 2;
        var cy = h / 2;
        var halfWidth = Math.Min(w * 0.42, 168);
        var open = Math.Clamp(EyeOpenness, 0, 1);
        var halfHeight = Math.Max(3, Math.Min(h * 0.36, 58) * open);
        var almond = BuildAlmond(cx, cy, halfWidth, halfHeight);
        _sclera.Data = almond;
        _globe.Width = w;
        _globe.Height = h;

        var irisCenter = GazePoint(cx, cy, halfWidth, PupilX, PupilY);
        var innerVisible = open > 0.12;
        _iris.Visibility = innerVisible ? Visibility.Visible : Visibility.Collapsed;
        _innerIris.Visibility = innerVisible ? Visibility.Visible : Visibility.Collapsed;
        _pupil.Visibility = innerVisible ? Visibility.Visible : Visibility.Collapsed;
        _highlight.Visibility = innerVisible ? Visibility.Visible : Visibility.Collapsed;
        var irisRadius = Math.Min(34, halfHeight * 0.85);
        PlaceEllipse(_iris, irisCenter, irisRadius);
        PlaceEllipse(_innerIris, irisCenter, irisRadius * 0.7);
        var pupilRadius = irisRadius * 0.38 * Math.Clamp(PupilSize / 0.55, 0.5, 1.6);
        PlaceEllipse(_pupil, irisCenter, pupilRadius);
        PlaceEllipse(_highlight, new Point(irisCenter.X - irisRadius * 0.32, irisCenter.Y - irisRadius * 0.45), irisRadius * 0.14);

        SetLayer(_varjoRing, _varjoDot, ShowVarjoLayer, irisCenter, 18, 2.4);

        var babble = BaballoniaState ?? EyeSourceVisual.Neutral;
        var babbleCenter = GazePoint(cx, cy, halfWidth, babble.PupilX, babble.PupilY);
        SetLayer(_babbleRing, _babbleDot, ShowBaballoniaLayer, babbleCenter, 22, 2.4);

        var combined = CombinedState ?? EyeSourceVisual.Neutral;
        var combinedCenter = GazePoint(cx, cy, halfWidth, combined.PupilX, combined.PupilY);
        SetLayer(_combinedRing, _combinedDot, ShowCombinedLayer, combinedCenter, 27, 2.4);

        if (ShowCombinedLayer)
        {
            var lidHeight = Math.Max(2, Math.Min(h * 0.36, 58) * Math.Clamp(combined.Openness, 0, 1));
            _lids.Data = BuildLids(cx, cy, halfWidth, lidHeight);
            _lids.Visibility = Visibility.Visible;
        }
        else
        {
            _lids.Visibility = Visibility.Collapsed;
        }
    }

    private static void SetLayer(Ellipse ring, Ellipse dot, bool show, Point center, double radius, double dotRadius)
    {
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ring.Visibility = visibility;
        dot.Visibility = visibility;
        if (!show)
        {
            return;
        }

        PlaceEllipse(ring, center, radius);
        PlaceEllipse(dot, center, dotRadius);
    }

    private static void PlaceEllipse(Ellipse ellipse, Point center, double radius)
    {
        ellipse.Width = radius * 2;
        ellipse.Height = radius * 2;
        Canvas.SetLeft(ellipse, center.X - radius);
        Canvas.SetTop(ellipse, center.Y - radius);
    }

    private static Point GazePoint(double cx, double cy, double halfWidth, double gazeX, double gazeY) =>
        new(cx + Math.Clamp(gazeX, -1, 1) * halfWidth * 0.22,
            cy + Math.Clamp(gazeY, -1, 1) * 16);

    private static Geometry BuildAlmond(double cx, double cy, double halfWidth, double halfHeight)
    {
        var geo = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(cx - halfWidth, cy), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new BezierSegment
        {
            Point1 = new Point(cx - halfWidth * 0.48, cy - halfHeight * 1.35),
            Point2 = new Point(cx + halfWidth * 0.48, cy - halfHeight * 1.35),
            Point3 = new Point(cx + halfWidth, cy)
        });
        fig.Segments.Add(new BezierSegment
        {
            Point1 = new Point(cx + halfWidth * 0.48, cy + halfHeight * 1.35),
            Point2 = new Point(cx - halfWidth * 0.48, cy + halfHeight * 1.35),
            Point3 = new Point(cx - halfWidth, cy)
        });
        geo.Figures.Add(fig);
        return geo;
    }

    private static Geometry BuildLids(double cx, double cy, double halfWidth, double lidHeight)
    {
        var geo = new PathGeometry();
        var upper = new PathFigure { StartPoint = new Point(cx - halfWidth, cy), IsClosed = false, IsFilled = false };
        upper.Segments.Add(new BezierSegment
        {
            Point1 = new Point(cx - halfWidth * 0.48, cy - lidHeight * 1.35),
            Point2 = new Point(cx + halfWidth * 0.48, cy - lidHeight * 1.35),
            Point3 = new Point(cx + halfWidth, cy)
        });
        var lower = new PathFigure { StartPoint = new Point(cx - halfWidth, cy), IsClosed = false, IsFilled = false };
        lower.Segments.Add(new BezierSegment
        {
            Point1 = new Point(cx - halfWidth * 0.48, cy + lidHeight * 1.35),
            Point2 = new Point(cx + halfWidth * 0.48, cy + lidHeight * 1.35),
            Point3 = new Point(cx + halfWidth, cy)
        });
        geo.Figures.Add(upper);
        geo.Figures.Add(lower);
        return geo;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var value = hex.TrimStart('#');
        var r = System.Convert.ToByte(value[..2], 16);
        var g = System.Convert.ToByte(value[2..4], 16);
        var b = System.Convert.ToByte(value[4..6], 16);
        return new SolidColorBrush(Color.FromArgb(255, r, g, b));
    }
}
