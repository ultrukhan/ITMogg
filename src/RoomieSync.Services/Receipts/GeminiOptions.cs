namespace RoomieSync.Services.Receipts;

public sealed class GeminiOptions
{
    public const string Section = "Gemini";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gemini-2.5-flash";

    public int TimeoutSeconds { get; set; } = 5;

    public bool UseMock { get; set; }

    public string? ResolveApiKey() =>
        !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey : Environment.GetEnvironmentVariable("GEMINI_API_KEY");
}
