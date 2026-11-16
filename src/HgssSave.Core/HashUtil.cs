using System;
using System.Security.Cryptography;

namespace HgssSave.Core;

/// <summary>Lowercase hexadecimal SHA-256, used for base/output/patch hashes (design §9).</summary>
public static class HashUtil
{
    public static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string Sha256File(string path) =>
        Sha256(File.ReadAllBytes(path));
}
