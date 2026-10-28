using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.Services;
using RoomieSync.App.ViewModels.Items;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels.Pages;


public sealed partial class SettlementsViewModel(
    ISessionContext session,
    IBalanceService balances,
    IHouseholdService households,
    IExportService export,
    IFileDialogService files,
    IDialogService dialogs,
    IToastService toast,
    IClock clock) : PageViewModel
{
    public override string TitleKey => "S.Settlements.Title";
    public override string CaptionKey => "S.Settlements.Caption";
    public override NavSection Section => NavSection.Settlements;

    public ObservableCollection<TransferItem> Transfers { get; } = new();
    public ObservableCollection<BalanceItem> Balances { get; } = new();

    [ObservableProperty] private int _transferCount;
    [ObservableProperty] private decimal _totalToSettle;
    [ObservableProperty] private int _transfersSaved;
    [ObservableProperty] private bool _isSettled;

    public string TransfersSavedText => Loc.Format("S.Settlements.FewerFormat", TransfersSaved);
    partial void OnTransfersSavedChanged(int value) => OnPropertyChanged(nameof(TransfersSavedText));

    public override Task LoadAsync(object? parameter) => RunAsync(async () =>
    {
        if (session.CurrentHousehold is not { } h) return;
        var members = (await households.GetMembersAsync(h.Id)).ToDictionary(m => m.Id);
        var plan = await balances.GetSettlementPlanAsync(h.Id);

        Transfers.Clear();
        foreach (var t in plan.Transfers.OrderByDescending(t => t.Amount))
            Transfers.Add(new TransferItem(t, members.GetValueOrDefault(t.FromMemberId), members.GetValueOrDefault(t.ToMemberId)));

        Balances.Clear();
        var maxAbs = plan.Balances.Select(b => Math.Abs(b.Balance)).DefaultIfEmpty(0).Max();
        foreach (var b in plan.Balances.Where(b => members.ContainsKey(b.MemberId)).OrderByDescending(b => b.Balance))
            Balances.Add(new BalanceItem(members[b.MemberId], b.Balance, maxAbs));

        TransferCount = plan.Transfers.Count;
        TotalToSettle = plan.TotalAmount;
        TransfersSaved = plan.TransfersSaved;
        IsSettled = plan.IsSettled;
    }, toast);

    
    [RelayCommand]
    private async Task SettleUp(TransferItem item)
    {
        var ok = await dialogs.ConfirmAsync(
            Loc.Get("S.Settlements.ConfirmTitle"),
            Loc.Format("S.Settlements.ConfirmMessage", item.FromName, MoneyFormat.Format(item.Amount), item.ToName),
            Loc.Get("S.Settlements.MarkPaid"));
        if (!ok || session.CurrentHousehold is not { } h) return;

        await RunAsync(async () =>
        {
            await balances.RecordSettlementAsync(h.Id, item.Transfer);
            await LoadAsync(null);
            if (IsSettled) await dialogs.AlertAsync(Loc.Get("S.Settlements.AllDoneTitle"), Loc.Get("S.Settlements.AllDoneMessage"));
            else toast.Show(Loc.Format("S.Settlements.Recorded", item.FromName, item.ToName));
        }, toast);
    }

    
    [RelayCommand]
    private async Task Remind(TransferItem item)
    {
        if (session.CurrentHousehold is not { } h) return;
        await RunAsync(async () =>
        {
            await balances.RequestSettlementAsync(h.Id, item.Transfer);
            toast.Show($"Reminder sent to {item.FromName}");
        }, toast);
    }

    private IEnumerable<(string, string, decimal)> ExportRows() =>
        Transfers.Select(t => (t.From?.DisplayName ?? "?", t.To?.DisplayName ?? "?", t.Amount));

    [RelayCommand]
    private async Task ExportCsv()
    {
        var path = files.SaveFile($"settlement-plan-{clock.Now:yyyy-MM-dd}.csv", "CSV file (*.csv)|*.csv");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await export.ExportSettlementPlanCsvAsync(path, ExportRows());
            toast.Show(Loc.Format("S.Export.Saved", Path.GetFileName(path)));
        }, toast);
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        var path = files.SaveFile($"settlement-plan-{clock.Now:yyyy-MM-dd}.pdf", "PDF file (*.pdf)|*.pdf");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await export.ExportSettlementPlanPdfAsync(path, $"{session.CurrentHousehold?.Name} — {Loc.Get("S.Settlements.Title")}", ExportRows());
            toast.Show(Loc.Format("S.Export.Saved", Path.GetFileName(path)));
        }, toast);
    }
}
