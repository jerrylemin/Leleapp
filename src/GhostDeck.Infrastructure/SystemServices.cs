using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using GhostDeck.Core;
using Microsoft.Win32;

namespace GhostDeck.Infrastructure;

public sealed class ProcessTelemetryService
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At, DateTime Start)> _samples = new();

    public IReadOnlyList<ProcessSnapshot> Capture()
    {
        var now = DateTime.UtcNow;
        var output = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var start = process.StartTime.ToUniversalTime();
                var totalCpu = process.TotalProcessorTime;
                var cpu = 0d;
                if (_samples.TryGetValue(process.Id, out var previous) && previous.Start == start)
                {
                    var elapsed = (now - previous.At).TotalMilliseconds;
                    if (elapsed > 0)
                        cpu = Math.Clamp((totalCpu - previous.Cpu).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100d, 0d, 100d);
                }
                _samples[process.Id] = (totalCpu, now, start);
                output.Add(new ProcessSnapshot(process.Id, start, process.ProcessName, TryGetPath(process), null, process.WorkingSet64, cpu, null, TryGetParentPid(process.Id), ProtectedProcesses.IsProtected(process.ProcessName)));
            }
            catch { }
            finally { process.Dispose(); }
        }
        var live = output.Select(x => x.ProcessId).ToHashSet();
        foreach (var stale in _samples.Keys.Where(id => !live.Contains(id)).ToArray()) _samples.Remove(stale);
        return output;
    }

    private static string? TryGetPath(Process p)
    {
        try { return p.MainModule?.FileName; } catch { return null; }
    }

    private static int? TryGetParentPid(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId={pid}");
            return searcher.Get().Cast<ManagementObject>().Select(x => Convert.ToInt32((uint)x["ParentProcessId"])).FirstOrDefault();
        }
        catch { return null; }
    }
}

public sealed class PowerActionImporter
{
    private static readonly string[] Roots =
    {
        @"DesktopBackground\Shell", @"Directory\Background\shell",
        @"Software\Classes\DesktopBackground\Shell",
        @"Software\Classes\Directory\Background\shell",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell"
    };

    public IReadOnlyList<PowerAction> Import()
    {
        var actions = new List<PowerAction>();
        ScanHive(Registry.ClassesRoot, Roots[0], actions);
        ScanHive(Registry.ClassesRoot, Roots[1], actions);
        ScanHive(Registry.CurrentUser, Roots[2], actions);
        ScanHive(Registry.CurrentUser, Roots[3], actions);
        ScanHive(Registry.LocalMachine, Roots[4], actions);
        return actions.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray();
    }

    private static void ScanHive(RegistryKey hive, string path, ICollection<PowerAction> output)
    {
        using var root = hive.OpenSubKey(path);
        if (root is null) return;
        Walk(root, $"{hive.Name}\\{path}", output);
    }

    private static void Walk(RegistryKey key, string path, ICollection<PowerAction> output)
    {
        using var command = key.OpenSubKey("command");
        var raw = command?.GetValue(null)?.ToString();
        var label = key.GetValue("MUIVerb")?.ToString() ?? key.GetValue(null)?.ToString() ?? Path.GetFileName(path);
        if (!string.IsNullOrWhiteSpace(raw) && (path.Contains("Power", StringComparison.OrdinalIgnoreCase) || path.Contains("Memory", StringComparison.OrdinalIgnoreCase) || label.Contains("Power", StringComparison.OrdinalIgnoreCase) || label.Contains("Memory", StringComparison.OrdinalIgnoreCase)))
        {
            var (exe, args) = CommandLineParser.Split(Environment.ExpandEnvironmentVariables(raw));
            output.Add(new PowerAction(path, label, path, exe, args, key.GetValue("HasLUAShield") is not null));
        }
        foreach (var childName in key.GetSubKeyNames())
        {
            if (childName.Equals("command", StringComparison.OrdinalIgnoreCase)) continue;
            using var child = key.OpenSubKey(childName);
            if (child is not null) Walk(child, $"{path}\\{childName}", output);
        }
    }
}

