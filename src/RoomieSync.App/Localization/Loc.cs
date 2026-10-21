using System.Globalization;
using System.Windows;

namespace RoomieSync.App.Localization;

public static class Loc
{
    public static readonly IReadOnlyList<(string Code, string Name)> Available = new[]
    {
        ("en", "English"),
        // ("uk", "Українська"),  //  в розробці Strings.uk.xaml
    };

    private static ResourceDictionary? _current;

    public static string CurrentLanguage { get; private set; } = "en";

    public static void SetLanguage(string code)
    {
        var app = Application.Current ?? throw new InvalidOperationException("Application is not running.");
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Localization/Strings.{code}.xaml", UriKind.Absolute) };
        if (_current is not null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Add(dict);
        _current = dict;
        CurrentLanguage = code;
    }

    public static string Get(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.GetCultureInfo("en-US"), Get(key), args);
}
