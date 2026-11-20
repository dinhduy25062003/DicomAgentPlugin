using System.Net.Http.Headers;
using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using Microsoft.Extensions.Logging;

namespace DicomAgent.Infrastructure.Capture;

public sealed class SaasAttachmentClient(DicomOptions dicom, ILogger<SaasAttachmentClient> logger) : ISaasAttachmentClient
{
    public async Task<AttachmentUploadResult> UploadAsync(CaptureRequest request, string serverUrl, string authorization, CancellationToken ct)
    {
        var sent = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5));
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(serverUrl), "api/IrAttachments"));
            message.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
            message.Headers.Add("TenantName", request.TenantName);
            message.Headers.Add("Idempotency-Key", request.Id.ToString());
            using var form = new MultipartFormDataContent(); message.Content = form;
            form.Add(new StringContent(request.Info.ResId.ToString()), "ResId");
            form.Add(new StringContent(request.Info.ResModel), "ResModel");
            form.Add(new StringContent(request.TenantName), "Attachments.Tenant");
            foreach (var file in request.Files)
            {
                var info = new FileInfo(file.FilePath);
                if (!info.Exists || info.Length != file.FileSize || info.Length > dicom.MaxInstanceBytes)
                    throw new IOException("A selected file is missing or changed.");
                var content = new StreamContent(File.OpenRead(file.FilePath)); content.Headers.ContentType = new("application/dicom");
                form.Add(content, "Attachments.Files", file.FileName);
            }
            sent = true;
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return new((int)response.StatusCode, sent);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Capture upload did not complete ({ErrorType}).", ex.GetType().Name);
            return new(null, sent);
        }
    }
}
