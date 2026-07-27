using System.Collections.ObjectModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using GhostDeck.Contracts;
using GhostDeck.Core;
using GhostDeck.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GhostDeck.App;

public sealed partial class MainWindow : Window
{
    private readonly ProcessTelemetryService _processes;
    private readonly PowerActionImporter _power;
    private readonly NetworkInfoService _network;
    private readonly CleanupService _cleaner;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private IReadOnlyList<ProcessGroup> _allGroups = Array.Empty<ProcessGroup>();

    public ObservableCollection<ProcessGroupRow> ProcessRows { get; } = new();

    public MainWindow(ProcessTelemetryService processes, PowerActionImporter power, NetworkInfoService network, CleanupService cleaner)
    {
        InitializeComponent();
        _processes = processes;
        _power = power;
        _network = network;
        _cleaner = cleaner;

        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // The app remains usable when Mica is unavailable.
        }

        ProcessList.ItemsSource = ProcessRows;
        Nav.SelectedIndex = 0;
        _timer.Tick += (_, _) => RefreshProcesses();
        _timer.Start();
        RefreshProcesses();
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tag = (Nav.SelectedItem as ListBoxItem)?.Tag?.ToString() ?? "dashboard";
        DashboardPanel.Visibility = tag == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        ProcessesPanel.Visibility = tag == "processes" ? Visibility.Visible : Visibility.Collapsed;
        PowerPanel.Visibility = tag == "power" ? Visibility.Visible : Visibility.Collapsed;
        NetworkPanel.Visibility = tag == "network" ? Visibility.Visible : Visibility.Collapsed;
        CleanerPanel.Visibility = tag == "cleaner" ? Visibility.Visible : Visibility.Collapsed;
        AudioPanel.Visibility = tag == "audio" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = tag == "history" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = tag switch
        {
            "processes" => "Processes",
            "power" => "Power and Memory",
            "network" => "Network",
            "cleaner" => "Cleaner",
            "audio" => "Audio",
            "history" => "History",
            "settings" => "Settings",
            _ => "Dashboard"
        };

