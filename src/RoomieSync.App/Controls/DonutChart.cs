using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using RoomieSync.App.ViewModels.Items;

namespace RoomieSync.App.Controls;

public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(DonutChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(DonutChart),
            new FrameworkPropertyMetadata(0.38, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (DonutChart)d;
        if (e.OldValue is INotifyCollectionChanged oldC) oldC.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newC) newC.CollectionChanged += chart.OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var slices = ItemsSource?.OfType<ChartSlice>().Where(s => s.Value > 0).ToList() ?? new List<ChartSlice>();
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var outer = size / 2;
        var inner = outer * (1 - Thickness);
        var total = slices.Sum(s => (double)s.Value);

        if (total <= 0)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xEE, 0xE9, 0xE4)), outer - inner);
            dc.DrawEllipse(null, pen, center, (outer + inner) / 2, (outer + inner) / 2);
            return;
        }

        var angle = -90.0;
        foreach (var s in slices)
        {
            var sweep = (double)s.Value / total * 360.0;
            dc.DrawGeometry(s.Brush, null, Segment(center, inner, outer, angle, Math.Min(sweep, 359.99)));
            angle += sweep;
        }
    }

    private static Geometry Segment(Point c, double r1, double r2, double start, double sweep)
    {
        static Point P(Point c, double r, double deg)
        {
            var rad = deg * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }

        var large = sweep > 180;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(c, r2, start), true, true);
            ctx.ArcTo(P(c, r2, start + sweep), new Size(r2, r2), 0, large, SweepDirection.Clockwise, true, true);
            ctx.LineTo(P(c, r1, start + sweep), true, true);
            ctx.ArcTo(P(c, r1, start), new Size(r1, r1), 0, large, SweepDirection.Counterclockwise, true, true);
        }
        g.Freeze();
        return g;
    }
}
