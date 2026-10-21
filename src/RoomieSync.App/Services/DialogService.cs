using CommunityToolkit.Mvvm.ComponentModel;
using RoomieSync.App.ViewModels.Dialogs;

namespace RoomieSync.App.Services;

public interface IDialogService
{
    DialogViewModel? CurrentDialog { get; }
    Task<bool> ShowAsync(DialogViewModel dialog);
    Task<bool> ConfirmAsync(string title, string message, string? confirmText = null, bool danger = false);
    Task AlertAsync(string title, string message);
}


public sealed class DialogService : ObservableObject, IDialogService
{
    private DialogViewModel? _currentDialog;

    public DialogViewModel? CurrentDialog
    {
        get => _currentDialog;
        private set => SetProperty(ref _currentDialog, value);
    }

    public async Task<bool> ShowAsync(DialogViewModel dialog)
    {
        var previous = CurrentDialog;
        CurrentDialog = dialog;
        try
        {
            return await dialog.Completion;
        }
        finally
        {
            CurrentDialog = previous;
        }
    }

    public Task<bool> ConfirmAsync(string title, string message, string? confirmText = null, bool danger = false)
        => ShowAsync(new ConfirmDialogViewModel(title, message, confirmText, danger, showCancel: true));

    public Task AlertAsync(string title, string message)
        => ShowAsync(new ConfirmDialogViewModel(title, message, null, false, showCancel: false));
}
