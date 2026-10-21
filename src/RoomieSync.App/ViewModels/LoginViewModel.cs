using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.Services;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Data;
using RoomieSync.Data.Seed;

namespace RoomieSync.App.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly ISessionContext _session;
    private readonly IAppNavigator _navigator;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _lockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _lockedUntil;

    public LoginViewModel(IAuthService auth, ISessionContext session, IAppNavigator navigator, IDialogService dialogs, DataOptions dataOptions)
    {
        _auth = auth;
        _session = session;
        _navigator = navigator;
        _dialogs = dialogs;
        ShowDemoHint = dataOptions.SeedDemoData;
        _lockTimer.Tick += (_, _) => UpdateLockout();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _email = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isLockedOut;

    [ObservableProperty]
    private string? _lockoutMessage;

    public bool ShowDemoHint { get; }
    public string DemoEmail => DemoDataSeeder.DemoAdminEmail;
    public string DemoPassword => DemoDataSeeder.DemoPassword;

    private bool CanSignIn() => !IsLockedOut && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrEmpty(Password);

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignIn()
    {
        ErrorMessage = null;
        await RunAsync(async () =>
        {
            var result = await _auth.LoginAsync(Email, Password);
            switch (result.Status)
            {
                case LoginStatus.Success:
                    Password = string.Empty;
                    _session.SignIn(result.Member!, result.Household!);
                    await _navigator.ShowShellAsync();
                    break;
                case LoginStatus.LockedOut:
                    Password = string.Empty;
                    StartLockout(result.LockoutRemaining ?? TimeSpan.FromSeconds(60));
                    break;
                default:
                    Password = string.Empty;
                    ErrorMessage = result.AttemptsLeft is > 0 and < 3
                        ? Loc.Format("S.Login.InvalidWithAttempts", result.AttemptsLeft)
                        : Loc.Get("S.Login.Invalid");
                    break;
            }
        });
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private Task ForgotPassword() => _dialogs.AlertAsync(Loc.Get("S.Login.ForgotTitle"), Loc.Get("S.Login.ForgotMessage"));

    [RelayCommand]
    private Task CreateHousehold() => _navigator.ShowCreateHouseholdAsync();

    [RelayCommand]
    private void FillDemo()
    {
        Email = DemoEmail;
        Password = DemoPassword;
    }

    private void StartLockout(TimeSpan remaining)
    {
        ErrorMessage = null;
        _lockedUntil = DateTime.Now + remaining;
        IsLockedOut = true;
        UpdateLockout();
        _lockTimer.Start();
    }

    private void UpdateLockout()
    {
        var left = (int)Math.Ceiling((_lockedUntil - DateTime.Now).TotalSeconds);
        if (left <= 0)
        {
            _lockTimer.Stop();
            IsLockedOut = false;
            LockoutMessage = null;
            return;
        }
        LockoutMessage = Loc.Format("S.Login.Locked", left);
    }
}
