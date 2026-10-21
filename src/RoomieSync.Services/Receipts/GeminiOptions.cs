namespace RoomieSync.Services.Receipts;

public sealed class GeminiOptions
{
    public const string Section = "Gemini";

    /// <summary>Ключ Gemini API. Якщо порожній — береться зі змінної середовища GEMINI_API_KEY; якщо і там немає — використовується мок.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gemini-2.5-flash";

    /// <summary>NFR: очікування відповіді не більше 5 секунд.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Примусово використовувати мок навіть за наявності ключа (для демо).</summary>
    public bool UseMock { get; set; }

    public string? ResolveApiKey() =>
        !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey : Environment.GetEnvironmentVariable("GEMINI_API_KEY");
}
