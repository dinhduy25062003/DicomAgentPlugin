namespace DicomAgent.Application.Services;

public static class AgentPaths
{
    public static string DefaultDataDirectory(bool windows, string commonData) => windows
        ? Path.Combine(commonData, "DicomAgent") : "/var/lib/dicom-agent";
    public static string DataDirectory => Path.GetFullPath(Environment.GetEnvironmentVariable("DICOM_AGENT_DATA") ??
        DefaultDataDirectory(OperatingSystem.IsWindows(), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)));
    public static string DefaultStorage => Path.Combine(DataDirectory, OperatingSystem.IsWindows() ? "Storage" : "storage");
    public static string SocketPath => Path.Combine(DataDirectory, "management.sock");
    public const string PipeName = "DicomAgent.Management.v1";
}
