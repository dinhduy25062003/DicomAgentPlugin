using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DicomAgent.Desktop.ViewModels;
using DicomAgent.Application.Models;
using Avalonia.Threading;
namespace DicomAgent.Desktop.Views;
public sealed partial class MainWindow : Window
{
    public bool CloseToTray { get; set; }
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => { if (DataContext is MainViewModel vm) await vm.RefreshAsync(); refreshTimer.Start(); };
        Closed += (_, _) => refreshTimer.Stop();
        refreshTimer.Tick += async (_, _) => { if (DataContext is MainViewModel vm) await vm.PollAsync(); };
    }
    private void ViewRequest(object? sender, RoutedEventArgs e)
    { if (sender is Control { DataContext: CaptureRequest request } && DataContext is MainViewModel vm) vm.ViewRequest(request); }
    private void ExecuteRequest(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CaptureRequest request } && DataContext is MainViewModel vm)
        { vm.ViewRequest(request); vm.StartCaptureCommand.Execute(null); }
    }
    private async void DeleteRequest(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CaptureRequest request } && DataContext is MainViewModel vm &&
            await ConfirmAsync("Xóa request cho " + request.Info.PartnerName + "? File DICOM gốc vẫn được giữ trong kho local."))
            await vm.DeleteRequestAsync(request);
    }
    private async void ImportDicomFiles(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Chọn file DICOM", AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("DICOM") { Patterns = ["*.dcm", "*.dicom", "*.DCM", "*.DICOM"] }, FilePickerFileTypes.All] });
        if (DataContext is MainViewModel vm) await vm.ImportFilesAsync(files.Select(x => x.Path.LocalPath));
    }
    private async void ImportDicomFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Chọn thư mục DICOM", AllowMultiple = false });
        if (folders.Count == 0 || DataContext is not MainViewModel vm) return;
        try
        {
            var files = Directory.EnumerateFiles(folders[0].Path.LocalPath, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Where(x => Path.GetExtension(x).Equals(".dcm", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(x).Equals(".dicom", StringComparison.OrdinalIgnoreCase)).Take(101).ToArray();
            if (files.Length > 100) { vm.Message = "Thư mục có hơn 100 file DICOM. Chọn một thư mục nhỏ hơn hoặc chọn file cụ thể."; return; }
            await vm.ImportFilesAsync(files);
        }
        catch (Exception ex) { vm.Message = "Không đọc được thư mục: " + ex.Message; }
    }
    private async void ConfirmUploaded(object? sender, RoutedEventArgs e)
    { if (DataContext is MainViewModel vm && await ConfirmAsync("Bạn đã kiểm tra trên SaaS và xác nhận đủ tất cả ảnh của ca này?")) await vm.ResolveReviewAsync(true); }
    private async void AllowRetry(object? sender, RoutedEventArgs e)
    { if (DataContext is MainViewModel vm && await ConfirmAsync("Chỉ thử lại sau khi xác nhận SaaS chưa có ảnh của lần gửi trước. Cho phép gửi lại có thể tạo bản đính kèm trùng nếu server đã nhận ảnh.")) await vm.ResolveReviewAsync(false); }
    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new Window { Title = "Xác nhận", Width = 510, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var confirm = new Button { Content = "Xác nhận" }; var cancel = new Button { Content = "Hủy" };
        confirm.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(22), Spacing = 18, Children =
            { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
              new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { cancel, confirm } } } };
        return await dialog.ShowDialog<bool>(this);
    }
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Allow explicit app exit and Windows logout/shutdown to close normally.
        if (CloseToTray && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
    public void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    private async void BrowseStorage(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select DICOM storage directory", AllowMultiple = false });
        if (folders.Count > 0 && DataContext is MainViewModel vm) vm.StoragePath = folders[0].Path.LocalPath;
    }
    private void OpenLocation(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.Selected is null) return;
        try
        {
            var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open") { UseShellExecute = false };
            info.ArgumentList.Add(Path.GetDirectoryName(vm.Selected.FilePath)!); Process.Start(info)?.Dispose();
        }
        catch (Exception ex) { vm.Message = "Could not open folder: " + ex.Message; }
    }
    private async void CopyPath(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Selected is not null && Clipboard is not null)
        {
            try { await Clipboard.SetTextAsync(vm.Selected.FilePath); }
            catch (Exception ex) { vm.Message = "Could not copy path: " + ex.Message; }
        }
    }
}
