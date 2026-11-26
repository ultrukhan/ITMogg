using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Receipts;

public sealed class MockReceiptParser(GeminiOptions options, IClock clock, ILogger<MockReceiptParser>? logger = null) : IReceiptParser
{
    private readonly ILogger _log = logger ?? NullLogger<MockReceiptParser>.Instance;

    public async Task<ParsedReceipt> ParseReceiptAsync(byte[] image, string mimeType, string? fileName = null, CancellationToken ct = default)
    {
        var name = (fileName ?? string.Empty).ToLowerInvariant();
        _log.LogInformation("Mock receipt parser used for {File} ({Bytes} bytes): Gemini key is not configured or UseMock=true",
            Logging.LogSanitizer.FileName(fileName), image.Length);
        if (name.Contains("slow"))
        {
            await Task.Delay(TimeSpan.FromSeconds(options.TimeoutSeconds), ct);
            _log.LogWarning("Mock receipt recognition timed out after {Timeout} s", options.TimeoutSeconds);
            throw new ReceiptParseException($"Receipt recognition took longer than {options.TimeoutSeconds} seconds.", isTimeout: true);
        }

        await Task.Delay(1200, ct);
        if (name.Contains("fail"))
        {
            _log.LogError("Mock receipt recognition failed for {File}", Logging.LogSanitizer.FileName(fileName));
            throw new ReceiptParseException("Could not recognize the receipt.");
        }

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
        var sum = receipt.Items.Sum(i => i.Price);
        receipt.Total = name.Contains("warn") ? sum + 10m : sum;
        return receipt;
    }
}
