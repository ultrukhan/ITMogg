using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RoomieSync.App.Localization;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels.Items;


public sealed class ChartSlice
{
	public string Label { get; init; } = string.Empty;
	public decimal Value { get; init; }
	public Brush? Brush { get; init; }
	public double Percent { get; init; }
}


public static class CategoryVisuals
{
	public static string Glyph(ExpenseCategory c) => c switch
	{
		ExpenseCategory.Rent => "?",
		ExpenseCategory.Groceries => "?",
		ExpenseCategory.Utilities => "?",
		ExpenseCategory.Entertainment => "?",
		_ => "?"
	};

	public static Color Color(ExpenseCategory c) => c switch
	{
		ExpenseCategory.Rent => System.Windows.Media.Color.FromRgb(0xF0, 0x7B, 0x3F),
		ExpenseCategory.Groceries => System.Windows.Media.Color.FromRgb(0x2E, 0x9E, 0x5B),
		ExpenseCategory.Utilities => System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6),
		ExpenseCategory.Entertainment => System.Windows.Media.Color.FromRgb(0xA7, 0x8B, 0xFA),
		_ => System.Windows.Media.Color.FromRgb(0xF6, 0xB9, 0x8E)
	};

	public static Brush Brush(ExpenseCategory c) => Frozen(new SolidColorBrush(Color(c)));

	public static Brush LightBrush(ExpenseCategory c)
	{
		var col = Color(c);
		return Frozen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, col.R, col.G, col.B)));
	}

	public static string Name(ExpenseCategory c) => Loc.Get($"S.Category.{c}");

	private static Brush Frozen(Brush b) { b.Freeze(); return b; }
}

public sealed record CategoryOption(ExpenseCategory? Value, string Name)
{
	public override string ToString() => Name;
}

public sealed record MemberOption(Member? Member, string Name)
{
	public override string ToString() => Name;
}

public sealed class ExpenseRow
{
	public ExpenseRow(Expense e, Member? payer, Guid currentMemberId, DateTime today)
	{
		Expense = e;
		Payer = payer;
		YourShare = e.ShareOf(currentMemberId);
		RelativeDate = (today - e.Date.Date).Days switch
		{
			0 => Loc.Get("S.Date.Today"),
			1 => Loc.Get("S.Date.Yesterday"),
			var d and > 1 and < 7 => Loc.Format("S.Date.DaysAgo", d),
			_ => e.Date.ToString("MMM d", System.Globalization.CultureInfo.GetCultureInfo("en-US"))
		};
	}

	public Expense Expense { get; }
	public Member? Payer { get; }
	public string Title => Expense.Title;
	public decimal Amount => Expense.Amount;
	public decimal YourShare { get; }
	public string DateText => Expense.Date.ToString("MMM dd, yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
	public string RelativeDate { get; }
	public string PaidByText => Loc.Format("S.Expense.PaidBy", Payer?.FirstName ?? "?");
	public string Glyph => CategoryVisuals.Glyph(Expense.Category);
	public Brush CategoryBrush => CategoryVisuals.Brush(Expense.Category);
	public Brush CategoryLightBrush => CategoryVisuals.LightBrush(Expense.Category);
	public string CategoryName => CategoryVisuals.Name(Expense.Category);
}

public sealed partial class ChoreItem : ObservableObject
{
	public ChoreItem(Chore chore, Member? assignee, DateTime now)
	{
		Chore = chore;
		Assignee = assignee;
		IsOverdue = chore.IsOverdue(now);
		DeadlineText = FormatDeadline(chore.Deadline, now);
	}

	public Chore Chore { get; }
	public Member? Assignee { get; }
	public string Title => Chore.Title;
	public ChoreStatus Status => Chore.Status;
	public bool IsOverdue { get; }
	public string DeadlineText { get; }
	public bool HasRotation => Chore.Rotation != ChoreRotation.None;
	public string RotationText => Loc.Get($"S.Rotation.{Chore.Rotation}");
	public string StatusText => Loc.Get($"S.Status.{Chore.Status}");
	public string AssigneeName => Assignee?.FirstName ?? Loc.Get("S.Chores.Unassigned");

	
	public string StatusGlyph => Chore.Status switch
	{
		ChoreStatus.ToDo => "?",
		ChoreStatus.InProgress => "?",
		_ => "?"
	};

	public string NextStatusHint => Chore.Status switch
	{
		ChoreStatus.ToDo => Loc.Get("S.Chores.HintStart"),
		ChoreStatus.InProgress => Loc.Get("S.Chores.HintDone"),
		_ => Loc.Get("S.Chores.HintReopen")
	};

	public static string FormatDeadline(DateTime? deadline, DateTime now)
	{
		if (deadline is not { } d) return Loc.Get("S.Chores.NoDeadline");
		var en = System.Globalization.CultureInfo.GetCultureInfo("en-US");
		var days = (d.Date - now.Date).Days;
		return days switch
		{
			0 => Loc.Format("S.Date.TodayAt", d.ToString("h:mm tt", en)),
			1 => Loc.Get("S.Date.Tomorrow"),
			-1 => Loc.Get("S.Date.Yesterday"),
			> 1 and < 7 => d.ToString("ddd, MMM d", en),
			_ => d.ToString("MMM d, yyyy", en)
		};
	}
}

public sealed class TransferItem(Transfer transfer, Member? from, Member? to)
{
	public Transfer Transfer { get; } = transfer;
	public Member? From { get; } = from;
	public Member? To { get; } = to;
	public decimal Amount => Transfer.Amount;
	public string FromName => From?.FirstName ?? "?";
	public string ToName => To?.FirstName ?? "?";
}

public sealed class BalanceItem(Member member, decimal balance, decimal maxAbs)
{
	public Member Member { get; } = member;
	public decimal Balance { get; } = balance;
	public bool IsPositive => Balance > 0;
	public bool IsNegative => Balance < 0;
	
	public double BarRatio { get; } = maxAbs == 0 ? 0 : (double)(Math.Abs(balance) / maxAbs);
	public string StatusText => Balance switch
	{
		> 0 => Loc.Get("S.Balance.IsOwed"),
		< 0 => Loc.Get("S.Balance.Owes"),
		_ => Loc.Get("S.Balance.SettledUp")
	};
}

public sealed class NotificationItem(Notification n, DateTime now)
{
	public Notification Notification { get; } = n;
	public string Message => Notification.Message;
	public bool IsUnread => !Notification.IsRead;
	public string TimeText { get; } = (now - n.CreatedAt) switch
	{
		var t when t.TotalMinutes < 1 => Loc.Get("S.Date.JustNow"),
		var t when t.TotalHours < 1 => Loc.Format("S.Date.MinutesAgo", (int)t.TotalMinutes),
		var t when t.TotalDays < 1 => Loc.Format("S.Date.HoursAgo", (int)t.TotalHours),
		var t => Loc.Format("S.Date.DaysAgo", (int)t.TotalDays)
	};

	public string Glyph => Notification.Type switch
	{
		NotificationType.NewExpense => "?",
		NotificationType.OverdueChore => "?",
		NotificationType.SettlementRequest => "?",
		NotificationType.SettlementRecorded => "?",
		_ => "?"
	};

	public Brush AccentBrush => Notification.Type switch
	{
		NotificationType.OverdueChore => Brushes.IndianRed,
		NotificationType.SettlementRecorded => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x9E, 0x5B)),
		NotificationType.SettlementRequest => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x5C, 0xF6)),
		_ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0x7B, 0x3F))
	};
}
