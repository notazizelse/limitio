using System.Security.Cryptography;

namespace LimitIO.Core.Security;

/// <summary>PBKDF2-HMACSHA256 password hashing. Never compares raw strings — always constant-time.</summary>
public static class PasswordHasher
{
    public const int SaltSizeBytes = 16;
    public const int KeySizeBytes = 32;

    /// <summary>OWASP-recommended minimum for PBKDF2-HMACSHA256 as of 2024-2025 guidance.</summary>
    public const int DefaultIterations = 310_000;

    public sealed record HashResult(byte[] Hash, byte[] Salt, int Iterations);

    public static HashResult Hash(string password, int iterations = DefaultIterations)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password must not be empty.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySizeBytes);
        return new HashResult(hash, salt, iterations);
    }

    public static bool Verify(string password, byte[] storedHash, byte[] storedSalt, int iterations)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        var computed = Rfc2898DeriveBytes.Pbkdf2(password, storedSalt, iterations, HashAlgorithmName.SHA256, storedHash.Length);
        return CryptographicOperations.FixedTimeEquals(computed, storedHash);
    }
}
