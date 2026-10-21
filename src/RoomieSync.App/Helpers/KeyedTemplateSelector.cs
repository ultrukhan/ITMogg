using System.Windows;
using System.Windows.Controls;

namespace RoomieSync.App.Helpers;


public sealed class KeyedTemplateSelector : DataTemplateSelector
{
    private static readonly DataTemplate Empty = CreateEmpty();

    public string Prefix { get; set; } = string.Empty;

    public override DataTemplate SelectTemplate(object? item, DependencyObject container)
    {
        if (item is null || container is not FrameworkElement fe) return Empty;
        return fe.TryFindResource(Prefix + item.GetType().Name) as DataTemplate ?? Empty;
    }

    private static DataTemplate CreateEmpty()
    {
        var t = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(Grid)) };
        t.Seal();
        return t;
    }
}
