using System.Diagnostics;

namespace PcCheck.Core.Util;

/// <summary>Một tiến trình (gộp các tiến trình cùng tên, vd nhiều chrome.exe) với % CPU và RAM đang dùng.</summary>
public sealed record ProcessUsage(string Name, int Count, double CpuPercent, long MemoryBytes, string? Path);

/// <summary>
/// Theo dõi tiến trình dùng nhiều CPU/RAM nhất (giống "top processes" của Mole/Task Manager). % CPU tính theo chênh lệch
/// thời gian CPU giữa hai lần gọi, chia cho số nhân logic – nên tổng không quá 100%. Lần gọi đầu chỉ có RAM.
/// </summary>
public sealed class ProcessMonitor
{
    private Dictionary<int, (DateTime StartTime, TimeSpan Cpu)> _last = [];
    private DateTime _lastSample = DateTime.MinValue;
    private readonly Dictionary<string, string?> _paths = new(StringComparer.OrdinalIgnoreCase);

    // Tiến trình không đọc được thời gian CPU (được bảo vệ): không thử lại mỗi lần để tránh ném ngoại lệ liên tục.
    private HashSet<int> _denied = [];

    public (List<ProcessUsage> ByCpu, List<ProcessUsage> ByMemory) Sample(int top = 5)
    {
        var now = DateTime.UtcNow;
        var elapsed = _lastSample == DateTime.MinValue ? TimeSpan.Zero : now - _lastSample;
        var cores = Math.Max(1, Environment.ProcessorCount);
        var current = new Dictionary<int, (DateTime, TimeSpan)>();
        var denied = new HashSet<int>();
        var groups = new Dictionary<string, (int Count, double Cpu, long Memory, int Pid)>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id is 0 or 4) continue; // Idle, System
                    var name = process.ProcessName;
                    var memory = process.WorkingSet64;
                    double cpu = 0;
                    if (_denied.Contains(process.Id))
                    {
                        denied.Add(process.Id);
                    }
                    else
                    {
                        try
                        {
                            var start = process.StartTime;
                            var total = process.TotalProcessorTime;
                            current[process.Id] = (start, total);
                            if (elapsed > TimeSpan.Zero && _last.TryGetValue(process.Id, out var previous) && previous.StartTime == start)
                            {
                                cpu = Math.Max(0, (total - previous.Cpu).TotalMilliseconds / elapsed.TotalMilliseconds / cores * 100);
                            }
                        }
                        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
                        {
                            // Tiến trình được bảo vệ (antivirus, hệ thống): không đọc được thời gian CPU, vẫn có RAM.
                            denied.Add(process.Id);
                        }
                    }

                    groups.TryGetValue(name, out var g);
                    groups[name] = (g.Count + 1, g.Cpu + cpu, g.Memory + memory, g.Count == 0 ? process.Id : g.Pid);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Tiến trình vừa thoát.
                }
            }
        }

        _last = current;
        _denied = denied;
        _lastSample = now;
        var list = groups.Select(kv => new ProcessUsage(kv.Key, kv.Value.Count, Math.Min(100, kv.Value.Cpu), kv.Value.Memory, null)).ToList();
        var byCpu = elapsed > TimeSpan.Zero
            ? list.Where(p => p.CpuPercent >= 0.1).OrderByDescending(p => p.CpuPercent).Take(top).ToList()
            : [];
        var byMemory = list.OrderByDescending(p => p.MemoryBytes).Take(top).ToList();

        // Đường dẫn chỉ đọc cho các tiến trình được hiển thị (MainModule khá tốn), nhớ theo tên.
        ProcessUsage WithPath(ProcessUsage p) => p with { Path = PathOf(p.Name, groups[p.Name].Pid) };
        return (byCpu.Select(WithPath).ToList(), byMemory.Select(WithPath).ToList());
    }

    private string? PathOf(string name, int pid)
    {
        if (_paths.TryGetValue(name, out var cached)) return cached;
        string? path = null;
        try
        {
            using var process = Process.GetProcessById(pid);
            path = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException or ArgumentException)
        {
        }

        if (_paths.Count > 500) _paths.Clear();
        _paths[name] = path;
        return path;
    }
}
