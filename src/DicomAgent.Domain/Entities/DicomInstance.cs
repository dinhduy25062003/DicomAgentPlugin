namespace DicomAgent.Domain.Entities;

public sealed class DicomInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StorageRoot { get; set; } = "";
    public string SopInstanceUid { get; set; } = "";
    public string StudyInstanceUid { get; set; } = "";
    public string SeriesInstanceUid { get; set; } = "";
    public string SopClassUid { get; set; } = "";
    public string TransferSyntaxUid { get; set; } = "";
    public string? PatientId { get; set; }
    public string? PatientName { get; set; }
    public string? StudyDate { get; set; }
    public string? StudyTime { get; set; }
    public string? SeriesNumber { get; set; }
    public string? InstanceNumber { get; set; }
    public string? Modality { get; set; }
    public int? Rows { get; set; }
    public int? Columns { get; set; }
    public int? BitsAllocated { get; set; }
    public int? BitsStored { get; set; }
    public string FilePath { get; set; } = "";
    public string FileHash { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
}