public static class CommandLineParser
{
    public static (string Executable, string Arguments) Split(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end > 1) return (command[1..end], command[(end + 1)..].Trim());
        }
        var firstSpace = command.IndexOf(' ');
        return firstSpace < 0 ? (command, string.Empty) : (command[..firstSpace], command[(firstSpace + 1)..].Trim());
    }
}

public sealed class NetworkInfoService
{
    public IReadOnlyList<NetworkAdapterInfo> GetAdapters() => NetworkInterface.GetAllNetworkInterfaces().Select(n =>
    {
        var props = n.GetIPProperties();
        var ipv4 = props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).ToArray();
        var dns = props.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray();
        var desc = n.Description;
        var virtualAdapter = new[] { "virtual", "hyper-v", "vmware", "virtualbox", "vpn", "wsl", "tunnel", "loopback" }.Any(x => desc.Contains(x, StringComparison.OrdinalIgnoreCase));
        int index;
        try { index = props.GetIPv4Properties()?.Index ?? -1; } catch { index = -1; }
        return new NetworkAdapterInfo(index, n.Name, desc, n.OperationalStatus == OperationalStatus.Up, virtualAdapter, ipv4, dns);
    }).OrderByDescending(x => x.IsUp).ThenBy(x => x.Name).ToArray();
}

public sealed class CleanupService
{
    public IReadOnlyList<CleanupTarget> GetTargets() => new[]
    {
        new CleanupTarget("prefetch", "Windows Prefetch", Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Prefetch"), null, true),
        new CleanupTarget("wintemp", "Windows Temp", Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Temp"), null, true),
        new CleanupTarget("temp", "User Temp", Path.GetTempPath(), null, false),
        new CleanupTarget("localtemp", "Local AppData Temp", Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Temp"), null, false),
        new CleanupTarget("recent", "Recent Items", Environment.ExpandEnvironmentVariables(@"%APPDATA%\Microsoft\Windows\Recent"), null, false),
        new CleanupTarget("jumplist-auto", "Automatic Jump Lists", Environment.ExpandEnvironmentVariables(@"%APPDATA%\Microsoft\Windows\Recent\AutomaticDestinations"), null, false),
        new CleanupTarget("jumplist-custom", "Custom Jump Lists", Environment.ExpandEnvironmentVariables(@"%APPDATA%\Microsoft\Windows\Recent\CustomDestinations"), null, false),
        new CleanupTarget("crashdumps", "Crash Dumps", Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\CrashDumps"), null, false),
        new CleanupTarget("thumbcache", "Thumbnail Cache", Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer"), "thumbcache*.db", false),
        new CleanupTarget("iconcache", "Icon Cache", Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer"), "iconcache*.db", false),
        new CleanupTarget("inetcache", "Internet Cache", Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\INetCache"), null, false),
        new CleanupTarget("delivery", "Delivery Optimization Cache", Environment.ExpandEnvironmentVariables(@"%PROGRAMDATA%\Microsoft\Windows\DeliveryOptimization\Cache"), null, true)
    };

    public async Task<CleanupScanResult> ScanAsync(CleanupTarget target, CancellationToken token)
    {
        if (!Directory.Exists(target.RootPath)) return new(target, 0, 0, "SKIP", null);
        long count = 0, bytes = 0;
        try
        {
            var option = new EnumerationOptions { RecurseSubdirectories = target.Pattern is null, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(target.RootPath, target.Pattern ?? "*", option))
            {
                token.ThrowIfCancellationRequested();
                try { var info = new FileInfo(file); count++; bytes += info.Length; } catch { }
                if (count % 256 == 0) await Task.Yield();
            }
            return new(target, count, bytes, "OK", null);
        }
        catch (Exception ex) { return new(target, count, bytes, "PARTIAL", ex.Message); }
    }
}
