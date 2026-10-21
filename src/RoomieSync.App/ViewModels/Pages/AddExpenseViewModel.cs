using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.Services;
using RoomieSync.App.ViewModels.Items;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels.Pages;

public sealed partial class MemberSplitItem : ObservableObject
{
    public MemberSplitItem(Member member, string roleLabel)
    {
        Member = member;
        RoleLabel = roleLabel;
    }

    public Member Member { get; }
    public string RoleLabel { get; }

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _valueText = "0.00";
    [ObservableProperty] private bool _isEditable;
    [ObservableProperty] private string _prefix = MoneyFormat.Symbol;
    [ObservableProperty] private decimal _computedAmount;

    public decimal Value => AddExpenseViewModel.ParseDecimal(ValueText) ?? 0m;
}

public sealed record ReceiptItemRow(string Name, decimal Price);

public sealed partial class AddExpenseViewModel : PageViewModel
{
    private readonly ISessionContext _session;
    private readonly IExpenseService _expenses;
    private readonly IHouseholdService _households;
    private readonly IReceiptParser _receiptParser;
    private readonly IValidationService _validation;
    private readonly IFileDialogService _files;
    private readonly INavigationService _navigation;
    private readonly IToastService _toast;
    private readonly IClock _clock;
    private bool _suppressRecalc;

    public AddExpenseViewModel(ISessionContext session, IExpenseService expenses, IHouseholdService households,
        IReceiptParser receiptParser, IValidationService validation, IFileDialogService files,
        INavigationService navigation, IToastService toast, IClock clock)
    {
        _session = session;
        _expenses = expenses;
        _households = households;
        _receiptParser = receiptParser;
        _validation = validation;
        _files = files;
        _navigation = navigation;
        _toast = toast;
        _clock = clock;
        Categories = Enum.GetValues<ExpenseCategory>().Select(c => new CategoryOption(c, CategoryVisuals.Name(c))).ToList();
        _selectedCategory = Categories.First(c => c.Value == ExpenseCategory.Groceries);
        _date = clock.Now.Date;
    }

    public override string TitleKey => "S.AddExpense.Title";
    public override string CaptionKey => "S.AddExpense.Caption";
    public override NavSection Section => NavSection.Expenses;

    public string CurrencySymbol => MoneyFormat.Symbol;
    public IReadOnlyList<CategoryOption> Categories { get; }
    public ObservableCollection<Member> Members { get; } = new();
    public ObservableCollection<MemberSplitItem> Splits { get; } = new();
    public ObservableCollection<ReceiptItemRow> ReceiptItems { get; } = new();
    public ObservableCollection<string> ReceiptWarnings { get; } = new();
    public ObservableCollection<string> Errors { get; } = new();

    [ObservableProperty] private bool _isScanMode;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private DateTime? _date;
    [ObservableProperty] private CategoryOption _selectedCategory;
    [ObservableProperty] private Member? _paidBy;
    [ObservableProperty] private SplitMethod _splitMethod = SplitMethod.Equally;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private decimal _splitTotal;
    [ObservableProperty] private bool _splitMatches = true;

    [ObservableProperty] private string? _receiptPath;
    [ObservableProperty] private string? _receiptFileName;
    [ObservableProperty] private bool _isParsing;
    [ObservableProperty] private bool _hasParsedReceipt;
    [ObservableProperty] private string? _receiptError;
    [ObservableProperty] private string? _merchant;

    public decimal Amount => ParseDecimal(AmountText) ?? 0m;
    public bool IsReceiptImage => ReceiptPath is not null && !ReceiptPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    public string SplitHint => SplitMethod switch
    {
        SplitMethod.Equally => Loc.Get("S.AddExpense.HintEqually"),
        SplitMethod.ByPercentage => Loc.Get("S.AddExpense.HintPercentage"),
        _ => Loc.Get("S.AddExpense.HintExact")
    };
    public string SelectedText => Loc.Format("S.AddExpense.PeopleSelected", SelectedCount);
    public string SplitTotalText => SplitMethod == SplitMethod.ByPercentage
        ? $"{Splits.Where(s => s.IsSelected).Sum(s => s.Value):0.##}% / 100%"
        : $"{MoneyFormat.Format(SplitTotal)} / {MoneyFormat.Format(Amount)}";

