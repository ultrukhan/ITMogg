using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.Services;
using RoomieSync.App.ViewModels.Dialogs;
using RoomieSync.App.ViewModels.Items;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels.Pages;

public sealed partial class DashboardViewModel(
    ISessionContext session,
    IExpenseService expenses,
    IBalanceService balances,
    IChoreService chores,
    IHouseholdService households,
    INavigationService navigation,
    IDialogService dialogs,
    IToastService toast,
    IClock clock) : PageViewModel
{
    public override string TitleKey => "S.Dashboard.Title";
    public override string CaptionKey => "S.Dashboard.Caption";
    public override NavSection Section => NavSection.Dashboard;

    [ObservableProperty] private decimal _myBalance;
    [ObservableProperty] private decimal _totalThisMonth;
    [ObservableProperty] private double _changeVsLastMonth;
    [ObservableProperty] private bool _hasLastMonth;
    [ObservableProperty] private bool _hasExpenses;

    public bool IsOwed => MyBalance >= 0;
    public string BalanceStatus => MyBalance switch
    {
        > 0 => Loc.Get("S.Dashboard.YouAreOwed"),
        < 0 => Loc.Get("S.Dashboard.YouOwe"),
        _ => Loc.Get("S.Dashboard.AllSettled")
    };
    public bool IsIncrease => ChangeVsLastMonth > 0;
    public string ChangeText => Loc.Format("S.Dashboard.VsLastMonth", Math.Abs(ChangeVsLastMonth).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

    public ObservableCollection<ChartSlice> Categories { get; } = new();
    public ObservableCollection<ExpenseRow> RecentExpenses { get; } = new();
    public ObservableCollection<ChoreItem> ActiveChores { get; } = new();

    partial void OnMyBalanceChanged(decimal value)
    {
        OnPropertyChanged(nameof(IsOwed));
        OnPropertyChanged(nameof(BalanceStatus));
    }

    partial void OnChangeVsLastMonthChanged(double value)
    {
        OnPropertyChanged(nameof(IsIncrease));
        OnPropertyChanged(nameof(ChangeText));
    }

    public override Task LoadAsync(object? parameter) => RunAsync(async () =>
    {
        if (session.CurrentHousehold is not { } h || session.CurrentMember is not { } me) return;
        MoneyFormat.SetCurrency(h.Currency);
        var now = clock.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        var members = (await households.GetMembersAsync(h.Id)).ToDictionary(m => m.Id);
        var all = await expenses.GetExpensesAsync(h.Id, monthStart.AddMonths(-1));
        var balanceList = await balances.GetBalancesAsync(h.Id);
        var choreList = await chores.GetChoresAsync(h.Id);

        MyBalance = balanceList.FirstOrDefault(b => b.MemberId == me.Id)?.Balance ?? 0;

        var thisMonth = all.Where(e => e.Date >= monthStart).ToList();
        var lastMonth = all.Where(e => e.Date < monthStart).Sum(e => e.Amount);
        TotalThisMonth = thisMonth.Sum(e => e.Amount);
        HasLastMonth = lastMonth > 0;
        ChangeVsLastMonth = lastMonth > 0 ? (double)((TotalThisMonth - lastMonth) / lastMonth * 100) : 0;
        HasExpenses = thisMonth.Count > 0;

        Categories.Clear();
        var total = thisMonth.Sum(e => e.Amount);
        foreach (var g in thisMonth.GroupBy(e => e.Category).OrderBy(g => g.Key))
            Categories.Add(new ChartSlice
            {
                Label = CategoryVisuals.Name(g.Key),
                Value = g.Sum(e => e.Amount),
                Brush = CategoryVisuals.Brush(g.Key),
                Percent = total == 0 ? 0 : (double)(g.Sum(e => e.Amount) / total * 100)
            });

        RecentExpenses.Clear();
        foreach (var e in all.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).Take(3))
            RecentExpenses.Add(new ExpenseRow(e, members.GetValueOrDefault(e.PaidByMemberId), me.Id, now.Date));

        ActiveChores.Clear();
        foreach (var c in choreList
                     .Where(c => c.Status != ChoreStatus.Done || c.CompletedAt > now.AddDays(-2))
                     .OrderBy(c => c.Status == ChoreStatus.Done)
                     .ThenBy(c => c.Deadline ?? DateTime.MaxValue)
                     .Take(5))
            ActiveChores.Add(new ChoreItem(c, c.AssigneeId is { } id ? members.GetValueOrDefault(id) : null, now));
    }, toast);

    [RelayCommand]
    private Task AddExpense() => navigation.NavigateToAsync<AddExpenseViewModel>();

    [RelayCommand]
    private Task ViewAllExpenses() => navigation.NavigateToAsync<ExpensesViewModel>();

    [RelayCommand]
    private Task OpenSettlements() => navigation.NavigateToAsync<SettlementsViewModel>();

    
    [RelayCommand]
    private Task OpenReportsThisMonth() => navigation.NavigateToAsync<ReportsViewModel>(ReportPeriod.ThisMonth);

    [RelayCommand]
    private Task OpenChores() => navigation.NavigateToAsync<ChoresViewModel>();

    [RelayCommand]
    private async Task AddChore()
    {
        if (session.CurrentHousehold is not { } h) return;
        var members = await households.GetMembersAsync(h.Id);
        var dialog = new ChoreEditorDialogViewModel(chores, h.Id, members, null, clock);
        if (await dialogs.ShowAsync(dialog))
        {
            toast.Show(Loc.Get("S.Chores.Created"));
            await LoadAsync(null);
        }
    }

    [RelayCommand]
    private async Task AdvanceChore(ChoreItem item)
    {
        var next = item.Status switch
        {
            ChoreStatus.ToDo => ChoreStatus.InProgress,
            ChoreStatus.InProgress => ChoreStatus.Done,
            _ => ChoreStatus.ToDo
        };
        await RunAsync(async () =>
        {
            var rotated = await chores.ChangeStatusAsync(item.Chore.Id, next);
            if (next == ChoreStatus.Done)
                toast.Show(rotated is null ? Loc.Get("S.Chores.MarkedDone") : Loc.Get("S.Chores.MarkedDoneRotated"));
            await LoadAsync(null);
        }, toast);
    }
}
