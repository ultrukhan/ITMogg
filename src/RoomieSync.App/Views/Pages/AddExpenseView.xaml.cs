using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoomieSync.App.ViewModels.Pages;

namespace RoomieSync.App.Views.Pages;

public partial class AddExpenseView : UserControl
{
    public AddExpenseView()
    {
        InitializeComponent();
    }

    private AddExpenseViewModel? Vm => DataContext as AddExpenseViewModel;

    
    private void OnReceiptDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnReceiptDrop(object sender, DragEventArgs e)
    {
        if (Vm is null || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        await Vm.LoadReceiptAsync(files[0]);
    }

    private void OnDropZoneClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) is not null) return; 
        if (Vm?.ReceiptFileName is null) Vm?.BrowseReceiptCommand.Execute(null);
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
