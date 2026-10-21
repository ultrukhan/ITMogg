using System.Globalization;

namespace RoomieSync.Core.Common;

public static class Money
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    public static long ToCents(decimal value) => (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
    public static decimal FromCents(long cents) => cents / 100m;
}

public static class MoneyFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IReadOnlyList<(string Code, string Symbol, string DisplayName)> SupportedCurrencies { get; } = new[]
    {
        ("UAH", "₴", "UAH (₴) — Ukrainian Hryvnia"),
        ("USD", "$", "USD ($) — US Dollar"),
        ("EUR", "€", "EUR (€) — Euro"),
        ("PLN", "zł", "PLN (zł) — Polish Złoty"),
        ("GBP", "£", "GBP (£) — British Pound")
    };

    public static string CurrencyCode { get; private set; } = "USD";
    public static string Symbol { get; private set; } = "$";

    public static event EventHandler? CurrencyChanged;

    public static void SetCurrency(string? code)
    {
        var normalized = (code ?? "USD").Trim().ToUpperInvariant();
        var match = SupportedCurrencies.FirstOrDefault(c => c.Code == normalized);
        if (string.IsNullOrEmpty(match.Code))
        {
            CurrencyCode = "USD";
            Symbol = "$";
        }
        else
        {
            CurrencyCode = match.Code;
            Symbol = match.Symbol;
        }
        CurrencyChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Format(decimal amount)
    {
        var num = amount.ToString("#,0.00", Inv);
        return CurrencyCode is "PLN" ? $"{num} {Symbol}" : $"{Symbol}{num}";
    }

    public static string FormatSigned(decimal amount)
    {
        var sign = amount > 0 ? "+" : amount < 0 ? "-" : "";
        return sign + Format(Math.Abs(amount));
    }
}
