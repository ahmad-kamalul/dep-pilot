using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using DepPilot.Contracts;

namespace DepPilot.Agent;

// Samples the Agent's OS view; container metrics are not physical-machine telemetry.
public sealed class HostResources(AgentOptions options)
{
    private ulong previousIdle, previousTotal;
    public static string Address() => Dns.GetHostAddresses(Dns.GetHostName())
        .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))?.ToString() ?? "127.0.0.1";
    public static string[] Capabilities => OperatingSystem.IsLinux() ? ["linux", "ansible", "docker"] : ["windows", "wsl", "ansible", "docker"];

    public ResourceMetrics Sample()
    {
        var (idle, total, used, memory) = OperatingSystem.IsLinux() ? Linux() : Windows();
        double? cpu = previousTotal > 0 && total > previousTotal
            ? Math.Clamp(100.0 * (1 - (double)(idle - previousIdle) / (total - previousTotal)), 0, 100) : null;
        (previousIdle, previousTotal) = (idle, total);
        var disk = new DriveInfo(Path.GetFullPath(options.StateDirectory));
        return new(cpu, used, memory, disk.AvailableFreeSpace, disk.TotalSize, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private static (ulong, ulong, long, long) Linux()
    {
        var cpu = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(ulong.Parse).ToArray();
        var memory = File.ReadLines("/proc/meminfo").Select(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(x => x[0].TrimEnd(':'), x => long.Parse(x[1]) * 1024);
        return (cpu[3] + cpu[4], cpu.Aggregate(0UL, (a, b) => a + b), memory["MemTotal"] - memory["MemAvailable"], memory["MemTotal"]);
    }

    private static (ulong, ulong, long, long) Windows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Resource collection supports Linux and Windows.");
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory) || !GetSystemTimes(out var idle, out var kernel, out var user))
            throw new IOException("Cannot read Windows resource counters.");
        return (idle, kernel + user, (long)(memory.TotalPhysical - memory.AvailablePhysical), (long)memory.TotalPhysical);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
