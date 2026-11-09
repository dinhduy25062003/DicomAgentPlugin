using FellowOakDicom;
using DicomAgent.Domain.Entities;
using DicomAgent.Application.Services;
namespace DicomAgent.Infrastructure.Dicom;
public static class MetadataExtractor
{
    public static DicomInstance Extract(DicomFile file)
    {
        var d = file.Dataset;
        string? Text(DicomTag tag) => d.TryGetSingleValue<string>(tag, out var value) ? value : null;
        int? Number(DicomTag tag) => d.TryGetSingleValue<int>(tag, out var value) ? value : null;
        var study = Text(DicomTag.StudyInstanceUID); var series = Text(DicomTag.SeriesInstanceUID);
        var sop = Text(DicomTag.SOPInstanceUID); var sopClass = Text(DicomTag.SOPClassUID);
        if (!ConfigurationRules.ValidUid(study) || !ConfigurationRules.ValidUid(series) ||
            !ConfigurationRules.ValidUid(sop) || !ConfigurationRules.ValidUid(sopClass))
            throw new InvalidDataException("Required DICOM UIDs are missing or invalid.");
        return new DicomInstance
        {
            StudyInstanceUid = study!,
            SeriesInstanceUid = series!,
            SopInstanceUid = sop!,
            SopClassUid = sopClass!,
            TransferSyntaxUid = d.InternalTransferSyntax.UID.UID,
            PatientId = Text(DicomTag.PatientID),
            PatientName = Text(DicomTag.PatientName),
            StudyDate = Text(DicomTag.StudyDate),
            StudyTime = Text(DicomTag.StudyTime),
            SeriesNumber = Text(DicomTag.SeriesNumber),
            InstanceNumber = Text(DicomTag.InstanceNumber),
            Modality = Text(DicomTag.Modality),
            Rows = Number(DicomTag.Rows),
            Columns = Number(DicomTag.Columns),
            BitsAllocated = Number(DicomTag.BitsAllocated),
            BitsStored = Number(DicomTag.BitsStored)
        };
    }
}