        if (tag == "power") RefreshPower();
        if (tag == "network") RefreshNetwork();
    }

    private void RefreshProcesses()
    {
        var snapshots = _processes.Capture();
        _allGroups = ProcessGrouping.Group(snapshots);
        CpuValue.Text = $"{snapshots.Sum(x => x.CpuPercent):0.0}%";
        ProcessCountValue.Text = snapshots.Count.ToString("N0");
        var memory = GetMemory();
        MemoryValue.Text = memory.Total == 0
            ? "N/A"
            : $"{memory.Used / 1024d / 1024d / 1024d:0.0} / {memory.Total / 1024d / 1024d / 1024d:0.0} GB";
        ApplyProcessFilter();
    }

    private void ApplyProcessFilter()
    {
        var query = ProcessSearch?.Text?.Trim() ?? string.Empty;
        var selectedIds = ProcessList.SelectedItems
            .Cast<ProcessGroupRow>()
            .Select(x => x.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ProcessRows.Clear();
        foreach (var group in _allGroups.Where(x =>
                     query.Length == 0 ||
                     x.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                     x.Identity.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            ProcessRows.Add(new ProcessGroupRow(group));
        }

        foreach (var row in ProcessRows.Where(x => selectedIds.Contains(x.Identity)))
        {
            ProcessList.SelectedItems.Add(row);
        }
    }

    private void ProcessSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyProcessFilter();

    private void RefreshProcesses_Click(object sender, RoutedEventArgs e) => RefreshProcesses();

    private async void EndSelected_Click(object sender, RoutedEventArgs e)
    {
        var rows = ProcessList.SelectedItems.Cast<ProcessGroupRow>().ToArray();
        if (rows.Length == 0) return;

        var processes = rows
            .SelectMany(x => x.Group.Processes)
            .Where(x => !x.IsProtected)
            .Select(x => new EndProcessItem(x.ProcessId, x.StartTimeUtc, true))
            .ToArray();

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "End selected processes?",
            Content = $"Groups: {rows.Length}\nProcesses: {processes.Length}\nProtected items are skipped.",
            PrimaryButtonText = "End",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var response = await SendAsync(PrivilegedActionType.EndProcesses, new EndProcessesPayload(processes));
        await new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = response.Success ? "Completed" : "Failed",
            Content = response.Message,
            CloseButtonText = "OK"
        }.ShowAsync();

        RefreshProcesses();
    }

    private void RefreshPower_Click(object sender, RoutedEventArgs e) => RefreshPower();

    private void RefreshPower() => PowerList.ItemsSource = _power.Import();

    private void RefreshNetwork_Click(object sender, RoutedEventArgs e) => RefreshNetwork();

    private void RefreshNetwork() => NetworkList.ItemsSource = _network.GetAdapters().Select(x => new NetworkRow(x)).ToArray();

    private async void ScanCleaner_Click(object sender, RoutedEventArgs e)
    {
        CleanerStatus.Text = "Scanning...";
        try
        {
            var rows = new List<CleanerRow>();
            foreach (var target in _cleaner.GetTargets())
            {
                rows.Add(new CleanerRow(await _cleaner.ScanAsync(target, CancellationToken.None)));
            }

            CleanerList.ItemsSource = rows;
            CleanerStatus.Text = $"Scan completed. {rows.Count} targets checked.";
        }
        catch (Exception ex)
        {
            CleanerStatus.Text = $"Scan failed: {ex.Message}";
        }
    }

    private static async Task<ServiceResponse> SendAsync<T>(PrivilegedActionType action, T payload)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                ServiceProtocol.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000);

            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var request = new ServiceRequest(
                Guid.NewGuid(),
                ServiceProtocol.Version,
                action,
                JsonSerializer.Serialize(payload));

            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            var line = await reader.ReadLineAsync();
            return line is null
                ? new ServiceResponse(request.CorrelationId, false, "Service returned no response")
                : JsonSerializer.Deserialize<ServiceResponse>(line)
                  ?? new ServiceResponse(request.CorrelationId, false, "Invalid service response");
        }
        catch (Exception ex)
        {
            return new ServiceResponse(Guid.Empty, false, $"GhostDeck Service is unavailable. {ex.Message}");
        }
    }

    private static (ulong Used, ulong Total) GetMemory()
    {
        var status = new MemoryStatusEx();
        return GlobalMemoryStatusEx(status)
            ? (status.TotalPhys - status.AvailPhys, status.TotalPhys)
            : (0, 0);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx status);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}

public sealed class ProcessGroupRow(ProcessGroup group)
{
    public ProcessGroup Group { get; } = group;
    public string Identity => Group.Identity;
    public string DisplayName => Group.DisplayName;
    public string ProcessCountText => $"{Group.Processes.Count} process" + (Group.Processes.Count == 1 ? "" : "es");
    public string CpuText => $"CPU {Group.CpuPercent:0.0}%";
    public string MemoryText => $"RAM {Group.WorkingSetBytes / 1024d / 1024d:0} MB";
}

public sealed record NetworkRow(NetworkAdapterInfo Adapter)
{
    public string Name => Adapter.Name;
    public string Description => Adapter.Description;
    public string Summary => $"{(Adapter.IsUp ? "Connected" : "Disconnected")} · IPv4: {string.Join(", ", Adapter.Ipv4Addresses)} · DNS: {string.Join(", ", Adapter.DnsServers)}";
}

public sealed record CleanerRow(CleanupScanResult Result)
{
    public string Name => Result.Target.DisplayName;
    public string Path => Result.Target.RootPath;
    public string Summary => $"{Result.Status} · {Result.FileCount:N0} files · {Result.Bytes / 1024d / 1024d:0.0} MB";
}
