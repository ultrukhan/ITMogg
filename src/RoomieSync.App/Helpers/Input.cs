using System.Windows;
using System.Windows.Controls;
using RoomieSync.Core.Common;

namespace RoomieSync.App.Helpers;

public static class Input
{
    private static readonly HashSet<WeakReference<DependencyObject>> CurrencyPrefixTargets = new();

    static Input()
    {
        _ = PasswordBoxHelper.BoundPasswordProperty;
        MoneyFormat.CurrencyChanged += (_, _) => RefreshCurrencyPrefixes();
    }

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Input), new PropertyMetadata(string.Empty));

    public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string value) => d.SetValue(PlaceholderProperty, value);

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Input), new PropertyMetadata(string.Empty));

    public static string GetIcon(DependencyObject d) => (string)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string value) => d.SetValue(IconProperty, value);

    public static readonly DependencyProperty PrefixProperty =
        DependencyProperty.RegisterAttached(
            "Prefix",
            typeof(string),
            typeof(Input),
            new PropertyMetadata(string.Empty, OnPrefixChanged, CoercePrefix));

    public static string GetPrefix(DependencyObject d) => (string)d.GetValue(PrefixProperty);
    public static void SetPrefix(DependencyObject d, string value) => d.SetValue(PrefixProperty, value);

    private static void OnPrefixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is string s && (s == "$" || MoneyFormat.SupportedCurrencies.Any(c => c.Symbol == s)))
        {
            lock (CurrencyPrefixTargets)
                CurrencyPrefixTargets.Add(new WeakReference<DependencyObject>(d));
        }
    }

    private static object CoercePrefix(DependencyObject d, object baseValue)
    {
        if (baseValue is string s && s == "$")
            return MoneyFormat.Symbol;
        return baseValue;
    }

    private static void RefreshCurrencyPrefixes()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (CurrencyPrefixTargets)
            {
                CurrencyPrefixTargets.RemoveWhere(wr => !wr.TryGetTarget(out _));
                foreach (var wr in CurrencyPrefixTargets)
                {
                    if (wr.TryGetTarget(out var d))
                        d.CoerceValue(PrefixProperty);
                }
            }
        });
    }

    public static readonly DependencyProperty IsEmptyProperty =
        DependencyProperty.RegisterAttached("IsEmpty", typeof(bool), typeof(Input), new PropertyMetadata(true));

    public static bool GetIsEmpty(DependencyObject d) => (bool)d.GetValue(IsEmptyProperty);
    public static void SetIsEmpty(DependencyObject d, bool value) => d.SetValue(IsEmptyProperty, value);
}

public static class PasswordBoxHelper
{
    static PasswordBoxHelper()
    {
        EventManager.RegisterClassHandler(
            typeof(PasswordBox),
            PasswordBox.PasswordChangedEvent,
            new RoutedEventHandler(OnPasswordChanged));
    }

    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached("IsUpdating", typeof(bool), typeof(PasswordBoxHelper));

    public static string GetBoundPassword(DependencyObject d) => (string)(d.GetValue(BoundPasswordProperty) ?? string.Empty);
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if (!(bool)box.GetValue(IsUpdatingProperty))
        {
            box.SetValue(IsUpdatingProperty, true);
            box.Password = e.NewValue as string ?? string.Empty;
            box.SetValue(IsUpdatingProperty, false);
        }
        Input.SetIsEmpty(box, box.Password.Length == 0);
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        Input.SetIsEmpty(box, box.Password.Length == 0);

        if ((bool)box.GetValue(IsUpdatingProperty)) return;

        box.SetValue(IsUpdatingProperty, true);
        SetBoundPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}
