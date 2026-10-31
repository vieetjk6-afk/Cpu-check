using PcCheck.Core.Util;

namespace PcCheck.Core.Storage;

public sealed record DeleteResult(long FreedBytes, int DeletedFiles, int FailedFiles)
{
    public bool Complete => FailedFiles == 0;
}

/// <summary>
/// Xóa vĩnh viễn một cây thư mục, không bao giờ đi theo liên kết (junction/symlink – pnpm, OneDrive...): chỉ gỡ chính liên kết,
/// không đụng tới thư mục mà nó trỏ tới. Tệp bị khóa được bỏ qua và đếm là lỗi.
/// </summary>
public static class FileDeleter
{
    public static DeleteResult DeleteTree(string path, CancellationToken ct = default)
    {
        long freed = 0;
        int deleted = 0, failed = 0;
        if (File.Exists(path) && !Directory.Exists(path))
        {
            var length = SafeLength(path);
            if (TryDeleteFile(path))
            {
                return new DeleteResult(length, 1, 0);
            }

            return new DeleteResult(0, 0, 1);
        }

        if (!Directory.Exists(path)) return new DeleteResult(0, 0, 0);
        if (IsLink(path))
        {
            return TryDeleteDirectory(path) ? new DeleteResult(0, 0, 0) : new DeleteResult(0, 0, 1);
        }

        Walk(path);
        return new DeleteResult(freed, deleted, failed);

        void Walk(string dir)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                return;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(entry))
                {
                    if (IsLink(entry))
                    {
                        if (!TryDeleteDirectory(entry)) failed++;
                        continue;
                    }

                    Walk(entry);
                }
                else
                {
                    var length = SafeLength(entry);
                    if (TryDeleteFile(entry))
                    {
                        freed += length;
                        deleted++;
                    }
                    else
                    {
                        failed++;
                    }
                }
            }

            // Thư mục còn tệp bị khóa thì lỗi đã được đếm ở trên; chỉ đếm thêm khi thư mục rỗng mà vẫn không xóa được.
            if (!TryDeleteDirectory(dir) && IsEmpty(dir)) failed++;
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // không đọc được thì coi như liên kết: chỉ thử gỡ chính nó, không đi vào trong
        }
    }

    private static bool IsEmpty(string dir)
    {
        try
        {
            return Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static long SafeLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool TryDeleteFile(string file)
    {
        try
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            File.Delete(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryDeleteDirectory(string dir)
    {
        try
        {
            var attributes = File.GetAttributes(dir);
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(dir, attributes & ~FileAttributes.ReadOnly);
            Directory.Delete(dir, recursive: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Không xóa được thư mục {dir}: {ex.Message}");
            return false;
        }
    }
}
