using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Receipts;

/// <summary>
/// Мок розпізнавання для демо без ключа Gemini.
/// Підказка для демонстрації сценаріїв: файл, у назві якого є "fail" — помилка, "slow" — таймаут (5 с),
/// "warn" — дані з розбіжністю суми (показ попереджень у формі перевірки).
/// </summary>
public sealed class MockReceiptParser(GeminiOptions options, IClock clock) : IReceiptParser
{
    public async Task<ParsedReceipt> ParseReceiptAsync(byte[] image, string mimeType, string? fileName = null, CancellationToken ct = default)
    {
        var name = (fileName ?? string.Empty).ToLowerInvariant();
        if (name.Contains("slow"))
        {
            await Task.Delay(TimeSpan.FromSeconds(options.TimeoutSeconds), ct);
            throw new ReceiptParseException($"Receipt recognition took longer than {options.TimeoutSeconds} seconds.", isTimeout: true);
        }

        await Task.Delay(1200, ct);
        if (name.Contains("fail")) throw new ReceiptParseException("Could not recognize the receipt.");

        var receipt = new ParsedReceipt
        {
            Merchant = "Whole Foods Market",
            Date = clock.Now.Date,
            Category = ExpenseCategory.Groceries,
            Items =
            {
                new ReceiptLineItem { Name = "Organic Milk 1gal", Price = 6.49m },
                new ReceiptLineItem { Name = "Sourdough Bread", Price = 5.99m },
                new ReceiptLineItem { Name = "Avocados (4)", Price = 7.96m },
                new ReceiptLineItem { Name = "Chicken Breast 2lb", Price = 13.58m },
                new ReceiptLineItem { Name = "Pasta & Sauce", Price = 8.47m },
                new ReceiptLineItem { Name = "Coffee Beans", Price = 14.99m },
                new ReceiptLineItem { Name = "Fresh Vegetables", Price = 7.17m },
            }
        };
        receipt.Total = receipt.Items.Sum(i => i.Price);
        if (name.Contains("warn")) receipt.Total += 10m;
        return receipt;
    }
}
