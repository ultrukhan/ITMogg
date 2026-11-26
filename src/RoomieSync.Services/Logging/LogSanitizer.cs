namespace RoomieSync.Services.Logging;

/// <summary>
/// Допоміжні методи, щоб у журнали не потрапляли персональні або секретні дані.
/// Паролі, хеші, солі та API-ключі не логуються взагалі; email пишемо лише в замаскованому вигляді.
/// </summary>
public static class LogSanitizer
{
    /// <summary><c>maya@roomiesync.app</c> → <c>m***@roomiesync.app</c>.</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(empty)";
        email = email.Trim();
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        return email[0] + "***" + email[at..];
    }

    /// <summary>Лише ім'я файлу без повного шляху (у шляху може бути ім'я користувача Windows).</summary>
    public static string FileName(string? path)
        => string.IsNullOrWhiteSpace(path) ? "(none)" : Path.GetFileName(path);
}
