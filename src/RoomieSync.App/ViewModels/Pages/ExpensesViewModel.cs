using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.Services;
using RoomieSync.App.ViewModels.Dialogs;
using RoomieSync.App.ViewModels.Items;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels.Pages;

public enum DateFilter { AllTime, ThisMonth, Last30Days, Last3Months }

public sealed record PageButton(int Number, bool IsCurrent);

public sealed record DateFilterOption(DateFilter Value, string Name)
{
    public override string ToString() => Name;
}

public sealed partial class ExpensesViewModel : PageViewModel
{
    private const int PageSize = 8;

    private readonly ISessionContext _session;
    private readonly IExpenseService _expenses;
    private readonly IHouseholdService _households;
    private readonly IExportService _export;
    private readonly IFileDialogService _files;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly IClock _clock;
    private List<ExpenseRow> _all = new();
    private Dictionary<Guid, Member> _members = new();
    private bool _loading;

    public ExpensesViewModel(ISessionContext session, IExpenseService expenses, IHouseholdService households, IExportService export,
        IFileDialogService files, INavigationService navigation, IDialogService dialogs, IToastService toast, IClock clock)
    {
        _session = session;
        _expenses = expenses;
        _households = households;
        _export = export;
        _files = files;
        _navigation = navigation;
        _dialogs = dialogs;
        _toast = toast;
        _clock = clock;

        DateFilters = Enum.GetValues<DateFilter>().Select(d => new DateFilterOption(d, Loc.Get($"S.Expenses.Date.{d}"))).ToList();
        Categories = new[] { new CategoryOption(null, Loc.Get("S.Expenses.AllCategories")) }
            .Concat(Enum.GetValues<ExpenseCategory>().Select(c => new CategoryOption(c, CategoryVisuals.Name(c)))).ToList();
        _selectedDateFilter = DateFilters[0];
        _selectedCategory = Categories[0];
    }

    public override string TitleKey => "S.Expenses.Title";
    public override string CaptionKey => "S.Expenses.Caption";
    public override NavSection Section => NavSection.Expenses;

