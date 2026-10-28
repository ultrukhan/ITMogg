using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.Services;
using RoomieSync.App.ViewModels.Items;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels.Pages;

public enum ReportPeriod { ThisMonth, Last3Months, Last6Months, ThisYear }

public sealed record ReportPeriodOption(ReportPeriod Value, string Name)
{
    public override string ToString() => Name;
}

public sealed class MemberReportRow(Member member, decimal paid, decimal share)
{
    public Member Member { get; } = member;
    public decimal Paid { get; } = paid;
    public decimal Share { get; } = share;
    public decimal Net => Paid - Share;
}

public sealed partial class ReportsViewModel : PageViewModel
{
    private readonly ISessionContext _session;
    private readonly IExpenseService _expenses;
    private readonly IHouseholdService _households;
    private readonly IExportService _export;
    private readonly IFileDialogService _files;
    private readonly IToastService _toast;
    private readonly IClock _clock;
    private List<ReportRow> _rows = new();
    private bool _suppressReload;

    public ReportsViewModel(ISessionContext session, IExpenseService expenses, IHouseholdService households,
        IExportService export, IFileDialogService files, IToastService toast, IClock clock)
    {
        _session = session;
        _expenses = expenses;
        _households = households;
        _export = export;
        _files = files;
        _toast = toast;
        _clock = clock;
        Periods = Enum.GetValues<ReportPeriod>().Select(p => new ReportPeriodOption(p, Loc.Get($"S.Reports.Period.{p}"))).ToList();
        _selectedPeriod = Periods[2];
    }

    public override string TitleKey => "S.Reports.Title";
    public override string CaptionKey => "S.Reports.Caption";
    public override NavSection Section => NavSection.Reports;

    public IReadOnlyList<ReportPeriodOption> Periods { get; }
    public ObservableCollection<ChartSlice> Monthly { get; } = new();
    public ObservableCollection<ChartSlice> Categories { get; } = new();
    public ObservableCollection<MemberReportRow> MemberRows { get; } = new();

    [ObservableProperty] private ReportPeriodOption _selectedPeriod;
    [ObservableProperty] private decimal _total;
    [ObservableProperty] private decimal _monthlyAverage;
    [ObservableProperty] private int _expenseCount;
    [ObservableProperty] private string _topCategory = "—";

    partial void OnSelectedPeriodChanged(ReportPeriodOption value)
    {
        if (!_suppressReload) _ = LoadAsync(null);
    }

    public override Task LoadAsync(object? parameter) => RunAsync(async () =>
    {
        if (_session.CurrentHousehold is not { } h || _session.CurrentMember is not { } me) return;

        if (parameter is ReportPeriod targetPeriod)
        {
            var found = Periods.FirstOrDefault(p => p.Value == targetPeriod);
            if (found is not null)
            {
                _suppressReload = true;
                SelectedPeriod = found;
                _suppressReload = false;
            }
        }

        var now = _clock.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var from = SelectedPeriod.Value switch
        {
            ReportPeriod.ThisMonth => monthStart,
            ReportPeriod.Last3Months => monthStart.AddMonths(-2),
            ReportPeriod.Last6Months => monthStart.AddMonths(-5),
            _ => new DateTime(now.Year, 1, 1)
        };

        var members = await _households.GetMembersAsync(h.Id);
        var byId = members.ToDictionary(m => m.Id);
        var list = await _expenses.GetExpensesAsync(h.Id, from, now.Date);

        Total = list.Sum(e => e.Amount);
        ExpenseCount = list.Count;
        var months = (now.Year - from.Year) * 12 + now.Month - from.Month + 1;
        MonthlyAverage = months > 0 ? Math.Round(Total / months, 2) : Total;

        Monthly.Clear();
        var en = CultureInfo.GetCultureInfo("en-US");
        for (var m = from; m <= monthStart; m = m.AddMonths(1))
        {
            var mm = m;
            Monthly.Add(new ChartSlice
            {
                Label = m.ToString("MMM", en),
                Value = list.Where(e => e.Date.Year == mm.Year && e.Date.Month == mm.Month).Sum(e => e.Amount),
                Brush = mm == monthStart ? (Brush)new SolidColorBrush(Color.FromRgb(0xF0, 0x7B, 0x3F)) : new SolidColorBrush(Color.FromRgb(0xF6, 0xB9, 0x8E))
            });
        }

        Categories.Clear();
        foreach (var g in list.GroupBy(e => e.Category).OrderByDescending(g => g.Sum(e => e.Amount)))
            Categories.Add(new ChartSlice
            {
                Label = CategoryVisuals.Name(g.Key),
                Value = g.Sum(e => e.Amount),
                Brush = CategoryVisuals.Brush(g.Key),
                Percent = Total == 0 ? 0 : (double)(g.Sum(e => e.Amount) / Total * 100)
            });
        TopCategory = Categories.FirstOrDefault()?.Label ?? "—";

        MemberRows.Clear();
        foreach (var m in members)
            MemberRows.Add(new MemberReportRow(m,
                list.Where(e => e.PaidByMemberId == m.Id).Sum(e => e.Amount),
                list.Sum(e => e.ShareOf(m.Id))));

        _rows = list.Select(e => new ReportRow(e.Date, e.Title, CategoryVisuals.Name(e.Category),
            byId.GetValueOrDefault(e.PaidByMemberId)?.DisplayName ?? "?", e.Amount, e.ShareOf(me.Id))).ToList();
    }, _toast);

    [RelayCommand]
    private async Task ExportCsv()
    {
        var path = _files.SaveFile($"report-{_clock.Now:yyyy-MM-dd}.csv", "CSV file (*.csv)|*.csv");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await _export.ExportExpensesCsvAsync(path, _rows);
            _toast.Show(Loc.Format("S.Export.Saved", Path.GetFileName(path)));
        }, _toast);
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        var path = _files.SaveFile($"report-{_clock.Now:yyyy-MM-dd}.pdf", "PDF file (*.pdf)|*.pdf");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await _export.ExportExpensesPdfAsync(path, $"{_session.CurrentHousehold?.Name} - {Loc.Get("S.Reports.Title")} ({SelectedPeriod.Name})", _rows);
            _toast.Show(Loc.Format("S.Export.Saved", Path.GetFileName(path)));
        }, _toast);
    }
}
