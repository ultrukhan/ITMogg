using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using RoomieSync.App.ViewModels.Items;

namespace RoomieSync.App.Controls;

public sealed class BarChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(BarChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (BarChart)d;
        if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged o) o.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged n) n.CollectionChanged += chart.OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty BarBrushProperty =
        DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(BarChart),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xF0, 0x7B, 0x3F)), FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bars = ItemsSource?.OfType<ChartSlice>().ToList() ?? new List<ChartSlice>();
        if (bars.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var font = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0x74, 0x89));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xEE, 0xE9, 0xE4)), 1);
        var culture = CultureInfo.GetCultureInfo("en-US");

        const double left = 56, bottom = 26, top = 18;
        var plotH = ActualHeight - bottom - top;
        var plotW = ActualWidth - left;
        var max = (double)bars.Max(b => b.Value);
        max = max <= 0 ? 1 : NiceCeiling(max);

        for (var i = 0; i <= 4; i++)
        {
            var y = top + plotH - plotH * i / 4;
            dc.DrawLine(gridPen, new Point(left, y), new Point(ActualWidth, y));
            var text = new FormattedText((max * i / 4).ToString("$#,0", culture), culture, FlowDirection.LeftToRight, font, 11, labelBrush, dpi);
            dc.DrawText(text, new Point(left - text.Width - 8, y - text.Height / 2));
        }

        var slot = plotW / bars.Count;
        var barW = Math.Min(42, slot * 0.55);
        for (var i = 0; i < bars.Count; i++)
        {
            var h = plotH * (double)bars[i].Value / max;
            var x = left + slot * i + (slot - barW) / 2;
            var rect = new Rect(x, top + plotH - h, barW, Math.Max(h, 0));
            dc.DrawRoundedRectangle(bars[i].Brush ?? BarBrush, null, rect, 6, 6);

            var label = new FormattedText(bars[i].Label, culture, FlowDirection.LeftToRight, font, 11, labelBrush, dpi);
            dc.DrawText(label, new Point(x + barW / 2 - label.Width / 2, top + plotH + 6));

            if (bars[i].Value > 0)
            {
                var v = new FormattedText(bars[i].Value.ToString("$#,0", culture), culture, FlowDirection.LeftToRight, font, 10, labelBrush, dpi);
                dc.DrawText(v, new Point(x + barW / 2 - v.Width / 2, rect.Top - v.Height - 2));
            }
        }
    }

    private static double NiceCeiling(double v)
    {
        var exp = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var m in new[] { 1, 2, 2.5, 5, 10 })
            if (m * exp >= v) return m * exp;
        return 10 * exp;
    }
}
