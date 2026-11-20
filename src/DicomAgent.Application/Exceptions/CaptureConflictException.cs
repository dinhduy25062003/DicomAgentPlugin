namespace DicomAgent.Application.Exceptions;

public sealed class CaptureConflictException(string message) : InvalidOperationException(message);
