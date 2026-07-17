using System.Buffers.Binary;
using System.Text;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public static class SshKeyFormatDetector
{
    private static readonly string[] OpenSshAlgorithms =
    [
        "ssh-ed25519",
        "ssh-rsa",
        "ssh-dss",
        "ecdsa-sha2-",
        "sk-ssh-ed25519@",
        "sk-ecdsa-sha2-"
    ];

    public static SshKeyInspectionResult Detect(string? text, string sourcePath = "")
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result(SshKeyFormat.Unknown, "Empty or unreadable key", sourcePath);
        }

        if (trimmed.StartsWith("PuTTY-User-Key-File-", StringComparison.Ordinal))
        {
            return Result(SshKeyFormat.PuttyPrivate, "PuTTY private key (PPK)", sourcePath, isPrivate: true);
        }

        if (trimmed.Contains("-----BEGIN OPENSSH PRIVATE KEY-----", StringComparison.Ordinal))
        {
            return Result(SshKeyFormat.OpenSshPrivate, "OpenSSH private key", sourcePath, isPrivate: true, canConvert: true);
        }

        if (trimmed.Contains(" PRIVATE KEY-----", StringComparison.Ordinal)
            && trimmed.Contains("-----BEGIN ", StringComparison.Ordinal))
        {
            return Result(SshKeyFormat.PemPrivate, "PEM/PKCS private key", sourcePath, isPrivate: true, canConvert: true);
        }

        if (trimmed.Contains("---- BEGIN SSH2 PUBLIC KEY ----", StringComparison.Ordinal))
        {
            return Result(SshKeyFormat.Rfc4716Public, "RFC4716 SSH2 public key", sourcePath, canConvert: true);
        }

        if (trimmed.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal)
            || trimmed.Contains("-----BEGIN RSA PUBLIC KEY-----", StringComparison.Ordinal)
            || trimmed.Contains("-----BEGIN EC PUBLIC KEY-----", StringComparison.Ordinal))
        {
            return Result(SshKeyFormat.PemPublic, "PEM/PKCS public key", sourcePath, canConvert: true);
        }

        if (TryNormalizeOpenSshPublicKey(trimmed, out var normalized, out var algorithm))
        {
            return new SshKeyInspectionResult
            {
                Format = SshKeyFormat.OpenSshPublic,
                DisplayName = $"OpenSSH public key ({algorithm})",
                SourcePath = sourcePath,
                PublicKeyText = normalized,
                Algorithm = algorithm,
                Exists = true,
                IsOpenSsh = true,
                CanConvert = true
            };
        }

        return Result(SshKeyFormat.Unknown, "Unknown or invalid public-key format", sourcePath);
    }

    public static bool TryNormalizeOpenSshPublicKey(string? text, out string normalized, out string algorithm)
    {
        normalized = string.Empty;
        algorithm = string.Empty;

        foreach (var rawLine in (text ?? string.Empty).Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = rawLine.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !IsOpenSshAlgorithm(parts[0]))
            {
                continue;
            }

            byte[] blob;
            try
            {
                blob = Convert.FromBase64String(parts[1]);
            }
            catch (FormatException)
            {
                continue;
            }

            if (!TryReadAlgorithm(blob, out var embeddedAlgorithm)
                || !string.Equals(parts[0], embeddedAlgorithm, StringComparison.Ordinal)
                || blob.Length <= 4 + Encoding.ASCII.GetByteCount(embeddedAlgorithm))
            {
                continue;
            }

            algorithm = parts[0];
            normalized = string.Join(' ', parts);
            return true;
        }

        return false;
    }

    private static bool TryReadAlgorithm(ReadOnlySpan<byte> blob, out string algorithm)
    {
        algorithm = string.Empty;
        if (blob.Length < sizeof(int))
        {
            return false;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(blob);
        if (length <= 0 || length > blob.Length - sizeof(int))
        {
            return false;
        }

        var bytes = blob.Slice(sizeof(int), length);
        foreach (var value in bytes)
        {
            if (value is < 0x21 or > 0x7e)
            {
                return false;
            }
        }

        algorithm = Encoding.ASCII.GetString(bytes);
        return IsOpenSshAlgorithm(algorithm);
    }

    private static bool IsOpenSshAlgorithm(string value)
        => OpenSshAlgorithms.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));

    private static SshKeyInspectionResult Result(
        SshKeyFormat format,
        string displayName,
        string sourcePath,
        bool isPrivate = false,
        bool canConvert = false)
        => new()
        {
            Format = format,
            DisplayName = displayName,
            SourcePath = sourcePath,
            Exists = true,
            IsPrivateMaterial = isPrivate,
            CanConvert = canConvert
        };
}
