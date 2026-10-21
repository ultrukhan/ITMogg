using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using RoomieSync.App.ViewModels;

namespace RoomieSync.App.Services;

public interface INavigationService
{
    PageViewModel? CurrentPage { get; }
    Task NavigateToAsync<TPage>(object? parameter = null) where TPage : PageViewModel;
    Task GoBackAsync();
    Task RefreshAsync();
    event EventHandler? Navigated;
}


public sealed class NavigationService(IServiceProvider services) : ObservableObject, INavigationService
{
    private readonly Stack<(Type Type, object? Parameter)> _history = new();
    private PageViewModel? _currentPage;

    public PageViewModel? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public event EventHandler? Navigated;

    public Task NavigateToAsync<TPage>(object? parameter = null) where TPage : PageViewModel
        => NavigateAsync(typeof(TPage), parameter, addToHistory: true);

    public async Task GoBackAsync()
    {
        if (_history.Count > 1)
        {
            _history.Pop();
            var (type, parameter) = _history.Peek();
            await NavigateAsync(type, parameter, addToHistory: false);
        }
    }

    public Task RefreshAsync() => CurrentPage?.LoadAsync(null) ?? Task.CompletedTask;

    public void Reset()
    {
        _history.Clear();
        CurrentPage = null;
    }

    private async Task NavigateAsync(Type type, object? parameter, bool addToHistory)
    {
        var page = (PageViewModel)services.GetRequiredService(type);
        if (addToHistory) _history.Push((type, parameter));
        CurrentPage = page;
        Navigated?.Invoke(this, EventArgs.Empty);
        await page.LoadAsync(parameter);
    }
}
