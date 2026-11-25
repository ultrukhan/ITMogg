using System.Security.Cryptography;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;

namespace RoomieSync.Core.Security;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public (string Hash, string Salt) Hash(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length > InputValidator.MaxPasswordLength)
            throw new ArgumentException("Password must be non-empty and at most 128 characters.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public bool Verify(string password, string hash, string salt)
    {
        if (string.IsNullOrEmpty(password) || password.Length > InputValidator.MaxPasswordLength
            || string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(salt))
            return false;

        Span<byte> expected = stackalloc byte[HashSize];
        Span<byte> saltBytes = stackalloc byte[SaltSize];
        if (!Convert.TryFromBase64String(hash, expected, out var hashLen) || hashLen != HashSize
            || !Convert.TryFromBase64String(salt, saltBytes, out var saltLen) || saltLen != SaltSize)
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
