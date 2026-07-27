namespace GhostDeck.Contracts;

public enum PrivilegedActionType
{
    EndProcesses,
    ExecuteImportedAction,
    SetDns,
    RestoreDns,
    FlushDns,
    CleanTargets
}

public sealed record ServiceRequest(Guid CorrelationId, int ProtocolVersion, PrivilegedActionType Action, string PayloadJson);
public sealed record ServiceResponse(Guid CorrelationId, bool Success, string Message, string? PayloadJson = null);
public sealed record EndProcessItem(int ProcessId, DateTime ExpectedStartTimeUtc, bool IncludeTree);
public sealed record EndProcessesPayload(IReadOnlyList<EndProcessItem> Processes);
public sealed record ImportedActionPayload(string ActionId);
public sealed record SetDnsPayload(IReadOnlyList<int> InterfaceIndexes, IReadOnlyList<string> Servers);
public sealed record CleanTargetsPayload(IReadOnlyList<string> TargetIds);

public static class ServiceProtocol
{
    public const int Version = 1;
    public const string PipeName = "GhostDeck.Service.v1";
}
