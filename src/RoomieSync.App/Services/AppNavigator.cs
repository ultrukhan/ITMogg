using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using RoomieSync.App.ViewModels;

namespace RoomieSync.App.Services;


public interface IAppNavigator
{
    object? CurrentScreen { get; }
    Task ShowLoginAsync();
    Task ShowCreateHouseholdAsync();
    Task ShowShellAsync();
}

public sealed class AppNavigator(IServiceProvider services) : ObservableObject, IAppNavigator
{
    private object? _currentScreen;

    public object? CurrentScreen
    {
        get => _currentScreen;
        private set => SetProperty(ref _currentScreen, value);
    }

    public Task ShowLoginAsync()
    {
        CurrentScreen = services.GetRequiredService<LoginViewModel>();
        return Task.CompletedTask;
    }

    public Task ShowCreateHouseholdAsync()
    {
        CurrentScreen = services.GetRequiredService<CreateHouseholdViewModel>();
        return Task.CompletedTask;
    }

    public async Task ShowShellAsync()
    {
        var shell = services.GetRequiredService<ShellViewModel>();
        CurrentScreen = shell;
        await shell.InitializeAsync();
    }
}
