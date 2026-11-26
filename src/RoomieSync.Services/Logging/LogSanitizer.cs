namespace RoomieSync.Services.Logging;

public static class LogSanitizer
{
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(empty)";
        email = email.Trim();
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        return email[0] + "***" + email[at..];
    }

    public static string FileName(string? path)
        => string.IsNullOrWhiteSpace(path) ? "(none)" : Path.GetFileName(path);
}
