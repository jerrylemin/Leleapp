using System.Collections.ObjectModel;

namespace GhostDeck.Core;

public sealed record SystemSnapshot(double CpuPercent, long UsedMemoryBytes, long TotalMemoryBytes, double? GpuPercent, double? CpuTemperature, double? GpuTemperature, string PowerPlan, IReadOnlyList<string> DnsServers, string AudioOutput);

public sealed record ProcessSnapshot(int ProcessId, DateTime StartTimeUtc, string Name, string? ExecutablePath, string? Publisher, long WorkingSetBytes, double CpuPercent, double? GpuPercent, int? ParentProcessId, bool IsProtected);

public sealed record ProcessGroup(string Identity, string DisplayName, IReadOnlyList<ProcessSnapshot> Processes)
{
    public long WorkingSetBytes => Processes.Sum(x => x.WorkingSetBytes);
    public double CpuPercent => Processes.Sum(x => x.CpuPercent);
    public double? GpuPercent => Processes.Any(x => x.GpuPercent.HasValue) ? Processes.Sum(x => x.GpuPercent ?? 0) : null;
}

public sealed record PowerAction(string Id, string DisplayName, string RegistryPath, string Executable, string Arguments, bool RequiresElevation);
public sealed record DnsProfile(string Name, string[] Servers);
public sealed record NetworkAdapterInfo(int InterfaceIndex, string Name, string Description, bool IsUp, bool IsVirtual, IReadOnlyList<string> Ipv4Addresses, IReadOnlyList<string> DnsServers);
public sealed record CleanupTarget(string Id, string DisplayName, string RootPath, string? Pattern, bool RequiresElevation);
public sealed record CleanupScanResult(CleanupTarget Target, long FileCount, long Bytes, string Status, string? Error);
public sealed record ActionResult(bool Success, string Message, int? ExitCode = null, TimeSpan? Duration = null);

public static class ProtectedProcesses
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "dwm", "fontdrvhost", "GhostDeck.App", "GhostDeck.Service"
    };

    public static bool IsProtected(string processName) => Names.Contains(Path.GetFileNameWithoutExtension(processName));
}

public static class ProcessGrouping
{
    public static IReadOnlyList<ProcessGroup> Group(IEnumerable<ProcessSnapshot> processes) => processes
        .GroupBy(p => !string.IsNullOrWhiteSpace(p.ExecutablePath) ? Path.GetFullPath(p.ExecutablePath).ToUpperInvariant() : $"NAME:{p.Name.ToUpperInvariant()}")
        .Select(g => new ProcessGroup(g.Key, g.First().Name, g.OrderBy(p => p.ProcessId).ToArray()))
        .OrderBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();
}
