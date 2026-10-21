using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.ViewModels.Items;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels.Dialogs;

public sealed record ShareRow(Member? Member, string Name, decimal Amount, string? PercentText, bool IsPayer);


public sealed partial class ExpenseDetailsDialogViewModel : DialogViewModel
{
    public ExpenseDetailsDialogViewModel(ExpenseRow row, IReadOnlyDictionary<Guid, Member> members, Guid currentMemberId)
    {
        Row = row;
        Shares = row.Expense.Shares.Select(s =>
        {
            var m = members.GetValueOrDefault(s.MemberId);
            return new ShareRow(m, m?.DisplayName ?? "?", s.Amount, s.Percentage is { } p ? $"{p:0.##}%" : null, s.MemberId == row.Expense.PaidByMemberId);
        }).ToList();
        SplitMethodText = Loc.Get($"S.Split.{row.Expense.SplitMethod}");
    }

    public override string Title => Loc.Get("S.Expenses.Details");
    public override double Width => 480;
    public ExpenseRow Row { get; }
    public IReadOnlyList<ShareRow> Shares { get; }
    public string SplitMethodText { get; }
    public bool DeleteRequested { get; private set; }

    [RelayCommand]
    private void Delete()
    {
        DeleteRequested = true;
        Close(true);
    }
}
