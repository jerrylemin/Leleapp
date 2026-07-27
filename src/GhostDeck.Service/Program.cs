using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using GhostDeck.Contracts;
using GhostDeck.Core;
using GhostDeck.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "GhostDeck Service");
builder.Services.AddSingleton<PowerActionImporter>();
builder.Services.AddSingleton<CleanupService>();
builder.Services.AddHostedService<PipeWorker>();
await builder.Build().RunAsync();

internal sealed class PipeWorker(PowerActionImporter importer, CleanupService cleaner, ILogger<PipeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(stoppingToken);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var request = JsonSerializer.Deserialize<ServiceRequest>(line);
                var response = request is null ? new ServiceResponse(Guid.Empty, false, "Invalid request") : await HandleAsync(request, stoppingToken);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Service pipe loop failed"); await Task.Delay(1000, stoppingToken); }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(ServiceProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    private async Task<ServiceResponse> HandleAsync(ServiceRequest request, CancellationToken token)
    {
        if (request.ProtocolVersion != ServiceProtocol.Version) return new(request.CorrelationId, false, "Unsupported protocol version");
        try
        {
            return request.Action switch
            {
                PrivilegedActionType.EndProcesses => EndProcesses(request),
                PrivilegedActionType.ExecuteImportedAction => await ExecuteImportedAsync(request, token),
                PrivilegedActionType.SetDns => await SetDnsAsync(request, false, token),
                PrivilegedActionType.RestoreDns => await SetDnsAsync(request, true, token),
                PrivilegedActionType.FlushDns => await RunAsync(request.CorrelationId, "ipconfig.exe", "/flushdns", token),
                _ => new(request.CorrelationId, false, "Action is not implemented")
            };
        }
        catch (Exception ex) { return new(request.CorrelationId, false, ex.Message); }
    }

    private static ServiceResponse EndProcesses(ServiceRequest request)
    {
        var payload = JsonSerializer.Deserialize<EndProcessesPayload>(request.PayloadJson) ?? throw new InvalidDataException("Missing process payload");
        var results = new List<string>();
        foreach (var item in payload.Processes)
        {
            try
            {
                using var process = Process.GetProcessById(item.ProcessId);
                if (ProtectedProcesses.IsProtected(process.ProcessName)) { results.Add($"{item.ProcessId}: protected"); continue; }
                if (process.StartTime.ToUniversalTime() != item.ExpectedStartTimeUtc) { results.Add($"{item.ProcessId}: identity changed"); continue; }
                process.Kill(item.IncludeTree);
                results.Add($"{item.ProcessId}: ended");
            }
            catch (Exception ex) { results.Add($"{item.ProcessId}: {ex.Message}"); }
        }
        return new(request.CorrelationId, true, string.Join(Environment.NewLine, results));
    }

    private async Task<ServiceResponse> ExecuteImportedAsync(ServiceRequest request, CancellationToken token)
    {
        var payload = JsonSerializer.Deserialize<ImportedActionPayload>(request.PayloadJson) ?? throw new InvalidDataException("Missing action payload");
        var action = importer.Import().SingleOrDefault(x => x.Id.Equals(payload.ActionId, StringComparison.OrdinalIgnoreCase));
        if (action is null || !Path.IsPathFullyQualified(action.Executable) || !File.Exists(action.Executable)) return new(request.CorrelationId, false, "Imported action is no longer valid");
        return await RunAsync(request.CorrelationId, action.Executable, action.Arguments, token);
    }

    private static async Task<ServiceResponse> SetDnsAsync(ServiceRequest request, bool automatic, CancellationToken token)
    {
        var payload = JsonSerializer.Deserialize<SetDnsPayload>(request.PayloadJson) ?? throw new InvalidDataException("Missing DNS payload");
        foreach (var index in payload.InterfaceIndexes.Where(x => x >= 0))
        {
            var args = automatic
                ? $"-NoProfile -NonInteractive -Command \"Set-DnsClientServerAddress -InterfaceIndex {index} -ResetServerAddresses\""
                : $"-NoProfile -NonInteractive -Command \"Set-DnsClientServerAddress -InterfaceIndex {index} -ServerAddresses @('{string.Join("','", payload.Servers)}')\"";
            var result = await RunAsync(request.CorrelationId, "powershell.exe", args, token);
            if (!result.Success) return result;
        }
        return new(request.CorrelationId, true, "DNS updated");
    }

    private static async Task<ServiceResponse> RunAsync(Guid id, string file, string arguments, CancellationToken token)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(60), token);
        var text = process.ExitCode == 0 ? await output : await error;
        return new(id, process.ExitCode == 0, string.IsNullOrWhiteSpace(text) ? $"Exit code {process.ExitCode}" : text.Trim());
    }
}
