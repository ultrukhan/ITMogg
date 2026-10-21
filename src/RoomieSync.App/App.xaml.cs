using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoomieSync.App.Localization;
using RoomieSync.App.Services;
using RoomieSync.App.ViewModels;
using RoomieSync.App.ViewModels.Pages;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Data;
using RoomieSync.Services;

namespace RoomieSync.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "RoomieSync", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Loc.SetLanguage("en");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true) // локальн≥ секрети (ключ Gemini, пароль Ѕƒ) Ч не ком≥тити
            .AddEnvironmentVariables("ROOMIESYNC_")
            .Build();

        Services = ConfigureServices(configuration);

        var window = new MainWindow { DataContext = Services.GetRequiredService<MainViewModel>() };
        MainWindow = window;
        window.Show();

        var navigator = Services.GetRequiredService<IAppNavigator>();
        try
        {
            await Services.InitializeRoomieSyncDataAsync();
            var exists = await Services.GetRequiredService<IHouseholdService>().AnyHouseholdExistsAsync();
            if (exists) await navigator.ShowLoginAsync();
            else await navigator.ShowCreateHouseholdAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("S.App.DataError", ex.Message), "RoomieSync", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static ServiceProvider ConfigureServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);

        services.AddRoomieSyncData(configuration);      
        services.AddRoomieSyncServices(configuration);  

        
        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IAppNavigator, AppNavigator>();

        
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<CreateHouseholdViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ExpensesViewModel>();
        services.AddTransient<AddExpenseViewModel>();
        services.AddTransient<SettlementsViewModel>();
        services.AddTransient<ChoresViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}
