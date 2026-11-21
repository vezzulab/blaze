using System.Security.Cryptography;

namespace Blaze.Core.Updates;

/// <summary>Public half of the release-signing key (ECDSA P-256). The private half never lives in the repository.</summary>
public static class ReleaseKey
{
    public const string PublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEbxM/daWSrq7Nhqxk0OT7d8kNwfqH1/XE1ieUodGXLtKwQQZdZo7q3acASfgAmvZNfo6g6Nl2Och6jjH45Qflfg==";
}

/// <summary>
/// Verifies that SHA256SUMS was signed by the Blaze maintainer. A checksum alone only proves the download was not
/// corrupted; the signature proves it was published by whoever holds the private key, even if the repository or the
/// release page were compromised.
/// </summary>
public sealed class ReleaseVerifier(byte[] publicKeySpki)
{
    public static ReleaseVerifier Default => new(Convert.FromBase64String(ReleaseKey.PublicKeyBase64));

    public bool Verify(byte[] data, byte[] derSignature)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
            return key.VerifyData(data, derSignature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException) { return false; }   // malformed key/signature = not verified
    }
}
