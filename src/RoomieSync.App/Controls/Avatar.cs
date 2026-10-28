using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RoomieSync.App.Controls;


public sealed class Avatar : Control
{
    public static readonly DependencyProperty InitialsProperty =
        DependencyProperty.Register(nameof(Initials), typeof(string), typeof(Avatar), new PropertyMetadata("?"));

    public string Initials
    {
        get => (string)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    public static readonly DependencyProperty ColorHexProperty =
        DependencyProperty.Register(nameof(ColorHex), typeof(string), typeof(Avatar), new PropertyMetadata("#F07B3F", OnColorChanged));

    public string ColorHex
    {
        get => (string)GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(Avatar), new PropertyMetadata(32d));

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private static readonly DependencyPropertyKey FillPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Fill), typeof(Brush), typeof(Avatar),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xF0, 0x7B, 0x3F))));

    public static readonly DependencyProperty FillProperty = FillPropertyKey.DependencyProperty;

    public Brush Fill => (Brush)GetValue(FillProperty);

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString((string)e.NewValue ?? "#F07B3F");
            var brush = new LinearGradientBrush(Lighten(color, 0.18), color, 45);
            brush.Freeze();
            d.SetValue(FillPropertyKey, brush);
        }
        catch (FormatException) { }
    }

    private static Color Lighten(Color c, double k) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * k), (byte)(c.G + (255 - c.G) * k), (byte)(c.B + (255 - c.B) * k));
}
