using System.Net;
using System.Text;
using DicomAgent.Application.Models;
using DicomAgent.Infrastructure.Storage;
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.Extensions.Logging;

namespace DicomAgent.Infrastructure.Dicom;

public sealed class StorageScp(INetworkStream stream, Encoding encoding, ILogger logger,
    DicomServiceDependencies dependencies, DicomOptions options, InstanceStore store, ReceiverState state)
    : DicomService(stream, encoding, logger, dependencies), IDicomServiceProvider, IDicomCStoreProvider, IDicomCEchoProvider
{
    private static readonly HashSet<string> StorageClasses = [
        "1.2.840.10008.5.1.4.1.1.1", "1.2.840.10008.5.1.4.1.1.1.1", "1.2.840.10008.5.1.4.1.1.1.1.1",
        "1.2.840.10008.5.1.4.1.1.1.2", "1.2.840.10008.5.1.4.1.1.1.2.1", "1.2.840.10008.5.1.4.1.1.1.3",
        "1.2.840.10008.5.1.4.1.1.1.3.1", "1.2.840.10008.5.1.4.1.1.2", "1.2.840.10008.5.1.4.1.1.2.1",
        "1.2.840.10008.5.1.4.1.1.4", "1.2.840.10008.5.1.4.1.1.4.1", "1.2.840.10008.5.1.4.1.1.7"
    ];
    public async Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        if (association.CalledAE != options.AeTitle)
        {
            await SendAssociationRejectAsync(DicomRejectResult.Permanent, DicomRejectSource.ServiceUser, DicomRejectReason.CalledAENotRecognized); return;
        }
        var remote = IPAddress.Parse(association.RemoteHost).MapToIPv6();
        if ((options.AllowedCallingAeTitles.Length > 0 && !options.AllowedCallingAeTitles.Contains(association.CallingAE, StringComparer.Ordinal)) ||
            (options.AllowedRemoteAddresses.Length > 0 && !options.AllowedRemoteAddresses.Any(a => IPAddress.Parse(a).MapToIPv6().Equals(remote))))
        {
            state.Error("Association rejected by allowlist.");
            await SendAssociationRejectAsync(DicomRejectResult.Permanent, DicomRejectSource.ServiceUser, DicomRejectReason.CallingAENotRecognized); return;
        }
        foreach (var pc in association.PresentationContexts)
        {
            if (pc.AbstractSyntax == DicomUID.Verification || StorageClasses.Contains(pc.AbstractSyntax.UID))
                pc.AcceptTransferSyntaxes(DicomTransferSyntax.ExplicitVRLittleEndian, DicomTransferSyntax.ImplicitVRLittleEndian);
            else pc.SetResult(DicomPresentationContextResult.RejectAbstractSyntaxNotSupported);
        }
        await SendAssociationAcceptAsync(association);
    }
    protected override void CreateCStoreReceiveStream(DicomFile file)
    {
        base.CreateCStoreReceiveStream(file);
        _dimseStream = new BoundedWriteStream(_dimseStream, options.MaxInstanceBytes);
    }
    public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();
    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) => state.Error("Association aborted.");
    public void OnConnectionClosed(Exception exception)
    {
        if (exception is not null) state.Error("DICOM connection closed with an error.");
    }
    public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request) => Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));
    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        try
        {
            var item = MetadataExtractor.Extract(request.File);
            if (request.SOPInstanceUID.UID != item.SopInstanceUid || request.SOPClassUID.UID != item.SopClassUid)
                return new DicomCStoreResponse(request, DicomStatus.StorageDataSetDoesNotMatchSOPClassError);
            await store.SaveAsync(request.File, CancellationToken.None);
            return new DicomCStoreResponse(request, DicomStatus.Success);
        }
        catch (InstanceConflictException) { state.Error("Instance UID content conflict; original retained."); return new(request, DicomStatus.ProcessingFailure); }
        catch (InvalidDataException) { state.Error("Invalid DICOM dataset."); return new(request, DicomStatus.StorageCannotUnderstand); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { state.Error("DICOM storage failed."); return new(request, DicomStatus.StorageStorageOutOfResources); }
        catch (Exception ex)
        {
            Logger.LogError("C-STORE failed ({ErrorType}).", ex.GetType().Name);
            state.Error("C-STORE processing failed."); return new(request, DicomStatus.ProcessingFailure);
        }
    }
    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
    {
        state.Error("Malformed or oversized DICOM request.");
        return Task.CompletedTask;
    }
}
