namespace RoomieSync.Core.Models;

public enum MemberRole
{
    Member = 0,
    Admin = 1
}

public enum SplitMethod
{
    Equally = 0,
    ByPercentage = 1,
    ByExactAmount = 2
}

public enum ExpenseCategory
{
    Rent = 0,
    Groceries = 1,
    Utilities = 2,
    Entertainment = 3,
    Other = 4
}

public enum ChoreStatus
{
    ToDo = 0,
    InProgress = 1,
    Done = 2
}

public enum ChoreRotation
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}

public enum NotificationType
{
    NewExpense = 0,
    OverdueChore = 1,
    SettlementRequest = 2,
    SettlementRecorded = 3,
    ChoreAssigned = 4
}
