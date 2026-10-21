using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.Core.Common;

namespace RoomieSync.App.ViewModels.Dialogs;


public abstract partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public Task<bool> Completion => _tcs.Task;

    public abstract string Title { get; }

    public virtual double Width => 460;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isBusy;

    
    protected virtual Task<bool> OnConfirmAsync() => Task.FromResult(true);

    [RelayCommand]
    private async Task Confirm()
    {
        Error = null;
        try
        {
            IsBusy = true;
            if (await OnConfirmAsync()) _tcs.TrySetResult(true);
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _tcs.TrySetResult(false);

    protected void Close(bool result) => _tcs.TrySetResult(result);
}

public sealed class ConfirmDialogViewModel(string title, string message, string? confirmText, bool danger, bool showCancel) : DialogViewModel
{
    public override string Title { get; } = title;
    public string Message { get; } = message;
    public string ConfirmText { get; } = confirmText ?? Localization.Loc.Get(showCancel ? "S.Common.Confirm" : "S.Common.Ok");
    public bool IsDanger { get; } = danger;
    public bool ShowCancel { get; } = showCancel;
    public override double Width => 420;
}
