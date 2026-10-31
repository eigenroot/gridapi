using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Providers;

public sealed class PayoutOptions
{
    public const string Section = "Payouts";

    /// <summary>Base64 of 32 random bytes. Encrypts payout account numbers at rest.</summary>
    public string EncryptionKey { get; set; } = "";
}

public sealed class PayoutOptionsValidator : IValidateOptions<PayoutOptions>
{
    public ValidateOptionsResult Validate(string? name, PayoutOptions o) =>
        PayoutProtector.TryDecodeKey(o.EncryptionKey, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Payouts:EncryptionKey must be base64 of exactly 32 bytes.");
}

/// <summary>
/// AES-256-GCM for payout account numbers, plus an HMAC fingerprint (from a key derived from the
/// same secret) so a resubmitted number can be matched without decrypting anything.
/// </summary>
public sealed class PayoutProtector
{
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _encryptionKey;
    private readonly byte[] _fingerprintKey;

    public PayoutProtector(IOptions<PayoutOptions> options)
    {
        if (!TryDecodeKey(options.Value.EncryptionKey, out var key))
            throw new InvalidOperationException("Payouts:EncryptionKey is invalid.");
        _encryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, info: "grid-payout-encryption"u8.ToArray());
        _fingerprintKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, info: "grid-payout-fingerprint"u8.ToArray());
    }

    /// <summary>Layout: version (1) | nonce (12) | tag (16) | ciphertext.</summary>
    public byte[] Encrypt(string accountNumber)
    {
        var plain = Encoding.UTF8.GetBytes(accountNumber);
        var output = new byte[1 + NonceSize + TagSize + plain.Length];
        output[0] = FormatVersion;
        var nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(1 + NonceSize + TagSize), output.AsSpan(1 + NonceSize, TagSize));
        return output;
    }

    public string Decrypt(byte[] protectedValue)
    {
        if (protectedValue.Length < 1 + NonceSize + TagSize || protectedValue[0] != FormatVersion)
            throw new CryptographicException("Unknown payout number format.");
        var cipher = protectedValue.AsSpan(1 + NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Decrypt(protectedValue.AsSpan(1, NonceSize), cipher, protectedValue.AsSpan(1 + NonceSize, TagSize), plain);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Same type and number always give the same fingerprint, for deduplication.</summary>
    public string Fingerprint(PayoutMethodType type, string accountNumber) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_fingerprintKey, Encoding.UTF8.GetBytes($"{type}:{accountNumber}")));

    /// <summary><c>233241234567</c> → <c>********4567</c>.</summary>
    public static string Mask(string accountNumber) =>
        accountNumber.Length <= 4 ? new string('*', accountNumber.Length) : new string('*', accountNumber.Length - 4) + accountNumber[^4..];

    internal static bool TryDecodeKey(string? value, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            key = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return false;
        }
        return key.Length == 32;
    }
}
