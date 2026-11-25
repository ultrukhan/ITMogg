using System.Text;

namespace RoomieSync.Core.Common;

public static class InputValidator
{
    public const int MinNameLength = 2;
    public const int MaxHouseholdNameLength = 100;
    public const int MaxMemberNameLength = 100;

    public const int MinTitleLength = 2;
    public const int MaxExpenseTitleLength = 200;
    public const int MaxMerchantLength = 200;
    public const int MaxReceiptPathLength = 500;
    public const int MaxChoreTitleLength = 200;
    public const int MaxNotificationMessageLength = 500;
    public const int MaxReportTitleLength = 200;

    public const int MaxEmailLength = 254;
    public const int MaxEmailLocalPartLength = 64;
    public const int MaxDomainLabelLength = 63;

    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    public const decimal MinMoneyAmount = 0.01m;
    public const decimal MaxMoneyAmount = 1_000_000m;

    public const long MaxSingleReceiptBytes = 20L * 1024L * 1024L;
    public const int MaxFutureChoreYears = 5;

    public static readonly DateTime MinAllowedDate = new(2000, 1, 1);

    private static readonly HashSet<string> AllowedReceiptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".pdf"
    };

    private static readonly HashSet<string> AllowedReceiptMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "application/pdf"
    };

    public static void RequireNonEmptyGuid(Guid value, string fieldName)
    {
        if (value == Guid.Empty)
            throw new DomainException($"{fieldName} cannot be empty.");
    }

    public static bool ContainsControlChars(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
                return true;
        }
        return false;
    }

    public static string CollapseWhitespace(string value)
    {
        var trimmed = value.Trim();
        var sb = new StringBuilder(trimmed.Length);
        var prevSpace = false;
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                prevSpace = false;
            }
        }
        return sb.ToString();
    }

    public static string ValidateHouseholdName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("Household name is required.");

        if (ContainsControlChars(raw))
            throw new DomainException("Household name contains invalid control characters.");

        var normalized = CollapseWhitespace(raw);
        if (normalized.Length < MinNameLength || normalized.Length > MaxHouseholdNameLength)
            throw new DomainException($"Household name must be between {MinNameLength} and {MaxHouseholdNameLength} characters.");

        if (!normalized.Any(char.IsLetter))
            throw new DomainException("Household name must contain at least one letter.");

        if (!normalized.All(IsAllowedHouseholdNameChar))
            throw new DomainException("Household name contains unsupported special characters.");

        return normalized;
    }

    public static string ValidateMemberName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("Member name is required.");

        if (ContainsControlChars(raw))
            throw new DomainException("Member name contains invalid control characters.");

        var normalized = CollapseWhitespace(raw);
        if (normalized.Length < MinNameLength || normalized.Length > MaxMemberNameLength)
            throw new DomainException($"Member name must be between {MinNameLength} and {MaxMemberNameLength} characters.");

        if (!normalized.Any(char.IsLetter))
            throw new DomainException("Member name must contain at least one letter.");

        if (!normalized.All(IsAllowedMemberNameChar))
            throw new DomainException("Member name can only contain letters, digits, spaces, hyphens, apostrophes, and periods.");

        return normalized;
    }

    public static bool IsValidEmail(string? raw) => TryNormalizeEmail(raw, out _, out _);

    public static string ValidateEmail(string? raw)
    {
        if (!TryNormalizeEmail(raw, out var normalized, out var error))
            throw new DomainException(error);
        return normalized;
    }

    public static bool TryNormalizeEmail(string? raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Email address is required.";
            return false;
        }

        var email = raw.Trim();
        if (email.Length > MaxEmailLength)
        {
            error = $"Email address cannot exceed {MaxEmailLength} characters.";
            return false;
        }

        if (email.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            error = "Email address cannot contain spaces or control characters.";
            return false;
        }

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex != email.LastIndexOf('@') || atIndex == email.Length - 1)
        {
            error = "Enter a valid email address.";
            return false;
        }

        var local = email[..atIndex];
        var domain = email[(atIndex + 1)..];

        if (local.Length > MaxEmailLocalPartLength
            || local.StartsWith('.')
            || local.EndsWith('.')
            || local.Contains("..")
            || !local.All(IsAllowedEmailLocalChar))
        {
            error = "Enter a valid email address.";
            return false;
        }

        var labels = domain.Split('.');
        if (labels.Length < 2 || labels.Any(l => !IsValidDomainLabel(l)))
        {
            error = "Enter a valid email address.";
            return false;
        }

        var tld = labels[^1];
        if (tld.Length < 2 || !tld.All(char.IsLetter))
        {
            error = "Email domain must end with a valid top-level domain (e.g. .com, .ua).";
            return false;
        }

        normalized = email;
        error = string.Empty;
        return true;
    }

    public static void ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
            throw new DomainException("Password is required.");

        if (password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
            throw new DomainException($"Password must be between {MinPasswordLength} and {MaxPasswordLength} characters.");

        if (password.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new DomainException("Password cannot contain spaces or control characters.");

        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            throw new DomainException("Password must include at least one uppercase letter, one lowercase letter, and one digit.");
    }

    public static string ValidateCurrencyCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("Currency code is required.");

        var code = raw.Trim().ToUpperInvariant();
        if (!MoneyFormat.SupportedCurrencies.Any(c => c.Code == code))
            throw new DomainException($"Unsupported currency code \"{code}\".");

        return code;
    }

    public static bool HasValidScale(decimal value, int maxDecimals = 2)
        => decimal.Round(value, maxDecimals) == value;

    public static void ValidatePositiveMoney(decimal amount, string fieldName)
    {
        if (amount < MinMoneyAmount)
            throw new DomainException($"{fieldName} must be at least {MinMoneyAmount:0.00}.");
        if (amount > MaxMoneyAmount)
            throw new DomainException($"{fieldName} cannot exceed {MaxMoneyAmount:#,0.00}.");
        if (!HasValidScale(amount))
            throw new DomainException($"{fieldName} cannot have more than 2 decimal places.");
    }

    public static string ValidateChoreTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("Task name is required.");

        if (ContainsControlChars(raw))
            throw new DomainException("Task name contains invalid control characters.");

        var normalized = CollapseWhitespace(raw);
        if (normalized.Length < MinTitleLength || normalized.Length > MaxChoreTitleLength)
            throw new DomainException($"Task name must be between {MinTitleLength} and {MaxChoreTitleLength} characters.");

        if (!normalized.Any(char.IsLetterOrDigit))
            throw new DomainException("Task name must contain at least one letter or digit.");

        return normalized;
    }

    public static string ValidateNotificationMessage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("Notification message is required.");

        if (ContainsControlChars(raw))
            throw new DomainException("Notification message contains invalid control characters.");

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxNotificationMessageLength)
            throw new DomainException($"Notification message cannot exceed {MaxNotificationMessageLength} characters.");

        return trimmed;
    }

    public static bool IsValidReceiptPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > MaxReceiptPathLength
            || ContainsControlChars(path)
            || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return false;

        var fileName = Path.GetFileName(path.Trim());
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        return AllowedReceiptExtensions.Contains(Path.GetExtension(fileName));
    }

    public static void ValidateReceiptPayload(byte[]? image, string? mimeType)
    {
        if (image is not { Length: > 0 })
            throw new DomainException("Receipt file cannot be empty.");

        if (image.LongLength > MaxSingleReceiptBytes)
            throw new DomainException("Receipt file exceeds the 20 MB maximum size.");

        if (string.IsNullOrWhiteSpace(mimeType) || !AllowedReceiptMimeTypes.Contains(mimeType.Trim()))
            throw new DomainException("Unsupported receipt MIME type. Allowed: JPEG, PNG, or PDF.");
    }

    public static string ValidateFilePathWithExtension(string? path, string expectedExtension)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new DomainException("File path is required.");

        var trimmed = path.Trim();
        if (trimmed.Length > MaxReceiptPathLength
            || ContainsControlChars(trimmed)
            || trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new DomainException("File path contains invalid characters or is too long.");

        var fileName = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new DomainException("File name is invalid.");

        if (!string.Equals(Path.GetExtension(fileName), expectedExtension, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"File must have a {expectedExtension} extension.");

        return trimmed;
    }

    public static bool HasValidImageSignature(ReadOnlySpan<byte> header, string extension)
    {
        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            return header.Length >= 8
                   && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                   && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
        }

        return (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
               && header.Length >= 3
               && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
    }

    private static bool IsAllowedHouseholdNameChar(char ch)
        => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '\'' or '’' or '.' or ',' or '&' or '#' or '/' or '(' or ')' or '!' or '+';

    private static bool IsAllowedMemberNameChar(char ch)
        => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '\'' or '’' or '.';

    private static bool IsAllowedEmailLocalChar(char ch)
        => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '%' or '+' or '-';

    private static bool IsValidDomainLabel(string label)
        => label.Length is >= 1 and <= MaxDomainLabelLength
           && !label.StartsWith('-')
           && !label.EndsWith('-')
           && label.All(c => char.IsLetterOrDigit(c) || c == '-');
}
