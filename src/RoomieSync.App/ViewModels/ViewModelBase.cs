using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using RoomieSync.App.Logging;
using RoomieSync.App.Services;
using RoomieSync.Core.Common;

namespace RoomieSync.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    private ILogger? _logger;

    /// <summary>Логер з категорією конкретної ViewModel (напр. RoomieSync.App.ViewModels.Pages.ExpensesViewModel).</summary>
    protected ILogger Logger => _logger ??= AppLogging.CreateLogger(GetType());

    
    protected async Task RunAsync(Func<Task> action, IToastService? toast = null)
    {
        try
        {
            IsBusy = true;
            await action();
        }
        catch (DomainException ex)
        {
            // Очікувана помилка бізнес-правил (некоректні дані тощо): Warning без стеку.
            Logger.LogWarning("Operation rejected: {Reason}", ex.Message);
            toast?.Show(ex.Message, ToastKind.Error);
        }
        catch (Exception ex)
        {
            // Неочікувана помилка: Error з повним стеком для розбору.
            Logger.LogError(ex, "Unexpected error in {ViewModel}", GetType().Name);
            toast?.Show("Something went wrong: " + ex.Message, ToastKind.Error);
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
