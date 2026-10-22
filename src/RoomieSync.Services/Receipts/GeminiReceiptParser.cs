using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Receipts;

public sealed class GeminiReceiptParser(HttpClient http, GeminiOptions options) : IReceiptParser
{
    private const string Prompt = """
        You are a receipt parser. Extract data from this receipt photo and answer ONLY with JSON:
        {"merchant": string|null, "date": "yyyy-MM-dd"|null, "total": number|null,
         "category": "Rent"|"Groceries"|"Utilities"|"Entertainment"|"Other",
         "items": [{"name": string, "price": number}]}
        """;

    public async Task<ParsedReceipt> ParseReceiptAsync(byte[] image, string mimeType, string? fileName = null, CancellationToken ct = default)
    {
        var key = options.ResolveApiKey();
        if (string.IsNullOrWhiteSpace(key)) throw new ReceiptParseException("Gemini API key is not configured.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = Prompt },
                        new { inline_data = new { mime_type = mimeType, data = Convert.ToBase64String(image) } }
                    }
                }
            },
            generationConfig = new { responseMimeType = "application/json", temperature = 0 }
        };

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{options.Model}:generateContent";
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            request.Headers.Add("x-goog-api-key", key);
            using var response = await http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
                throw new ReceiptParseException($"Gemini API returned {(int)response.StatusCode}.");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
            var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            return ParseJson(text ?? "{}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ReceiptParseException($"Receipt recognition took longer than {options.TimeoutSeconds} seconds.", isTimeout: true);
        }
        catch (ReceiptParseException) { throw; }
        catch (Exception ex)
        {
            throw new ReceiptParseException("Could not recognize the receipt.", inner: ex);
        }
    }

    internal static ParsedReceipt ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var result = new ParsedReceipt
        {
            Merchant = root.TryGetProperty("merchant", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null,
            Total = root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDecimal() : null,
        };
        if (root.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String
            && DateTime.TryParseExact(d.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            result.Date = date;
        if (root.TryGetProperty("category", out var c) && Enum.TryParse<ExpenseCategory>(c.GetString(), true, out var cat))
            result.Category = cat;
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var it in items.EnumerateArray())
                result.Items.Add(new ReceiptLineItem
                {
                    Name = it.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Price = it.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : 0
                });
        return result;
    }
}
