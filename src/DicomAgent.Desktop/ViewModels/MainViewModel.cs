using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using DicomAgent.Desktop.Services;
using Avalonia.Styling;
namespace DicomAgent.Desktop.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly DesktopThemeSettings appearance;
    private int themeIndex;
    public int ThemeIndex
    {
        get => themeIndex;
        set
        {
            if (value is < 0 or > 1 || themeIndex == value) return;
            var theme = value == 1 ? ThemeVariant.Dark : ThemeVariant.Light;
            try
            {
                appearance.Save(theme);
                if (Avalonia.Application.Current is { } app) app.RequestedThemeVariant = theme;
                Set(ref themeIndex, value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Message = "Không lưu được chủ đề giao diện: " + ex.Message;
                PropertyChanged?.Invoke(this, new(nameof(ThemeIndex)));
            }
        }
    }
    private readonly ManagementClient client = new();
    private bool isApplyingSettings;
    public bool IsApplyingSettings { get => isApplyingSettings; private set => Set(ref isApplyingSettings, value); }
    public string StorageHelp => "Để trống để dùng " + AgentPaths.DefaultStorage + ". Khi lưu, app tự cấp quyền thư mục và khởi động lại dịch vụ; có thể cần xác thực quản trị.";
    private readonly DesktopStartupRegistration startup = new();
    private bool openAtLogin;
    public bool OpenAtLogin { get => openAtLogin; set => Set(ref openAtLogin, value); }
    public ICommand SaveStartupCommand { get; }
    private AgentSettings? loaded;
    private string message = "Open the Host service, then refresh.";
    private string status = "Disconnected";
    private string summary = "";
    private string errors = "";
    private string aeTitle = "DICOM_AGENT", listenAddress = "0.0.0.0", port = "11112", storagePath = "", callingAes = "", remoteAddresses = "";
    private string maxAssociations = "10", maxBytes = "268435456";
    private bool receiverEnabled = true, historyEnabled = true;
    private HistoryItem? selected;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<HistoryItem> History { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ValidateCommand { get; }
    public MainViewModel() : this(new DesktopThemeSettings()) { }
    public MainViewModel(DesktopThemeSettings appearance)
    {
        this.appearance = appearance;
        themeIndex = appearance.Load() == ThemeVariant.Dark ? 1 : 0;
        try { OpenAtLogin = startup.IsEnabled(); } catch { OpenAtLogin = false; }
        SaveStartupCommand = new AsyncCommand(() =>
        {
            try { startup.SetEnabled(OpenAtLogin); Message = OpenAtLogin ? "Đã bật mở giao diện khi đăng nhập máy." : "Đã tắt mở giao diện khi đăng nhập máy. Dịch vụ nhận ảnh vẫn chạy nền."; }
            catch (Exception ex) { Message = "Không lưu được tùy chọn khởi động: " + ex.Message; }
            return Task.CompletedTask;
        });
        InitializeCaptures();
        RefreshCommand = new AsyncCommand(RefreshAsync);
        SaveCommand = new AsyncCommand(SaveAsync);
        ValidateCommand = new AsyncCommand(() => { Validate(); return Task.CompletedTask; });
    }
    public string Message { get => message; set => Set(ref message, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public string Summary { get => summary; set => Set(ref summary, value); }
    public string Errors { get => errors; set => Set(ref errors, value); }
    public string AeTitle { get => aeTitle; set => Set(ref aeTitle, value); }
    public string ListenAddress { get => listenAddress; set => Set(ref listenAddress, value); }
    public string Port { get => port; set => Set(ref port, value); }
    public string StoragePath { get => storagePath; set => Set(ref storagePath, value); }
    public string CallingAes { get => callingAes; set => Set(ref callingAes, value); }
    public string RemoteAddresses { get => remoteAddresses; set => Set(ref remoteAddresses, value); }
    public string MaxAssociations { get => maxAssociations; set => Set(ref maxAssociations, value); }
    public string MaxBytes { get => maxBytes; set => Set(ref maxBytes, value); }
    public bool ReceiverEnabled { get => receiverEnabled; set => Set(ref receiverEnabled, value); }
    public bool HistoryEnabled { get => historyEnabled; set => Set(ref historyEnabled, value); }
    public HistoryItem? Selected { get => selected; set => Set(ref selected, value); }
    public async Task RefreshAsync()
    {
        if (IsApplyingSettings) return;
        try
        {
            var response = await client.SendAsync(new("status"));
            EnsureSuccess(response);
            ApplyStatus(response.Status!);
            var configuration = await client.SendAsync(new("settings"));
            EnsureSuccess(configuration);
            loaded = configuration.Settings!;
            ApplyLoadedSettings(loaded);
            await RefreshCapturesAsync();
            var history = await client.SendAsync(new("history"));
            EnsureSuccess(history);
            History.Clear();
            foreach (var item in history.History!) History.Add(item);
            Message = response.Status!.RestartRequired
                ? "Settings saved. Restart the operating system service to apply changes."
                : history.Error ?? "Connected. Showing up to 100 recent files.";
        }
        catch (Exception ex)
        {
            Status = "Error / Disconnected";
            Message = "Cannot read Host state. " + ex.Message;
        }
    }

    private void ApplyStatus(AgentStatus status)
    {
        Status = status.State;
        Summary = $"AE: {status.ActiveConfiguration.AeTitle}   |   {status.ActiveConfiguration.ListenAddress}:{status.ActiveConfiguration.Port}\nStorage: {status.ActiveConfiguration.StoragePath}\nReceived: {status.TotalReceivedFiles}   |   Last (UTC): {status.LastReceivedAtUtc:O}";
        Errors = string.Join(Environment.NewLine, status.RecentErrors);
    }

    private void ApplyLoadedSettings(AgentSettings settings)
    {
        LocalPort = settings.LocalApi.Port.ToString();
        SaasUrl = settings.LocalApi.SaasBaseUrlTemplate;
        AllowedOrigins = string.Join(", ", settings.LocalApi.AllowedOrigins);
        var d = settings.Dicom;
        AeTitle = d.AeTitle;
        ListenAddress = d.ListenAddress;
        Port = d.Port.ToString();
        StoragePath = d.StoragePath;
        CallingAes = string.Join(", ", d.AllowedCallingAeTitles);
        RemoteAddresses = string.Join(", ", d.AllowedRemoteAddresses);
        MaxAssociations = d.MaxConcurrentAssociations.ToString();
        MaxBytes = d.MaxInstanceBytes.ToString();
        ReceiverEnabled = settings.Agent.EnableReceiver;
        HistoryEnabled = settings.Agent.EnableFileHistory;
    }
    private AgentSettings ReadSettings()
    {
        if (!int.TryParse(Port, out var p) || !int.TryParse(MaxAssociations, out var max) || !long.TryParse(MaxBytes, out var bytes))
            throw new ArgumentException("Port, association limit and byte limit must be numbers.");
        return new(new DicomOptions
        {
            AeTitle = AeTitle,
            ListenAddress = ListenAddress,
            Port = p,
            StoragePath = StoragePath,
            MaxConcurrentAssociations = max,
            MaxInstanceBytes = bytes,
            AllowedCallingAeTitles = Split(CallingAes),
            AllowedRemoteAddresses = Split(RemoteAddresses)
        },
            new AgentOptions { EnableReceiver = ReceiverEnabled, EnableFileHistory = HistoryEnabled })
            { LocalApi = new() { Port = int.TryParse(LocalPort, out var localPort) ? localPort : 0, SaasBaseUrlTemplate = SaasUrl, AllowedOrigins = Split(AllowedOrigins) } };
    }
    private static string[] Split(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private void Validate()
    {
        try { var errors = ConfigurationRules.Validate(ReadSettings().Dicom); Message = errors.Length == 0 ? "Settings are valid. Host will also check storage write access on save." : string.Join(" ", errors); }
        catch (ArgumentException ex) { Message = ex.Message; }
    }
    private async Task SaveAsync()
    {
        if (IsApplyingSettings) return;
        if (busy)
        {
            Message = "Đợi thao tác với ca hiện tại hoàn tất trước khi lưu cấu hình.";
            return;
        }
        try
        {
            if (loaded is null) throw new InvalidOperationException("Bấm Làm mới để kết nối trước khi lưu.");
            var value = ReadSettings();
            var errors = ConfigurationRules.Validate(value.Dicom);
            if (errors.Length > 0) throw new ArgumentException(string.Join(" ", errors));
            CaptureRules.Validate(value.LocalApi);
            IsApplyingSettings = true;
            busy = true;
            NotifyCapture();
            Message = "Đang lưu và áp dụng cấu hình. Xác thực quản trị nếu hệ điều hành yêu cầu; dịch vụ sẽ tự khởi động lại.";
            await new InstalledSettingsUpdater().ApplyAsync(value);
            IsApplyingSettings = false;
            await RefreshAsync();
            Message = "Đã lưu và áp dụng cấu hình. Dịch vụ đã sẵn sàng, không cần chạy lệnh. Nếu đang thực hiện một ca, bấm Thực hiện / Nhận ảnh để nhận tiếp.";
        }
        catch (Exception ex) { Message = "Không lưu được cấu hình. " + ex.Message; }
        finally
        {
            IsApplyingSettings = false;
            busy = false;
            NotifyCapture();
        }
    }
    private static void EnsureSuccess(ManagementResponse response) { if (!response.Success) throw new InvalidOperationException(response.Error); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
}
internal sealed class AsyncCommand(Func<Task> action) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running;
    public async void Execute(object? parameter)
    {
        if (running) return;
        running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await action(); } finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
