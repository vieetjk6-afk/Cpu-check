using System.Diagnostics;
using System.Runtime.InteropServices;
using PcCheck.Core.Fixes;
using PcCheck.Core.Util;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Sensors;

/// <summary>
/// Cài driver PawnIO (mã nguồn mở, có chữ ký số) để đọc nhiệt độ CPU chính xác – người dùng chỉ cần bấm 1 nút.
/// Tải bản chính thức từ GitHub qua HTTPS, kiểm tra chữ ký Authenticode hợp lệ rồi mới chạy "-install -silent".
/// </summary>
public static class PawnIoInstaller
{
    public const string DownloadUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
    public const string HomePage = "https://pawnio.eu/";

    private const long MaxDownloadBytes = 30L * 1024 * 1024;
    private const int ErrorSuccessRebootRequired = 3010;

    public static async Task<FixOutcome> InstallAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"PawnIO_setup_{Guid.NewGuid():N}.exe");
        try
        {
            progress?.Report("download");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PCCheck/1.0");
                using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > MaxDownloadBytes) return new FixOutcome(false, L("cpu_sensor.result.download_failed"));

                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = File.Create(path);
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
                if (target.Length > MaxDownloadBytes || target.Length < 10_000) return new FixOutcome(false, L("cpu_sensor.result.download_failed"));
            }

            progress?.Report("verify");
            if (!Authenticode.IsTrusted(path))
            {
                Log.Warn("PawnIO_setup.exe không có chữ ký số hợp lệ – hủy cài đặt");
                return new FixOutcome(false, L("cpu_sensor.result.untrusted"));
            }

            progress?.Report("install");
            // Ứng dụng thường đã chạy với quyền admin; nếu chưa thì để Windows hỏi UAC.
            var elevated = Elevation.IsAdministrator();
            using var process = Process.Start(new ProcessStartInfo(path, "-install -silent")
            {
                UseShellExecute = !elevated,
                Verb = elevated ? "" : "runas",
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path)!,
            });
            if (process is null) return new FixOutcome(false, L("cpu_sensor.result.failed", "-1"));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            Log.Info($"PawnIO setup kết thúc với mã {process.ExitCode}");
            return process.ExitCode switch
            {
                0 => new FixOutcome(true, L("cpu_sensor.result.ok")),
                ErrorSuccessRebootRequired => new FixOutcome(true, L("cpu_sensor.result.reboot")),
                var code => new FixOutcome(false, L("cpu_sensor.result.failed", code)),
            };
        }
        catch (HttpRequestException ex)
        {
            Log.Warn("Tải PawnIO lỗi", ex);
            return new FixOutcome(false, L("cpu_sensor.result.download_failed"));
        }
        catch (TaskCanceledException ex)
        {
            Log.Warn("Cài PawnIO quá thời gian", ex);
            return new FixOutcome(false, L("cpu_sensor.result.download_failed"));
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // Người dùng bấm "No" ở hộp thoại UAC.
            return new FixOutcome(false, L("cpu_sensor.result.cancelled"));
        }
        catch (Exception ex)
        {
            Log.Error("Cài PawnIO lỗi", ex);
            return new FixOutcome(false, L("cpu_sensor.result.failed", ex.Message));
        }
        finally
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>Kiểm tra chữ ký số Authenticode của tệp thực thi bằng WinVerifyTrust (giống Windows khi chạy file tải về).</summary>
internal static class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint UiNone = 2;
    private const uint RevokeNone = 0;
    private const uint ChoiceFile = 1;
    private const uint StateActionIgnore = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

#pragma warning disable SYSLIB1054
    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);
#pragma warning restore SYSLIB1054

    public static bool IsTrusted(string filePath)
    {
        var pathPtr = Marshal.StringToHGlobalUni(filePath);
        var filePtr = IntPtr.Zero;
        var dataPtr = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = pathPtr,
            };
            filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, filePtr, false);

            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = UiNone,
                fdwRevocationChecks = RevokeNone,
                dwUnionChoice = ChoiceFile,
                pFile = filePtr,
                dwStateAction = StateActionIgnore,
            };
            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPtr, false);

            var result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, dataPtr);
            if (result != 0) Log.Warn($"WinVerifyTrust({Path.GetFileName(filePath)}) = 0x{result:X8}");
            return result == 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Kiểm tra chữ ký số lỗi", ex);
            return false;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
            if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }
}
