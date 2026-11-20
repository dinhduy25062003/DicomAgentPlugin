using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Infrastructure.Dicom;
using FellowOakDicom;
using FellowOakDicom.Imaging;

namespace DicomAgent.Infrastructure.Capture;

public sealed class DicomCaptureImageReader : ICaptureImageReader
{
    public static CapturedDicomFile Describe(DicomFile file, string path)
    {
        var metadata = MetadataExtractor.Extract(file);
        return new(Path.GetFileName(path), path, new FileInfo(path).Length, metadata.PatientId,
            metadata.PatientName, metadata.StudyInstanceUid, metadata.SopInstanceUid);
    }
    public async Task<ImagePreview> PreviewAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var file = await DicomFile.OpenAsync(path);
        var image = new DicomImage(file.Dataset);
        image.Scale = Math.Min(1, 384.0 / Math.Max(image.Width, image.Height));
        using var rendered = image.RenderImage(0);
        return new(rendered.Width, rendered.Height, rendered.AsBytes());
    }
}
