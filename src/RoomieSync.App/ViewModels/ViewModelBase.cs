using CommunityToolkit.Mvvm.ComponentModel;
using RoomieSync.App.Services;
using RoomieSync.Core.Common;

namespace RoomieSync.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    
    protected async Task RunAsync(Func<Task> action, IToastService? toast = null)
    {
        try
        {
            IsBusy = true;
            await action();
        }
        catch (DomainException ex)
        {
            toast?.Show(ex.Message, ToastKind.Error);
        }
        catch (Exception ex)
        {
            toast?.Show("Something went wrong: " + ex.Message, ToastKind.Error);
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}


public abstract class PageViewModel : ViewModelBase
{
    
    public abstract string TitleKey { get; }

    
    public virtual string CaptionKey => "S.Common.Empty";

    
    public abstract NavSection Section { get; }

    public abstract Task LoadAsync(object? parameter);
}

public enum NavSection { Dashboard, Expenses, Settlements, Chores, Reports, Settings }
