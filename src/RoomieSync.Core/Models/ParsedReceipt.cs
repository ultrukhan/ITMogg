namespace RoomieSync.Core.Models;

public sealed class ParsedReceipt
{
    public string? Merchant { get; set; }
    public DateTime? Date { get; set; }
    public decimal? Total { get; set; }
    public ExpenseCategory? Category { get; set; }
    public List<ReceiptLineItem> Items { get; set; } = new();
}

public sealed class ReceiptLineItem
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
