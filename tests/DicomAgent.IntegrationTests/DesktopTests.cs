using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using DicomAgent.Desktop;
using DicomAgent.Desktop.ViewModels;
using DicomAgent.Desktop.Views;
using Xunit;

namespace DicomAgent.IntegrationTests;

public sealed class DesktopTests
{
    [Fact]
    public void HeadlessWindowLoadsDashboardSettingsHistoryAndStatusBinding()
    {
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };
        var tabs = Assert.Single(window.GetLogicalDescendants().OfType<TabControl>());
        Assert.Equal(3, tabs.ItemCount);
        Assert.Equal("Dashboard", ((TabItem)tabs.Items[0]!).Header);
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
