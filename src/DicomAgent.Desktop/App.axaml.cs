using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DicomAgent.Desktop.Views;
using DicomAgent.Desktop.ViewModels;
namespace DicomAgent.Desktop;
public sealed partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel() };
        base.OnFrameworkInitializationCompleted();
    }
}
