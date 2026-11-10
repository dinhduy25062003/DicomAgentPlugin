using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using DicomAgent.Desktop.Views;
using DicomAgent.Desktop.ViewModels;
namespace DicomAgent.Desktop;
public sealed partial class App : Avalonia.Application
{
    private TrayIcon? trayIcon;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = new MainViewModel() };
            desktop.MainWindow = window;
            if (OperatingSystem.IsWindows())
            {
                var open = new NativeMenuItem("Open DICOM Agent");
                open.Click += (_, _) => window.RestoreFromTray();
                var exit = new NativeMenuItem("Exit desktop (receiver keeps running)");
                exit.Click += (_, _) => desktop.Shutdown();
                using var iconStream = AssetLoader.Open(new Uri("avares://DicomAgent.Desktop/Assets/dicom-agent.ico"));
                trayIcon = new TrayIcon
                {
                    Icon = new WindowIcon(iconStream),
                    ToolTipText = "DICOM Desktop Agent — receiver runs as a service",
                    Menu = new NativeMenu { Items = { open, new NativeMenuItemSeparator(), exit } },
                    IsVisible = true
                };
                TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
                trayIcon.Clicked += (_, _) => window.RestoreFromTray();
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window.CloseToTray = true;
                desktop.Exit += (_, _) => trayIcon.Dispose();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
