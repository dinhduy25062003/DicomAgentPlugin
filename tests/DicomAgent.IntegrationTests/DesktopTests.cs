using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using DicomAgent.Desktop;
using DicomAgent.Desktop.ViewModels;
using DicomAgent.Desktop.Views;
using DicomAgent.Application.Models;
using Xunit;

namespace DicomAgent.IntegrationTests;

public sealed class DesktopTests
{
    [Fact]
    public void HeadlessWindowLoadsRequestsCaptureSettingsAndStatusBinding()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };
        var tabs = Assert.Single(window.GetLogicalDescendants().OfType<TabControl>());
        Assert.Equal(3, tabs.ItemCount);
        Assert.Equal("Quản lý request", ((TabItem)tabs.Items[0]!).Header);
        vm.Status = "Running";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "Running");

        // Closing to tray must preserve the window so the menu can restore it.
        using var icon = AssetLoader.Open(new Uri("avares://DicomAgent.Desktop/Assets/dicom-agent.ico"));
        Assert.NotNull(new WindowIcon(icon));
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.CloseToTray = true;
        window.Show();
        var screenshots = Environment.GetEnvironmentVariable("DICOM_AGENT_SCREENSHOTS");
        if (!string.IsNullOrEmpty(screenshots))
        {
            Directory.CreateDirectory(screenshots);
            var request = new CaptureRequest(Guid.NewGuid(), "demo1", new("crm.service", Guid.NewGuid(), "Phiếu chụp Panorama", "Điều trị", "Chụp X-quang Panorama", "KH-001", "Nguyễn Văn An"), DateTime.UtcNow, "new", []);
            vm.TenantName = "demo1"; vm.Requests.Add(request); vm.ViewRequest(request);
            for (var index = 0; index < 3; index++)
            {
                vm.SelectedTab = index;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame); frame.Save(Path.Combine(screenshots, $"screen-{index + 1}.png"));
            }
        }
        window.Close();
        Assert.False(window.IsVisible);
        Assert.False(closed);
        window.WindowState = WindowState.Minimized;
        window.RestoreFromTray();
        Assert.True(window.IsVisible);
        Assert.Equal(WindowState.Normal, window.WindowState);
        window.CloseToTray = false;
        window.Close();
        Assert.True(closed);
    }
}
