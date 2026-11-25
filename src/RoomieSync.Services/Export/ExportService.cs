using System.Globalization;
using System.Text;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;

namespace RoomieSync.Services.Export;

public sealed class ExportService : IExportService
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task ExportExpensesCsvAsync(string path, IEnumerable<ReportRow> rows, CancellationToken ct = default)
    {
        path = InputValidator.ValidateFilePathWithExtension(path, ".csv");
        var list = ValidateExpenseRows(rows);

        var sb = new StringBuilder();
        sb.AppendLine("Date,Description,Category,Paid By,Total Amount,Your Share");
        foreach (var r in list)
            sb.AppendLine(string.Join(",", r.Date.ToString("yyyy-MM-dd", Inv), Csv(r.Title), Csv(r.Category), Csv(r.PaidBy),
                r.Amount.ToString("0.00", Inv), r.YourShare.ToString("0.00", Inv)));
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true), ct);
    }

    public async Task ExportSettlementPlanCsvAsync(string path, IEnumerable<(string From, string To, decimal Amount)> transfers, CancellationToken ct = default)
    {
        path = InputValidator.ValidateFilePathWithExtension(path, ".csv");
        var list = ValidateTransferRows(transfers);

        var sb = new StringBuilder();
        sb.AppendLine("From,To,Amount");
        foreach (var t in list)
            sb.AppendLine(string.Join(",", Csv(t.From), Csv(t.To), t.Amount.ToString("0.00", Inv)));
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true), ct);
    }

    public Task ExportExpensesPdfAsync(string path, string title, IEnumerable<ReportRow> rows, CancellationToken ct = default)
    {
        path = InputValidator.ValidateFilePathWithExtension(path, ".pdf");
        title = ValidateReportTitle(title);
        var list = ValidateExpenseRows(rows);

        var pdf = new SimplePdfWriter();
        pdf.Heading(title);
        var generatedAt = DateTime.Now.ToString("MMM d, yyyy HH:mm", Inv);
        pdf.Text($"Generated {generatedAt} - {list.Count} expenses - Total {MoneyFormat.Format(list.Sum(r => r.Amount))}");
        pdf.Space();
        var cols = new[] { 0f, 75f, 285f, 370f, 455f };
        pdf.Row(cols, true, "Date", "Description", "Paid by", "Amount", "Your share");
        foreach (var r in list)
            pdf.Row(cols, false, r.Date.ToString("MMM dd, yyyy", Inv), Trim(r.Title, 38), r.PaidBy, MoneyFormat.Format(r.Amount), MoneyFormat.Format(r.YourShare));
        return File.WriteAllBytesAsync(path, pdf.Build(), ct);
    }

    public Task ExportSettlementPlanPdfAsync(string path, string title, IEnumerable<(string From, string To, decimal Amount)> transfers, CancellationToken ct = default)
    {
        path = InputValidator.ValidateFilePathWithExtension(path, ".pdf");
        title = ValidateReportTitle(title);
        var list = ValidateTransferRows(transfers);

        var pdf = new SimplePdfWriter();
        pdf.Heading(title);
        var generatedAt = DateTime.Now.ToString("MMM d, yyyy HH:mm", Inv);
        pdf.Text($"Generated {generatedAt} - {list.Count} transfers - Total {MoneyFormat.Format(list.Sum(t => t.Amount))}");
        pdf.Space();
        var cols = new[] { 0f, 180f, 360f };
        pdf.Row(cols, true, "From", "To", "Amount");
        foreach (var t in list) pdf.Row(cols, false, t.From, t.To, MoneyFormat.Format(t.Amount));
        if (list.Count == 0) pdf.Text("All settled up!");
        return File.WriteAllBytesAsync(path, pdf.Build(), ct);
    }

    private static string ValidateReportTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Report title is required.");
        if (InputValidator.ContainsControlChars(title))
            throw new DomainException("Report title contains invalid control characters.");
        var trimmed = title.Trim();
        if (trimmed.Length > InputValidator.MaxReportTitleLength)
            throw new DomainException($"Report title cannot exceed {InputValidator.MaxReportTitleLength} characters.");
        return trimmed;
    }

    private static List<ReportRow> ValidateExpenseRows(IEnumerable<ReportRow> rows)
    {
        if (rows is null)
            throw new DomainException("Export rows collection is required.");

        var list = rows.ToList();
        foreach (var r in list)
        {
            if (r is null
                || string.IsNullOrWhiteSpace(r.Title) || InputValidator.ContainsControlChars(r.Title)
                || string.IsNullOrWhiteSpace(r.Category) || InputValidator.ContainsControlChars(r.Category)
                || string.IsNullOrWhiteSpace(r.PaidBy) || InputValidator.ContainsControlChars(r.PaidBy))
            {
                throw new DomainException("Export rows contain invalid or empty text fields.");
            }
            InputValidator.ValidatePositiveMoney(r.Amount, "Report row amount");
            if (r.YourShare < 0m || r.YourShare > InputValidator.MaxMoneyAmount || !InputValidator.HasValidScale(r.YourShare))
                throw new DomainException("Report row share must be non-negative and have at most 2 decimal places.");
        }
        return list;
    }

    private static List<(string From, string To, decimal Amount)> ValidateTransferRows(IEnumerable<(string From, string To, decimal Amount)> transfers)
    {
        if (transfers is null)
            throw new DomainException("Transfer rows collection is required.");

        var list = transfers.ToList();
        foreach (var t in list)
        {
            if (string.IsNullOrWhiteSpace(t.From) || InputValidator.ContainsControlChars(t.From)
                || string.IsNullOrWhiteSpace(t.To) || InputValidator.ContainsControlChars(t.To))
            {
                throw new DomainException("Settlement export rows contain invalid participant names.");
            }
            InputValidator.ValidatePositiveMoney(t.Amount, "Settlement export amount");
        }
        return list;
    }

    private static string Csv(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
        return safe.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{safe.Replace("\"", "\"\"")}\"" : safe;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "...";
}
