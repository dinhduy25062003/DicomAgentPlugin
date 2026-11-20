using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using DicomAgent.Desktop.Services;
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
        // Never let visual tests read the installed Host or real patient requests.
        var previousData = Environment.GetEnvironmentVariable("DICOM_AGENT_DATA");
        var previousConfig = Environment.GetEnvironmentVariable("DICOM_AGENT_DESKTOP_CONFIG");
        var data = Path.Combine(Path.GetTempPath(), "dicom-ui-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("DICOM_AGENT_DATA", data);
        Environment.SetEnvironmentVariable("DICOM_AGENT_DESKTOP_CONFIG", data);
        MainWindow? window = null;
        try
        {
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm };
            var nav = window.GetLogicalDescendants().OfType<RadioButton>().Where(x => x.GroupName == "Nav").ToArray();
            Assert.Equal(3, nav.Length);
            Assert.All(nav, item => Assert.True(item.IsChecked == (vm.SelectedTab == Array.IndexOf(nav, item))));
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "Quản lý yêu cầu");
            vm.Status = "Running";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "Running");

            using var icon = AssetLoader.Open(new Uri("avares://DicomAgent.Desktop/Assets/dicom-agent.ico"));
            Assert.NotNull(new WindowIcon(icon));
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.CloseToTray = true;
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var request = new CaptureRequest(Guid.NewGuid(), "demo1", new("crm.service", Guid.NewGuid(), "Phiếu chụp Panorama", "Điều trị", "Chụp X-quang Panorama", "KH-001", "Khách thử nghiệm"), DateTime.UtcNow, "new", []);
            vm.TenantName = "demo1";
            vm.Status = "Running";
            vm.Requests.Add(request);
            vm.ViewRequest(request);

            var screenshots = Environment.GetEnvironmentVariable("DICOM_AGENT_SCREENSHOTS");
            if (!string.IsNullOrEmpty(screenshots)) Directory.CreateDirectory(screenshots);
            var themeSelector = window.GetLogicalDescendants().OfType<ComboBox>()
                .Single(combo => combo.Items.Count == 2 && combo.Items[0] is ComboBoxItem { Content: "Sáng" });
            foreach (var themeIndex in new[] { 0, 1, 0 })
            {
                themeSelector.SelectedIndex = themeIndex;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(themeIndex, vm.ThemeIndex);
                var theme = themeIndex == 0 ? ThemeVariant.Light : ThemeVariant.Dark;
                var prefix = themeIndex == 0 ? "light" : "dark";
                Assert.Equal(theme, window.ActualThemeVariant);
                Assert.Equal(theme, new DesktopThemeSettings().Load());
                Assert.Equal(Color.Parse(themeIndex == 0 ? "#F4F8FC" : "#101820"), BrushColor(window.Background));
                if (!string.IsNullOrEmpty(screenshots))
                {
                    for (var index = 0; index < 3; index++)
                    {
                        vm.SelectedTab = index;
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        SaveFrame(window, screenshots, $"{prefix}-screen-{index + 1}.png");
                        if (index == 2)
                        {
                            var settingsScroll = themeSelector.GetVisualAncestors().OfType<ScrollViewer>().First();
                            settingsScroll.Offset = new Vector(0, settingsScroll.Extent.Height);
                            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                            SaveFrame(window, screenshots, $"{prefix}-appearance.png");
                            settingsScroll.Offset = Vector.Zero;
                            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        }
                    }
                }

                vm.SelectedTab = 0;
                window.MouseMove(new Point(250, 10));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var status = window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "new");
                Assert.Equal(Color.Parse(themeIndex == 0 ? "#A66A12" : "#F3CC79"), BrushColor(status.Foreground));
                Assert.Equal(Color.Parse(themeIndex == 0 ? "#FBF3DE" : "#3B3020"), BrushColor(((Border)status.Parent!).Background));
                var primary = window.GetVisualDescendants().OfType<Button>()
                    .First(button => button.Classes.Contains("primary") && button.Content as string == "Thực hiện");
                var presenter = primary.GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(content => content.Name == "PART_ContentPresenter");
                var caption = presenter.GetVisualDescendants().OfType<TextBlock>().First();
                Assert.Equal(Colors.White, BrushColor(caption.Foreground));
                var point = primary.TranslatePoint(new Point(primary.Bounds.Width / 2, primary.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(primary.IsPointerOver);
                Assert.Equal(Color.Parse("#265D9F"), BrushColor(presenter.Background));
                Assert.Equal(Colors.White, BrushColor(caption.Foreground));
                if (!string.IsNullOrEmpty(screenshots)) SaveFrame(window, screenshots, $"{prefix}-hover.png");

                window.MouseDown(point, MouseButton.Left);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(primary.IsPressed);
                Assert.Equal(Color.Parse("#1F4E86"), BrushColor(presenter.Background));
                Assert.Equal(Colors.White, BrushColor(caption.Foreground));
                if (!string.IsNullOrEmpty(screenshots)) SaveFrame(window, screenshots, $"{prefix}-pressed.png");
                // Release away from the button so a visual assertion never executes a service action.
                window.MouseMove(new Point(250, 10));
                window.MouseUp(new Point(250, 10), MouseButton.Left);

                var refresh = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Làm mới");
                var refreshPoint = refresh.TranslatePoint(new Point(refresh.Bounds.Width / 2, refresh.Bounds.Height / 2), window)!.Value;
                window.MouseMove(refreshPoint);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var refreshPresenter = refresh.GetVisualDescendants().OfType<ContentPresenter>().Single(content => content.Name == "PART_ContentPresenter");
                var refreshCaption = refreshPresenter.GetVisualDescendants().OfType<TextBlock>().First();
                Assert.Equal(Color.Parse(themeIndex == 0 ? "#D8E8F9" : "#263C55"), BrushColor(refreshPresenter.Background));
                Assert.Equal(Color.Parse(themeIndex == 0 ? "#243B53" : "#E5EDF7"), BrushColor(refreshCaption.Foreground));
                window.MouseMove(new Point(250, 10));
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
        finally
        {
            if (window is not null)
            {
                window.CloseToTray = false;
                window.Close();
            }
            Environment.SetEnvironmentVariable("DICOM_AGENT_DATA", previousData);
            Environment.SetEnvironmentVariable("DICOM_AGENT_DESKTOP_CONFIG", previousConfig);
            Directory.Delete(data, true);
        }
    }

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static void SaveFrame(MainWindow window, string directory, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name));
    }
}