    public static decimal? ParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim()
            .Replace("$", "")
            .Replace("?", "")
            .Replace("ˆ", "")
            .Replace("?", "")
            .Replace("z?", "")
            .Replace("%", "")
            .Replace(" ", "")
            .Replace(',', '.');
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public override async Task LoadAsync(object? parameter)
    {
        if (_session.CurrentHousehold is not { } h || _session.CurrentMember is not { } me) return;
        MoneyFormat.SetCurrency(h.Currency);
        OnPropertyChanged(nameof(CurrencySymbol));

        await RunAsync(async () =>
        {
            var members = await _households.GetMembersAsync(h.Id);
            Members.Clear();
            foreach (var m in members) Members.Add(m);
            PaidBy = Members.FirstOrDefault(m => m.Id == me.Id) ?? Members.FirstOrDefault();

            foreach (var s in Splits) s.PropertyChanged -= OnSplitChanged;
            Splits.Clear();
            foreach (var m in members)
            {
                var role = m.Id == me.Id ? Loc.Get("S.AddExpense.You") : m.IsAdmin ? Loc.Get("S.Role.Admin") : Loc.Get("S.AddExpense.Roommate");
                var item = new MemberSplitItem(m, role);
                item.PropertyChanged += OnSplitChanged;
                Splits.Add(item);
            }
            if (parameter is true) IsScanMode = true;
            Recalculate(resetInputs: true);
        }, _toast);
    }

    partial void OnAmountTextChanged(string value)
    {
        OnPropertyChanged(nameof(Amount));
        Recalculate(resetInputs: SplitMethod == SplitMethod.ByExactAmount && Splits.All(s => s.Value == 0));
    }

    partial void OnSplitMethodChanged(SplitMethod value)
    {
        OnPropertyChanged(nameof(SplitHint));
        Recalculate(resetInputs: true);
    }

    partial void OnReceiptPathChanged(string? value) => OnPropertyChanged(nameof(IsReceiptImage));

