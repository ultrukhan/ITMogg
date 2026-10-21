using RoomieSync.App.Services;

namespace RoomieSync.App.ViewModels;


public sealed class MainViewModel(IAppNavigator navigator, IDialogService dialogs, IToastService toast)
{
    public IAppNavigator Navigator { get; } = navigator;
    public IDialogService Dialogs { get; } = dialogs;
    public IToastService Toast { get; } = toast;
}
