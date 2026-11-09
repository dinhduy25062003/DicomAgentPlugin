using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
namespace DicomAgent.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ManagementClient client = new();
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
    public MainViewModel()
    {
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
        try
        {
            var response = await client.SendAsync(new("status")); EnsureSuccess(response);
            var s = response.Status!; Status = s.State;
            Summary = $"AE: {s.ActiveConfiguration.AeTitle}   |   {s.ActiveConfiguration.ListenAddress}:{s.ActiveConfiguration.Port}\nStorage: {s.ActiveConfiguration.StoragePath}\nReceived: {s.TotalReceivedFiles}   |   Last (UTC): {s.LastReceivedAtUtc:O}";
            Errors = string.Join(Environment.NewLine, s.RecentErrors);
            var configuration = await client.SendAsync(new("settings")); EnsureSuccess(configuration); loaded = configuration.Settings!;
            var d = loaded.Dicom;
            AeTitle = d.AeTitle; ListenAddress = d.ListenAddress; Port = d.Port.ToString(); StoragePath = d.StoragePath;
            CallingAes = string.Join(", ", d.AllowedCallingAeTitles); RemoteAddresses = string.Join(", ", d.AllowedRemoteAddresses);
            MaxAssociations = d.MaxConcurrentAssociations.ToString(); MaxBytes = d.MaxInstanceBytes.ToString();
            ReceiverEnabled = loaded.Agent.EnableReceiver; HistoryEnabled = loaded.Agent.EnableFileHistory;
            var history = await client.SendAsync(new("history")); EnsureSuccess(history);
            History.Clear(); foreach (var item in history.History!) History.Add(item);
            Message = s.RestartRequired ? "Settings saved. Restart the operating system service to apply changes." : history.Error ?? "Connected. Showing up to 100 recent files.";
        }
        catch (Exception ex) { Status = "Error / Disconnected"; Message = "Cannot read Host state. " + ex.Message; }
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
            new AgentOptions { EnableReceiver = ReceiverEnabled, EnableFileHistory = HistoryEnabled });
    }
    private static string[] Split(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private void Validate()
    {
        try { var errors = ConfigurationRules.Validate(ReadSettings().Dicom); Message = errors.Length == 0 ? "Settings are valid. Host will also check storage write access on save." : string.Join(" ", errors); }
        catch (ArgumentException ex) { Message = ex.Message; }
    }
    private async Task SaveAsync()
    {
        try
        {
            if (loaded is null) throw new InvalidOperationException("Refresh to connect before saving.");
            var value = ReadSettings(); var errors = ConfigurationRules.Validate(value.Dicom);
            if (errors.Length > 0) throw new ArgumentException(string.Join(" ", errors));
            EnsureSuccess(await client.SendAsync(new("save", value)));
            Message = "Saved. Restart DicomAgent / dicom-agent.service to apply changes.";
        }
        catch (Exception ex) { Message = "Save failed. " + ex.Message; }
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