    private void OnSplitChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressRecalc) return;
        if (e.PropertyName == nameof(MemberSplitItem.IsSelected))
            Recalculate(resetInputs: true);
        else if (e.PropertyName == nameof(MemberSplitItem.ValueText))
            Recalculate(resetInputs: false);
    }

    private void Recalculate(bool resetInputs)
    {
        _suppressRecalc = true;
        try
        {
            var selected = Splits.Where(s => s.IsSelected).ToList();
            SelectedCount = selected.Count;
            var amount = Amount;

            foreach (var s in Splits)
            {
                s.IsEditable = s.IsSelected && SplitMethod != SplitMethod.Equally;
                s.Prefix = SplitMethod == SplitMethod.ByPercentage ? "%" : MoneyFormat.Symbol;
                if (!s.IsSelected)
                {
                    s.ValueText = SplitMethod == SplitMethod.ByPercentage ? "0" : "0.00";
                    s.ComputedAmount = 0;
                }
            }

            if (selected.Count > 0)
            {
                if (SplitMethod == SplitMethod.Equally || resetInputs)
                {
                    var equal = _expenses.CalculateShares(amount, SplitMethod.Equally, selected.Select(s => new ShareInput(s.Member.Id, 0)).ToList());
                    for (var i = 0; i < selected.Count; i++)
                    {
                        selected[i].ValueText = SplitMethod switch
                        {
                            SplitMethod.ByPercentage => Math.Round(100m / selected.Count, 2).ToString("0.##", CultureInfo.InvariantCulture),
                            _ => equal[i].Amount.ToString("0.00", CultureInfo.InvariantCulture)
                        };
                    }
                    if (SplitMethod == SplitMethod.ByPercentage)
                    {
                        var rest = 100m - selected.Sum(s => s.Value);
                        selected[0].ValueText = (selected[0].Value + rest).ToString("0.##", CultureInfo.InvariantCulture);
                    }
                }

                var shares = _expenses.CalculateShares(amount, SplitMethod, selected.Select(s => new ShareInput(s.Member.Id, s.Value)).ToList());
                for (var i = 0; i < selected.Count; i++) selected[i].ComputedAmount = shares[i].Amount;
                SplitTotal = shares.Sum(s => s.Amount);
            }
            else
            {
                SplitTotal = 0;
            }

            SplitMatches = SplitMethod == SplitMethod.ByPercentage
                ? Math.Abs(selected.Sum(s => s.Value) - 100m) < 0.01m && selected.Count > 0
                : selected.Count > 0 && SplitTotal == Money.Round(amount) && amount > 0;
            OnPropertyChanged(nameof(SelectedText));
            OnPropertyChanged(nameof(SplitTotalText));
        }
        finally
        {
            _suppressRecalc = false;
        }
    }

    [RelayCommand]
    private async Task BrowseReceipt()
    {
        var path = _files.OpenReceiptImage();
        if (path is not null) await LoadReceiptAsync(path);
    }

    public async Task LoadReceiptAsync(string path)
    {
        ReceiptError = null;
        ReceiptWarnings.Clear();
        ReceiptItems.Clear();
        HasParsedReceipt = false;
        ReceiptPath = path;
        ReceiptFileName = Path.GetFileName(path);
        IsScanMode = true;

        try
        {
            IsParsing = true;
            var bytes = await File.ReadAllBytesAsync(path);
            var mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".pdf" => "application/pdf",
                _ => "image/jpeg"
            };
            var receipt = await _receiptParser.ParseReceiptAsync(bytes, mime, ReceiptFileName);
            ApplyReceipt(receipt);
        }
        catch (ReceiptParseException ex)
        {
            ReceiptError = (ex.IsTimeout ? Loc.Get("S.AddExpense.ScanTimeout") : Loc.Get("S.AddExpense.ScanFailed")) + " " + Loc.Get("S.AddExpense.EnterManually");
            IsScanMode = false;
        }
        catch (IOException ex)
        {
            ReceiptError = ex.Message;
            IsScanMode = false;
        }
        finally
        {
            IsParsing = false;
        }
    }

    private void ApplyReceipt(ParsedReceipt receipt)
    {
        Merchant = receipt.Merchant;
        if (!string.IsNullOrWhiteSpace(receipt.Merchant) && string.IsNullOrWhiteSpace(Title)) Title = receipt.Merchant!;
        if (receipt.Total is { } total) AmountText = total.ToString("0.00", CultureInfo.InvariantCulture);
        if (receipt.Date is { } d) Date = d;
        if (receipt.Category is { } c) SelectedCategory = Categories.First(x => x.Value == c);
        foreach (var item in receipt.Items) ReceiptItems.Add(new ReceiptItemRow(item.Name, item.Price));
        foreach (var w in _validation.ValidateReceipt(receipt).Warnings) ReceiptWarnings.Add(w);
        HasParsedReceipt = true;
    }

    [RelayCommand]
    private void ClearReceipt()
    {
        ReceiptPath = null;
        ReceiptFileName = null;
        ReceiptItems.Clear();
        ReceiptWarnings.Clear();
        HasParsedReceipt = false;
        ReceiptError = null;
        Merchant = null;
    }

    [RelayCommand]
    private Task Cancel() => _navigation.GoBackAsync();

    [RelayCommand]
    private async Task Save()
    {
        Errors.Clear();
        if (_session.CurrentHousehold is not { } h) return;
        if (ParseDecimal(AmountText) is null && !string.IsNullOrWhiteSpace(AmountText))
        {
            Errors.Add(Loc.Get("S.AddExpense.InvalidAmount"));
            return;
        }

        var selected = Splits.Where(s => s.IsSelected).ToList();
        var request = new NewExpenseRequest(
            Title, Amount, Date ?? _clock.Now.Date, SelectedCategory.Value ?? ExpenseCategory.Other,
            PaidBy?.Id ?? Guid.Empty, SplitMethod,
            selected.Select(s => new ShareInput(s.Member.Id, s.Value)).ToList(),
            Merchant, ReceiptPath);

        var validation = _expenses.Validate(request);
        if (!validation.IsValid)
        {
            foreach (var e in validation.Errors) Errors.Add(e);
            return;
        }

        await RunAsync(async () =>
        {
            await _expenses.AddExpenseAsync(h.Id, request);
            _toast.Show(Loc.Format("S.AddExpense.Saved", MoneyFormat.Format(Amount)));
            await _navigation.NavigateToAsync<DashboardViewModel>();
        }, _toast);
    }
}
