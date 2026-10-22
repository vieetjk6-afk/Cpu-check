using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PcCheck.Core.Analysis;
using PcCheck.Core.Models;
using PcCheck.Core.Sensors;

namespace PcCheck.Core.Util;

/// <summary>
/// Kiểm tra độ tin cậy của tệp thực thi: chữ ký số Authenticode (WinVerifyTrust), tên người ký, hãng (FileVersionInfo) và
/// vị trí. Tệp trong thư mục Windows không được kiểm tra chữ ký (phần lớn ký qua catalog, WinVerifyTrust trên tệp sẽ báo sai).
/// Kết quả được nhớ theo đường dẫn + thời điểm sửa để quét lại nhanh.
/// </summary>
public static class TrustChecker
{
    private static readonly ConcurrentDictionary<string, (DateTime Modified, FileTrust Trust)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static FileTrust Check(string path, PathContext? context = null)
    {
        var kind = SecurityCatalog.Classify(path, context);
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new FileTrust(path, false, null, "", "", kind);

            var modified = File.GetLastWriteTimeUtc(path);
            if (Cache.TryGetValue(path, out var cached) && cached.Modified == modified) return cached.Trust;

            var company = "";
            try
            {
                company = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim() ?? "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }

            bool? signed = null;
            var signer = "";
            if (kind != PathKind.Windows && OperatingSystem.IsWindows())
            {
                signed = Authenticode.IsTrusted(path);
                if (signed == true) signer = SignerOf(path);
            }

            var trust = new FileTrust(path, true, signed, signer, company, kind);
            Cache[path] = (modified, trust);
            return trust;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new FileTrust(path, File.Exists(path), null, "", "", kind);
        }
    }

    /// <summary>Tên người ký (CN) của chữ ký nhúng trong tệp.</summary>
    private static string SignerOf(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var name = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            return string.IsNullOrWhiteSpace(name) ? cert.Subject : name.Trim();
        }
        catch (CryptographicException)
        {
            return "";
        }
    }

    /// <summary>SHA-256 (chữ thường) để tra cứu trên VirusTotal mà không phải tải tệp lên.</summary>
    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
