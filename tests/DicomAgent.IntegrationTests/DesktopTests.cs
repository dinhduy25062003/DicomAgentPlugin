using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
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
        window.Close();
    }
}
