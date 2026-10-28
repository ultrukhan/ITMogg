using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.Services;
using RoomieSync.App.ViewModels.Items;
using RoomieSync.App.ViewModels.Pages;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels;

public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly ISessionContext _session;
    private readonly IHouseholdService _households;
    private readonly IAuthService _auth;
    private readonly INotificationService _notifications;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly IAppNavigator _appNavigator;
    private readonly IClock _clock;

    public ShellViewModel(
        NavigationService navigation,
        ISessionContext session,
        IHouseholdService households,
        IAuthService auth,
        INotificationService notifications,
        IDialogService dialogs,
        IToastService toast,
        IAppNavigator appNavigator,
        IClock clock)
    {
        Navigation = navigation;
        _session = session;
        _households = households;
        _auth = auth;
        _notifications = notifications;
        _dialogs = dialogs;
        _toast = toast;
        _appNavigator = appNavigator;
        _clock = clock;

        Navigation.Navigated += (_, _) => OnPropertyChanged(nameof(Section));
        _session.Changed += async (_, _) => await RefreshHeaderAsync();
        _notifications.NotificationsChanged += async (_, _) => await LoadNotificationsAsync();
    }

    public NavigationService Navigation { get; }

    public NavSection Section => Navigation.CurrentPage?.Section ?? NavSection.Dashboard;

    public Member? CurrentMember => _session.CurrentMember;
    public string HouseholdName => _session.CurrentHousehold?.Name ?? string.Empty;
    public string RoleText => CurrentMember?.IsAdmin == true ? Loc.Get("S.Role.Admin") : Loc.Get("S.Role.Member");

    [ObservableProperty] private int _memberCount;
    [ObservableProperty] private string _sinceText = string.Empty;
    [ObservableProperty] private bool _isNotificationsOpen;
    [ObservableProperty] private bool _isUserMenuOpen;
    [ObservableProperty] private int _unreadCount;

    public ObservableCollection<NotificationItem> Notifications { get; } = new();

    public async Task InitializeAsync()
    {
        Navigation.Reset();
        if (_session.CurrentHousehold is { } currentH)
            MoneyFormat.SetCurrency(currentH.Currency);

        await RefreshHeaderAsync();

       
        if (_session.CurrentMember is { IsEmailConfirmed: false } unconfirmed && _session.CurrentHousehold is { } hPrompt)
        {
            var email = await _auth.GetEmailAsync(unconfirmed.Id) ?? "your Gmail account";
            var confirmed = await _dialogs.ConfirmAsync(
                "Confirm Household Membership (Gmail)",
                $"You were invited via Gmail ({email}) to join household \"{hPrompt.Name}\". Do you confirm your membership?",
                "Confirm Membership");

            if (confirmed)
            {
                await _auth.ConfirmMembershipAsync(unconfirmed.Id);
                unconfirmed.IsEmailConfirmed = true;
                _session.UpdateMember(unconfirmed);
                _toast.Show("Household membership confirmed via Gmail!");
            }
            else
            {
                await SignOut();
                return;
            }
        }

        if (_session.CurrentHousehold is { } h)
        {
            try { await _notifications.CheckOverdueChoresAsync(h.Id); } catch { }
        }
        await LoadNotificationsAsync();
        await Navigation.NavigateToAsync<DashboardViewModel>();
    }

    private async Task RefreshHeaderAsync()
    {
        OnPropertyChanged(nameof(CurrentMember));
        OnPropertyChanged(nameof(HouseholdName));
        OnPropertyChanged(nameof(RoleText));
        if (_session.CurrentHousehold is not { } h) return;
        MoneyFormat.SetCurrency(h.Currency);
        MemberCount = (await _households.GetMembersAsync(h.Id)).Count;
        SinceText = Loc.Format("S.Shell.Since", h.CreatedAt.Year);
    }

    private async Task LoadNotificationsAsync()
    {
        if (_session.CurrentMember is not { } me) return;
        var list = await _notifications.GetNotificationsAsync(me.Id);
        var now = _clock.Now;
        Notifications.Clear();
        foreach (var n in list.Take(20)) Notifications.Add(new NotificationItem(n, now));
        UnreadCount = list.Count(n => !n.IsRead);
    }

    [RelayCommand]
    private async Task Navigate(NavSection section)
    {
        switch (section)
        {
            case NavSection.Dashboard: await Navigation.NavigateToAsync<DashboardViewModel>(); break;
            case NavSection.Expenses: await Navigation.NavigateToAsync<ExpensesViewModel>(); break;
            case NavSection.Settlements: await Navigation.NavigateToAsync<SettlementsViewModel>(); break;
            case NavSection.Chores: await Navigation.NavigateToAsync<ChoresViewModel>(); break;
            case NavSection.Reports: await Navigation.NavigateToAsync<ReportsViewModel>(); break;
            case NavSection.Settings: await Navigation.NavigateToAsync<SettingsViewModel>(); break;
        }
    }

    [RelayCommand]
    private async Task MarkAllRead()
    {
        if (_session.CurrentMember is { } me) await _notifications.MarkAllReadAsync(me.Id);
    }

    [RelayCommand]
    private async Task SignOut()
    {
        IsUserMenuOpen = false;
        _session.SignOut();
        Navigation.Reset();
        await _appNavigator.ShowLoginAsync();
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        IsUserMenuOpen = false;
        await Navigation.NavigateToAsync<SettingsViewModel>();
    }
}
