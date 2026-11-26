using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoomieSync.App.Logging;
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

    private static ILogger _log = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables("ROOMIESYNC_")
            .Build();

        AppLogging.Initialize(configuration);
        _log = AppLogging.CreateLogger<App>();
        RegisterGlobalExceptionHandlers();

        Loc.SetLanguage("en");

        Services = ConfigureServices(configuration);
        AppLogging.TrackSession(Services.GetRequiredService<ISessionContext>());
        _log.LogInformation("Data provider: {Provider}; receipt parser: {Parser}",
            configuration["Data:Provider"] ?? "default",
            Services.GetRequiredService<IReceiptParser>().GetType().Name);

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
            _log.LogCritical(ex, "Could not initialize data storage, application will close");
            MessageBox.Show(Loc.Format("S.App.DataError", ex.Message), "RoomieSync", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogging.Shutdown(e.ApplicationExitCode);
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _log.LogError(args.Exception, "Unhandled UI exception");
            MessageBox.Show(args.Exception.Message, "RoomieSync", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _log.LogCritical(args.ExceptionObject as Exception, "Fatal unhandled exception (terminating: {Terminating})", args.IsTerminating);
            if (args.IsTerminating) Serilog.Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log.LogError(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private static ServiceProvider ConfigureServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(AppLogging.AttachTo);

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
