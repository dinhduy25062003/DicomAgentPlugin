using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DicomAgent.Application.Models;

namespace DicomAgent.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private CaptureRequest[] allRequests = [];
    private CaptureRequest? selectedRequest, currentRequest;
    private CaptureFile? selectedFile;
    private Guid? activeRequestId;
    private string search = "", localPort = "17890", saasUrl = "https://{TenantName}.tdental.vn", allowedOrigins = "", tenantName = "Chưa gắn tenant — tự cấu hình khi nhận request đầu tiên";
    private int filterIndex, selectedTab;
    private bool polling, verified, busy, connectionLost;
    private WriteableBitmap? preview;
    private string previewMessage = "Chọn ảnh để xem trước frame đầu tiên.";
    public ObservableCollection<CaptureRequest> Requests { get; } = [];
    public ObservableCollection<CaptureFile> CaptureFiles { get; } = [];
    public ICommand StartCaptureCommand { get; private set; } = null!;
    public ICommand StopCaptureCommand { get; private set; } = null!;
    public ICommand DoneCommand { get; private set; } = null!;
    public ICommand RemoveFileCommand { get; private set; } = null!;
    public string Search { get => search; set { Set(ref search, value); FilterRequests(); } }
    public int FilterIndex { get => filterIndex; set { Set(ref filterIndex, value); FilterRequests(); } }
    public int SelectedTab { get => selectedTab; set => Set(ref selectedTab, value); }
    public string LocalPort { get => localPort; set => Set(ref localPort, value); }
    public string SaasUrl { get => saasUrl; set => Set(ref saasUrl, value); }
    public string AllowedOrigins { get => allowedOrigins; set => Set(ref allowedOrigins, value); }
    public string TenantName { get => tenantName; set => Set(ref tenantName, value); }
    public CaptureRequest? SelectedRequest { get => selectedRequest; set => Set(ref selectedRequest, value); }
    public CaptureRequest? CurrentRequest { get => currentRequest; private set { Set(ref currentRequest, value); NotifyCapture(); } }
    public CaptureFile? SelectedFile
    {
        get => selectedFile;
        set { if (selectedFile?.Id == value?.Id) { selectedFile = value; return; } Set(ref selectedFile, value); _ = LoadPreviewAsync(); }
    }
    public WriteableBitmap? Preview { get => preview; private set => Set(ref preview, value); }
    public string PreviewMessage { get => previewMessage; private set => Set(ref previewMessage, value); }
    public bool Verified { get => verified; set => Set(ref verified, value); }
    public bool IsEditable => CurrentRequest?.Status == "new" && !busy;
    public bool IsReview => CurrentRequest?.Status == "review";
    public string ActiveCapture => activeRequestId is null ? "Chưa có ca đang nhận ảnh" : "Đang nhận DICOM cho: " + allRequests.FirstOrDefault(x => x.Id == activeRequestId)?.Info.PartnerName;
    public string CaptureNotice => CurrentRequest is null ? "Chọn request ở danh sách để xem chi tiết hoặc thực hiện." :
        activeRequestId == CurrentRequest.Id ? "Đang nhận ảnh cho ca này. Kiểm tra Patient ID, tên và study trước khi Done." :
        "Ca này chưa bật nhận ảnh. Bấm Thực hiện để nhận DICOM, hoặc chọn file/thư mục.";
    private void InitializeCaptures()
    {
        StartCaptureCommand = new AsyncCommand(() => RunCaptureAsync(async () =>
        {
            var request = SelectedTab == 1 ? CurrentRequest : SelectedRequest;
            if (request is null) throw new InvalidOperationException("Chọn một request trước.");
            EnsureSuccess(await client.SendAsync(new("capture-start", RequestId: request.Id)));
            CurrentRequest = request; SelectedTab = 1; Verified = false;
            await RefreshCapturesAsync(); Message = "Đã bật nhận DICOM. Cấu hình máy chụp gửi tới IP/port/AE của app.";
        }));
        StopCaptureCommand = new AsyncCommand(() => RunCaptureAsync(async () =>
        { EnsureSuccess(await client.SendAsync(new("capture-stop"))); await RefreshCapturesAsync(); Message = "Đã dừng gán ảnh vào ca. Bộ nhận DICOM vẫn chạy."; }));
        DoneCommand = new AsyncCommand(() => RunCaptureAsync(async () =>
        {
            if (CurrentRequest is null || !Verified) throw new InvalidOperationException("Kiểm tra đúng khách/ca và đánh dấu xác nhận ảnh trước khi Done.");
            EnsureSuccess(await client.SendAsync(new("capture-done", RequestId: CurrentRequest.Id, FileIds: CurrentRequest.Files.Select(x => x.Id).ToArray())));
            Verified = false; await RefreshCapturesAsync(); Message = "Đang đẩy ảnh lên SaaS. Trạng thái sẽ thành done sau khi server xác nhận.";
        }));
        RemoveFileCommand = new AsyncCommand(() => RunCaptureAsync(async () =>
        {
            if (CurrentRequest is null || SelectedFile is null) return;
            EnsureSuccess(await client.SendAsync(new("capture-delete", RequestId: CurrentRequest.Id, FileId: SelectedFile.Id)));
            Verified = false; await RefreshCapturesAsync();
        }));
    }
    private void NotifyCapture()
    {
        foreach (var name in new[] { nameof(IsEditable), nameof(IsReview), nameof(ActiveCapture), nameof(CaptureNotice) })
            PropertyChanged?.Invoke(this, new(name));
    }
    public void ViewRequest(CaptureRequest request)
    {
        CurrentRequest = request; SelectedTab = 1; Verified = false; UpdateFiles();
    }
    private void FilterRequests()
    {
        var selectedId = SelectedRequest?.Id;
        var statusFilter = new[] { "", "new", "done", "uploading", "review" }[Math.Clamp(FilterIndex, 0, 4)];
        Requests.Clear();
        foreach (var item in allRequests.Where(x => (statusFilter.Length == 0 || x.Status == statusFilter) &&
            (Search.Length == 0 || string.Join(" ", x.TenantName, x.Info.EntityName, x.Info.ModelName, x.Info.PartnerId, x.Info.PartnerName,
                x.Info.ServiceName, x.Info.ResModel, x.Info.ResId).Contains(Search, StringComparison.OrdinalIgnoreCase)))) Requests.Add(item);
        SelectedRequest = Requests.FirstOrDefault(x => x.Id == selectedId);
    }
    public async Task RefreshCapturesAsync()
    {
        if (polling) return; polling = true;
        try
        {
            var response = await client.SendAsync(new("captures")); EnsureSuccess(response);
            var snapshot = response.Captures!;
            var changed = !System.Text.Json.JsonSerializer.Serialize(allRequests).Equals(System.Text.Json.JsonSerializer.Serialize(snapshot.Requests), StringComparison.Ordinal);
            allRequests = snapshot.Requests; activeRequestId = snapshot.ActiveRequestId;
            TenantName = snapshot.TenantName ?? "Chưa gắn tenant — tự cấu hình khi nhận request đầu tiên";
            if (changed) FilterRequests();
            if (CurrentRequest is not null)
            {
                var next = allRequests.FirstOrDefault(x => x.Id == CurrentRequest.Id);
                if (next is null || !next.Files.Select(x => x.Id).SequenceEqual(CurrentRequest.Files.Select(x => x.Id))) Verified = false;
                CurrentRequest = next; UpdateFiles();
            }
            NotifyCapture();
        }
        finally { polling = false; }
    }
    public async Task PollAsync()
    {
        try
        {
            await RefreshCapturesAsync();
            if (connectionLost) { connectionLost = false; await RefreshAsync(); }
        }
        catch { connectionLost = true; Message = "Không kết nối được Host. Kiểm tra dịch vụ DicomAgent đang chạy."; }
    }
    private void UpdateFiles()
    {
        var files = CurrentRequest?.Files ?? [];
        if (CaptureFiles.Select(x => x.Id).SequenceEqual(files.Select(x => x.Id))) return;
        var selectedId = SelectedFile?.Id; CaptureFiles.Clear(); foreach (var file in files) CaptureFiles.Add(file);
        SelectedFile = CaptureFiles.FirstOrDefault(x => x.Id == selectedId) ?? CaptureFiles.FirstOrDefault();
    }
    public async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        await RunCaptureAsync(async () =>
        {
            if (CurrentRequest is null || CurrentRequest.Status != "new") throw new InvalidOperationException("Chọn request mới trước khi thêm file.");
            var id = CurrentRequest.Id; var count = 0; Verified = false;
            foreach (var path in paths)
            {
                EnsureSuccess(await client.ImportAsync(id, path)); count++;
                await RefreshCapturesAsync();
            }
            Message = $"Đã thêm {count} file DICOM. Kiểm tra đúng khách/ca trước khi Done.";
        });
    }
    public async Task DeleteRequestAsync(CaptureRequest request) => await RunCaptureAsync(async () =>
    {
        EnsureSuccess(await client.SendAsync(new("capture-delete", RequestId: request.Id)));
        await RefreshCapturesAsync(); Message = "Đã xóa request. File DICOM gốc vẫn được giữ trong kho local.";
    });
    public async Task ResolveReviewAsync(bool alreadyUploaded) => await RunCaptureAsync(async () =>
    {
        if (CurrentRequest is null) return;
        EnsureSuccess(await client.SendAsync(new(alreadyUploaded ? "capture-confirm-uploaded" : "capture-allow-retry", RequestId: CurrentRequest.Id)));
        await RefreshCapturesAsync();
    });
    private async Task RunCaptureAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; NotifyCapture();
        try { await action(); }
        catch (Exception ex) { Message = ex.Message; }
        finally { busy = false; NotifyCapture(); }
    }
    private async Task LoadPreviewAsync()
    {
        var id = CurrentRequest?.Id; var fileId = SelectedFile?.Id;
        var old = Preview; Preview = null; old?.Dispose();
        if (id is null || fileId is null) { PreviewMessage = "Chọn ảnh để xem trước frame đầu tiên."; return; }
        PreviewMessage = "Đang đọc ảnh…";
        try
        {
            var response = await client.SendAsync(new("capture-preview", RequestId: id, FileId: fileId)); EnsureSuccess(response);
            if (CurrentRequest?.Id != id || SelectedFile?.Id != fileId) return;
            var image = response.Preview!;
            if (image.Width < 1 || image.Height < 1 || image.Bgra.Length != image.Width * image.Height * 4) throw new InvalidDataException();
            var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var buffer = bitmap.Lock())
                for (var row = 0; row < image.Height; row++) Marshal.Copy(image.Bgra, row * image.Width * 4, buffer.Address + row * buffer.RowBytes, image.Width * 4);
            Preview = bitmap; PreviewMessage = "Ảnh xem trước frame đầu tiên — đối chiếu thông tin bệnh nhân bên cạnh.";
        }
        catch { if (SelectedFile?.Id == fileId) PreviewMessage = "Không xem trước được định dạng DICOM này. File gốc vẫn có thể gửi lên SaaS."; }
    }
}
