namespace RoomieSync.Core.Common;

public sealed class ValidationResult
{
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public IReadOnlyList<string> Errors => _errors;
    public IReadOnlyList<string> Warnings => _warnings;
    public bool IsValid => _errors.Count == 0;
    public bool HasWarnings => _warnings.Count > 0;

    public ValidationResult Error(string message) { _errors.Add(message); return this; }
    public ValidationResult Warning(string message) { _warnings.Add(message); return this; }

    public static ValidationResult Ok() => new();
}

public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
