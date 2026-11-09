namespace DicomAgent.Application.Models;

public sealed class DicomOptions
{
    public string AeTitle { get; set; } = "DICOM_AGENT";
    public string ListenAddress { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 11112;
    public string StoragePath { get; set; } = "";
    public int MaxConcurrentAssociations { get; set; } = 10;
    public long MaxInstanceBytes { get; set; } = 268435456;
    public string[] AllowedCallingAeTitles { get; set; } = [];
    public string[] AllowedRemoteAddresses { get; set; } = [];
}
public sealed class AgentOptions
{
    public bool EnableReceiver { get; set; } = true;
    public bool EnableFileHistory { get; set; } = true;
}
public sealed record AgentSettings(DicomOptions Dicom, AgentOptions Agent);
public sealed record HistoryItem(DateTime ReceivedAtUtc, string StudyInstanceUid, string SeriesInstanceUid,
    string SopInstanceUid, string? Modality, long FileSize, string FilePath);
public sealed record AgentStatus(string State, DicomOptions ActiveConfiguration, long TotalReceivedFiles,
    DateTime? LastReceivedAtUtc, string[] RecentErrors, bool RestartRequired);
public sealed record ManagementRequest(string Operation, AgentSettings? Settings = null);
public sealed record ManagementResponse(bool Success, string? Error = null, AgentSettings? Settings = null,
    AgentStatus? Status = null, HistoryItem[]? History = null);
