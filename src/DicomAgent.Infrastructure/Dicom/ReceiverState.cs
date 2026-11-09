using System.Collections.Concurrent;
namespace DicomAgent.Infrastructure.Dicom;
public sealed class ReceiverState
{
    public string State { get; set; } = "Stopped";
    private readonly ConcurrentQueue<string> errors = new();
    public string[] Errors => errors.ToArray();
    public void Error(string safeMessage)
    {
        errors.Enqueue(DateTime.UtcNow.ToString("O") + " " + safeMessage);
        while (errors.Count > 20) errors.TryDequeue(out _);
    }
}
