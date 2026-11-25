using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Receipts;

public sealed class ReceiptValidationService(IClock clock) : IValidationService
{
    public ValidationResult ValidateReceipt(ParsedReceipt receipt)
    {
        var r = new ValidationResult();
        if (receipt is null)
            return r.Error("Parsed receipt data is required.");

        if (receipt.Total is null or <= 0)
            r.Warning("Total amount was not recognized.");
        else if (receipt.Total > InputValidator.MaxMoneyAmount)
            r.Error("Recognized receipt total exceeds the maximum allowed amount.");
        else if (!InputValidator.HasValidScale(receipt.Total.Value))
            r.Warning("Recognized receipt total has more than 2 decimal places.");

        if (string.IsNullOrWhiteSpace(receipt.Merchant))
            r.Warning("Store name was not recognized.");
        else if (receipt.Merchant.Length > InputValidator.MaxMerchantLength || InputValidator.ContainsControlChars(receipt.Merchant))
            r.Error("Recognized store name is invalid or exceeds 200 characters.");

        if (receipt.Category is { } category && !Enum.IsDefined(category))
            r.Error("Recognized receipt category is invalid.");

        if (receipt.Date is null)
            r.Warning("Date was not recognized.");
        else if (receipt.Date.Value.Date > clock.Now.Date)
            r.Warning("Recognized date is in the future.");
        else if (receipt.Date.Value < clock.Now.AddYears(-1))
            r.Warning("Recognized date is more than a year ago.");

        if (receipt.Items is null)
            return r.Error("Receipt items list cannot be null.");

        if (receipt.Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Name) || InputValidator.ContainsControlChars(i.Name) || i.Name.Length > InputValidator.MaxExpenseTitleLength))
            r.Warning("One or more recognized line items have invalid names.");

        if (receipt.Items.Count > 0 && receipt.Total is { } total)
        {
            var sum = receipt.Items.Sum(i => i.Price);
            if (Math.Abs(sum - total) > 0.05m)
                r.Warning($"Items add up to {MoneyFormat.Format(sum)}, but the total is {MoneyFormat.Format(total)}.");
        }
        return r;
    }
}
