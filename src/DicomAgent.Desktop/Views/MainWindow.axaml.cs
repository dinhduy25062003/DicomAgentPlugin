using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DicomAgent.Desktop.ViewModels;
namespace DicomAgent.Desktop.Views;
public sealed partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); Opened += async (_, _) => { if (DataContext is MainViewModel vm) await vm.RefreshAsync(); }; }
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