    public IReadOnlyList<DateFilterOption> DateFilters { get; }
    public IReadOnlyList<CategoryOption> Categories { get; }
    public ObservableCollection<MemberOption> People { get; } = new();
    public ObservableCollection<ExpenseRow> Rows { get; } = new();
    public ObservableCollection<PageButton> Pages { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private DateFilterOption _selectedDateFilter;
    [ObservableProperty] private CategoryOption _selectedCategory;
    [ObservableProperty] private MemberOption? _selectedPerson;
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private decimal _total;
    [ObservableProperty] private string _showingText = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter(resetPage: true);
    partial void OnSelectedDateFilterChanged(DateFilterOption value) => ApplyFilter(resetPage: true);
    partial void OnSelectedCategoryChanged(CategoryOption value) => ApplyFilter(resetPage: true);
    partial void OnSelectedPersonChanged(MemberOption? value) => ApplyFilter(resetPage: true);

    public override Task LoadAsync(object? parameter) => RunAsync(async () =>
    {
        if (_session.CurrentHousehold is not { } h || _session.CurrentMember is not { } me) return;
        _loading = true;
        var members = await _households.GetMembersAsync(h.Id);
        _members = members.ToDictionary(m => m.Id);
        var list = await _expenses.GetExpensesAsync(h.Id);
        var today = _clock.Now.Date;
        _all = list.Select(e => new ExpenseRow(e, _members.GetValueOrDefault(e.PaidByMemberId), me.Id, today)).ToList();

        var selectedId = SelectedPerson?.Member?.Id;
        People.Clear();
        People.Add(new MemberOption(null, Loc.Get("S.Expenses.Everyone")));
        foreach (var m in members) People.Add(new MemberOption(m, m.DisplayName));
        SelectedPerson = People.FirstOrDefault(p => p.Member?.Id == selectedId) ?? People[0];
        _loading = false;
        ApplyFilter(resetPage: false);
    }, _toast);

    private IEnumerable<ExpenseRow> Filtered()
    {
        var today = _clock.Now.Date;
        DateTime? from = SelectedDateFilter.Value switch
        {
            DateFilter.ThisMonth => new DateTime(today.Year, today.Month, 1),
            DateFilter.Last30Days => today.AddDays(-30),
            DateFilter.Last3Months => today.AddMonths(-3),
            _ => null
        };
        var q = _all.AsEnumerable();
        if (from is { } f) q = q.Where(r => r.Expense.Date >= f);
        if (SelectedCategory.Value is { } c) q = q.Where(r => r.Expense.Category == c);
        if (SelectedPerson?.Member is { } p) q = q.Where(r => r.Expense.PaidByMemberId == p.Id);
        if (!string.IsNullOrWhiteSpace(SearchText))
            q = q.Where(r => r.Title.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)
                             || (r.Expense.Merchant?.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) ?? false));
        return q;
    }

    private void ApplyFilter(bool resetPage)
    {
        if (_loading) return;
        var filtered = Filtered().ToList();
        TotalCount = filtered.Count;
        Total = filtered.Sum(r => r.Amount);
        var pageCount = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        if (resetPage || CurrentPage > pageCount) CurrentPage = 1;

        Pages.Clear();
        for (var i = 1; i <= pageCount; i++) Pages.Add(new PageButton(i, i == CurrentPage));

        Rows.Clear();
        foreach (var r in filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize)) Rows.Add(r);
        var first = filtered.Count == 0 ? 0 : (CurrentPage - 1) * PageSize + 1;
        ShowingText = Loc.Format("S.Expenses.Showing", first, first + Rows.Count - (Rows.Count > 0 ? 1 : 0), filtered.Count);
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void GoToPage(int page)
    {
        CurrentPage = page;
        ApplyFilter(resetPage: false);
    }

    private bool CanPrevious() => CurrentPage > 1;
    private bool CanNext() => CurrentPage < Pages.Count;

    [RelayCommand(CanExecute = nameof(CanPrevious))]
    private void PreviousPage() => GoToPage(CurrentPage - 1);

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void NextPage() => GoToPage(CurrentPage + 1);

    [RelayCommand]
    private Task AddExpense() => _navigation.NavigateToAsync<AddExpenseViewModel>();

    [RelayCommand]
    private async Task OpenDetails(ExpenseRow row)
    {
        var meId = _session.CurrentMember?.Id ?? Guid.Empty;
        var dialog = new ExpenseDetailsDialogViewModel(row, _members, meId);
        if (!await _dialogs.ShowAsync(dialog) || !dialog.DeleteRequested) return;

        var requiredCount = row.Expense.RequiredApprovers.Count;
        var currentApproved = row.Expense.DeleteApprovedBy.Count;
        var msg = requiredCount > 1
            ? $"Deleting \"{row.Title}\" requires consent from all {requiredCount} split participants (currently {currentApproved}/{requiredCount} approved). Do you confirm your approval to delete?"
            : Loc.Format("S.Expenses.DeleteMessage", row.Title);

        if (!await _dialogs.ConfirmAsync(Loc.Get("S.Expenses.DeleteTitle"), msg, Loc.Get("S.Common.Delete"), danger: true))
            return;

        await RunAsync(async () =>
        {
            var res = await _expenses.RequestOrApproveDeleteAsync(row.Expense.Id, meId);
            if (res.Deleted)
                _toast.Show(Loc.Get("S.Expenses.Deleted"));
            else
                _toast.Show($"Delete approval recorded ({res.ApprovedCount}/{res.RequiredCount}). Waiting for other roommates.");
            await LoadAsync(null);
        }, _toast);
    }

    private IEnumerable<ReportRow> ExportRows() => Filtered().Select(r =>
        new ReportRow(r.Expense.Date, r.Title, r.CategoryName, r.Payer?.DisplayName ?? "?", r.Amount, r.YourShare));

    [RelayCommand]
    private async Task ExportCsv()
    {
        var path = _files.SaveFile($"expenses-{_clock.Now:yyyy-MM-dd}.csv", "CSV file (*.csv)|*.csv");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await _export.ExportExpensesCsvAsync(path, ExportRows());
            _toast.Show(Loc.Format("S.Export.Saved", Path.GetFileName(path)));
        }, _toast);
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        var path = _files.SaveFile($"expenses-{_clock.Now:yyyy-MM-dd}.pdf", "PDF file (*.pdf)|*.pdf");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await _export.ExportExpensesPdfAsync(path, $"{_session.CurrentHousehold?.Name} - {Loc.Get("S.Expenses.Title")}", ExportRows());
            _toast.Show(Loc.Format("S.Export.Saved", Path.GetFileName(path)));
        }, _toast);
    }
}
