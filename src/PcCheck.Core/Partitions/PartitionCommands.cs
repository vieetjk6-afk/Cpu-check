using System.Globalization;
using PcCheck.Core.Models;

namespace PcCheck.Core.Partitions;

/// <summary>
/// Dựng lệnh PowerShell (module Storage của Windows – cùng nền với Disk Management / diskpart) cho từng thao tác.
/// Chỉ nhận số, ký tự ổ A–Z, hệ thống tệp trong danh sách và nhãn đã làm sạch – không ghép chuỗi tùy ý vào lệnh.
/// </summary>
public static class PartitionCommands
{
    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Chuỗi PowerShell trong nháy đơn (nháy đơn bên trong được nhân đôi).</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    public static char Letter(char letter)
    {
        var c = char.ToUpperInvariant(letter);
        if (c is < 'A' or > 'Z') throw new ArgumentException("Invalid drive letter", nameof(letter));
        return c;
    }

    public static string FileSystem(string fs) =>
        PartitionLayout.FileSystems.FirstOrDefault(f => f.Equals(fs, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Unsupported file system", nameof(fs));

    private static string Partition(int disk, int partition) =>
        $"Get-Partition -DiskNumber {N(disk)} -PartitionNumber {N(partition)}";

    private static string FormatPipe(string fs, string label, bool full) =>
        $"Format-Volume -FileSystem {FileSystem(fs)} -NewFileSystemLabel {Quote(PartitionLayout.SanitizeLabel(label, fs))}" +
        (full ? " -Full" : "") + " -Force -Confirm:$false";

    public static string SupportedSize(int disk, int partition) =>
        $"$s = Get-PartitionSupportedSize -DiskNumber {N(disk)} -PartitionNumber {N(partition)}; '{{0}}|{{1}}' -f $s.SizeMin, $s.SizeMax";

    public static string Resize(int disk, int partition, long newSize) =>
        $"Resize-Partition -DiskNumber {N(disk)} -PartitionNumber {N(partition)} -Size {N(newSize)} -Confirm:$false";

    /// <summary>Mở rộng tới mức tối đa Windows cho phép (dùng sau khi xóa phân vùng khi gộp).</summary>
    public static string ExtendToMax(int disk, int partition) =>
        $"$s = Get-PartitionSupportedSize -DiskNumber {N(disk)} -PartitionNumber {N(partition)}; " +
        $"$p = {Partition(disk, partition)}; if ($s.SizeMax -gt $p.Size) {{ Resize-Partition -DiskNumber {N(disk)} -PartitionNumber {N(partition)} -Size $s.SizeMax -Confirm:$false }}";

    public static string Create(int disk, long offset, long size, char? letter, string fs, string label, bool full)
    {
        var assign = letter is { } l ? $" -DriveLetter {Letter(l)}" : " -AssignDriveLetter";
        return $"New-Partition -DiskNumber {N(disk)} -Offset {N(offset)} -Size {N(size)}{assign} | {FormatPipe(fs, label, full)}";
    }

    public static string Delete(int disk, int partition) =>
        $"Remove-Partition -DiskNumber {N(disk)} -PartitionNumber {N(partition)} -Confirm:$false";

    public static string Format(int disk, int partition, string fs, string label, bool full) =>
        $"{Partition(disk, partition)} | {FormatPipe(fs, label, full)}";

    public static string ChangeLetter(int disk, int partition, char? current, char letter)
    {
        var target = Letter(letter);
        return current is { } c
            ? $"Set-Partition -DriveLetter {Letter(c)} -NewDriveLetter {target}"
            : $"Set-Partition -DiskNumber {N(disk)} -PartitionNumber {N(partition)} -NewDriveLetter {target}";
    }

    public static string RemoveLetter(int disk, int partition, char letter) =>
        $"Remove-PartitionAccessPath -DiskNumber {N(disk)} -PartitionNumber {N(partition)} -AccessPath {Quote(Letter(letter) + @":\")}";

    public static string SetLabel(int disk, int partition, string fs, string label) =>
        $"{Partition(disk, partition)} | Get-Volume | Set-Volume -NewFileSystemLabel {Quote(PartitionLayout.SanitizeLabel(label, fs))}";

    /// <summary>Khởi tạo ổ mới (RAW) với bảng phân vùng GPT/MBR.</summary>
    public static string Initialize(int disk, PartitionStyle style) =>
        $"Initialize-Disk -Number {N(disk)} -PartitionStyle {StyleName(style)}";

    /// <summary>Đổi kiểu bảng phân vùng của ổ KHÔNG còn phân vùng nào.</summary>
    public static string ConvertEmpty(int disk, PartitionStyle style) =>
        $"Set-Disk -Number {N(disk)} -PartitionStyle {StyleName(style)}";

    public static string SetOnline(int disk) =>
        $"Set-Disk -Number {N(disk)} -IsOffline $false; Set-Disk -Number {N(disk)} -IsReadOnly $false";

    private static string StyleName(PartitionStyle style) => style switch
    {
        PartitionStyle.Gpt => "GPT",
        PartitionStyle.Mbr => "MBR",
        _ => throw new ArgumentException("Invalid partition style", nameof(style)),
    };

    /// <summary>
    /// Tham số robocopy chép toàn bộ dữ liệu ổ nguồn vào thư mục đích (khi gộp phân vùng). Bỏ qua thư mục hệ thống của volume.
    /// </summary>
    public static string MergeCopyArgs(char source, char target, string folder) =>
        $"{Letter(source)}:\\ \"{Letter(target)}:\\{folder}\" /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /XJ /MT:8 /NP /NFL /NDL " +
        "/XD \"System Volume Information\" \"$RECYCLE.BIN\" \"Recovery\" /XF pagefile.sys hiberfil.sys swapfile.sys";

    /// <summary>Chép toàn bộ ổ nguồn sang gốc ổ đích (khi dời phân vùng), giữ thuộc tính, quyền và chủ sở hữu.</summary>
    public static string MoveCopyArgs(char source, char target) =>
        $"{Letter(source)}:\\ {Letter(target)}:\\ /E /COPY:DATSO /DCOPY:DAT /B /R:1 /W:1 /XJ /MT:8 /NP /NFL /NDL " +
        "/XD \"System Volume Information\" \"$RECYCLE.BIN\" /XF pagefile.sys hiberfil.sys swapfile.sys";

    /// <summary>Xóa phân vùng theo ký tự ổ (dọn phân vùng tạm khi dời thất bại).</summary>
    public static string DeleteByLetter(char letter) =>
        $"Get-Partition -DriveLetter {Letter(letter)} | Remove-Partition -Confirm:$false";

    /// <summary>Tên thư mục chứa dữ liệu chuyển sang khi gộp: "Gop tu D" / "Merged from D".</summary>
    public static string MergeFolderName(char source, string prefix)
    {
        var clean = new string(prefix.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        if (clean.Length == 0) clean = "Merged from";
        return $"{clean} {Letter(source)}";
    }

    /// <summary>Robocopy: mã thoát nhỏ hơn 8 là thành công (có thể kèm tệp bỏ qua).</summary>
    public static bool RobocopySucceeded(int exitCode) => exitCode is >= 0 and < 8;
}
