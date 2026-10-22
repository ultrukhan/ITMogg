using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Receipts;

public sealed class ReceiptValidationService(IClock clock) : IValidationService
{
    public ValidationResult ValidateReceipt(ParsedReceipt receipt)
    {
        var r = new ValidationResult();
        if (receipt.Total is null or <= 0) r.Warning("Total amount was not recognized.");
        if (string.IsNullOrWhiteSpace(receipt.Merchant)) r.Warning("Store name was not recognized.");
        if (receipt.Date is null) r.Warning("Date was not recognized.");
        else if (receipt.Date.Value.Date > clock.Now.Date) r.Warning("Recognized date is in the future.");
        else if (receipt.Date.Value < clock.Now.AddYears(-1)) r.Warning("Recognized date is more than a year ago.");

        if (receipt.Items.Count > 0 && receipt.Total is { } total)
        {
            var sum = receipt.Items.Sum(i => i.Price);
            if (Math.Abs(sum - total) > 0.05m)
                r.Warning($"Items add up to {MoneyFormat.Format(sum)}, but the total is {MoneyFormat.Format(total)}.");
        }
        return r;
    }
}
