namespace DicomAgent.Application.Models;

public sealed class LocalApiOptions
{
    public int Port { get; set; } = 17890;
    public string SaasBaseUrlTemplate { get; set; } = "https://{TenantName}.tdental.vn";
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed record CaptureInput(string ResModel, Guid ResId, string EntityName, string ModelName,
    string ServiceName, [property: System.Text.Json.Serialization.JsonConverter(typeof(IdentifierStringConverter))] string PartnerId, string PartnerName);
public sealed record CaptureFile(Guid Id, string FileName, string FilePath, long FileSize, string? PatientId,
    string? PatientName, string StudyInstanceUid, string SopInstanceUid, DateTime ReceivedAtUtc);
public sealed record CaptureRequest(Guid Id, string TenantName, CaptureInput Info, DateTime CreatedAtUtc,
    string Status, CaptureFile[] Files, string? LastError = null, DateTime? CompletedAtUtc = null);
public sealed record CaptureSnapshot(CaptureRequest[] Requests, Guid? ActiveRequestId, string? TenantName = null);

public sealed record ImagePreview(int Width, int Height, byte[] Bgra);

// Web clients may send numeric partner IDs while other installations use UUID/string IDs.
public sealed class IdentifierStringConverter : System.Text.Json.Serialization.JsonConverter<string>
{
    public override string? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
        reader.TokenType == System.Text.Json.JsonTokenType.String ? reader.GetString() :
        reader.TokenType == System.Text.Json.JsonTokenType.Number && reader.TryGetInt64(out var value) ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) :
        throw new System.Text.Json.JsonException("PartnerId must be a string or integer.");
    public override void Write(System.Text.Json.Utf8JsonWriter writer, string value, System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue(value);
}

public sealed record CaptureSession(Guid RequestId, Guid Generation);
public sealed record StoredCapture(CaptureRequest Request, string ProtectedAuthorization, string ServerUrl);
public sealed record CaptureRequestStore(string? TenantName, StoredCapture[] Requests);
public sealed record CapturedDicomFile(string FileName, string FilePath, long FileSize, string? PatientId,
    string? PatientName, string StudyInstanceUid, string SopInstanceUid);
public sealed record AttachmentUploadResult(int? StatusCode, bool RequestSent);
