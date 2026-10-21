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
        var sb = new StringBuilder();
        sb.AppendLine("Date,Description,Category,Paid By,Total Amount,Your Share");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", r.Date.ToString("yyyy-MM-dd", Inv), Csv(r.Title), Csv(r.Category), Csv(r.PaidBy),
                r.Amount.ToString("0.00", Inv), r.YourShare.ToString("0.00", Inv)));
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true), ct);
    }

    public async Task ExportSettlementPlanCsvAsync(string path, IEnumerable<(string From, string To, decimal Amount)> transfers, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("From,To,Amount");
        foreach (var t in transfers)
            sb.AppendLine(string.Join(",", Csv(t.From), Csv(t.To), t.Amount.ToString("0.00", Inv)));
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true), ct);
    }

    public Task ExportExpensesPdfAsync(string path, string title, IEnumerable<ReportRow> rows, CancellationToken ct = default)
    {
        var list = rows.ToList();
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
        var list = transfers.ToList();
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

    private static string Csv(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "...";
}
