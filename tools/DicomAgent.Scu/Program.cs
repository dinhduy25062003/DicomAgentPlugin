using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
new DicomSetupBuilder().RegisterServices(s => s.AddFellowOakDicom()).Build();
if (args.Length == 2 && args[0] == "generate")
{
    var path = Path.GetFullPath(args[1]);
    if (File.Exists(path)) { Console.Error.WriteLine("Refusing to overwrite an existing file."); return 2; }
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var dataset = new DicomDataset(DicomTransferSyntax.ExplicitVRLittleEndian) {
        { DicomTag.SOPClassUID, DicomUID.DigitalXRayImageStorageForPresentation },
        { DicomTag.SOPInstanceUID, DicomUID.Generate() },
        { DicomTag.StudyInstanceUID, DicomUID.Generate() },
        { DicomTag.SeriesInstanceUID, DicomUID.Generate() },
        { DicomTag.PatientID, "SYNTHETIC" }, { DicomTag.PatientName, "TEST^ONLY" }, { DicomTag.Modality, "DX" },
        { DicomTag.Rows, (ushort)2 }, { DicomTag.Columns, (ushort)2 },
        { DicomTag.BitsAllocated, (ushort)8 }, { DicomTag.BitsStored, (ushort)8 }, { DicomTag.HighBit, (ushort)7 },
        { DicomTag.SamplesPerPixel, (ushort)1 }, { DicomTag.PixelRepresentation, (ushort)0 },
        { DicomTag.PhotometricInterpretation, "MONOCHROME2" },
        new DicomOtherByte(DicomTag.PixelData, 0, 85, 170, 255)
    };
    await new DicomFile(dataset).SaveAsync(path);
    Console.WriteLine("Synthetic DX fixture created."); return 0;
}
if (args.Length < 4 || args[0] is not ("echo" or "store"))
{
    Console.Error.WriteLine("Usage: generate FILE.dcm | echo HOST PORT CALLED_AE [CALLING_AE] | store HOST PORT CALLED_AE FILE.dcm [CALLING_AE]"); return 2;
}
var store = args[0] == "store";
if (store && args.Length < 5) { Console.Error.WriteLine("Missing DICOM file path."); return 2; }
var client = DicomClientFactory.Create(args[1], int.Parse(args[2]), false, args.ElementAtOrDefault(store ? 5 : 4) ?? "TEST_SCU", args[3]);
var success = false;
if (store)
{
    var request = new DicomCStoreRequest(args[4]);
    request.OnResponseReceived += (_, response) => { success = response.Status == DicomStatus.Success; Console.WriteLine($"C-STORE: {response.Status.Code:X4} ({response.Status.State})"); };
    await client.AddRequestAsync(request);
}
else
{
    var request = new DicomCEchoRequest();
    request.OnResponseReceived += (_, response) => { success = response.Status == DicomStatus.Success; Console.WriteLine($"C-ECHO: {response.Status.Code:X4} ({response.Status.State})"); };
    await client.AddRequestAsync(request);
}
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try { await client.SendAsync(timeout.Token); return success ? 0 : 1; }
catch (Exception ex) { Console.Error.WriteLine($"DICOM request failed ({ex.GetType().Name})."); return 1; }
