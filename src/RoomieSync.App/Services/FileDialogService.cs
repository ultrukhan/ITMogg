using Microsoft.Win32;

namespace RoomieSync.App.Services;

public interface IFileDialogService
{
    string? OpenReceiptImage();
    string? SaveFile(string defaultFileName, string filter);
}

public sealed class FileDialogService : IFileDialogService
{
    public string? OpenReceiptImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select receipt photo",
            Filter = "Receipt (*.png;*.jpg;*.jpeg;*.pdf)|*.png;*.jpg;*.jpeg;*.pdf|All files (*.*)|*.*"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? SaveFile(string defaultFileName, string filter)
    {
        var dlg = new SaveFileDialog { FileName = defaultFileName, Filter = filter, AddExtension = true };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
