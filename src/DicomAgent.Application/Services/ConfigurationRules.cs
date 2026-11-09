using System.Net;
using System.Text.RegularExpressions;
using DicomAgent.Application.Models;

namespace DicomAgent.Application.Services;

public static partial class ConfigurationRules
{
    public static bool ValidAe(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 16 &&
        value == value.Trim() && value.All(c => c >= 32 && c <= 126 && c != '\\');
    public static bool ValidUid(string? value) => value is { Length: > 0 and <= 64 } && UidRegex().IsMatch(value);
    [GeneratedRegex(@"^(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*))+$", RegexOptions.CultureInvariant)]
    private static partial Regex UidRegex();
    public static string[] Validate(DicomOptions o)
    {
        var errors = new List<string>();
        if (!ValidAe(o.AeTitle)) errors.Add("AE Title must contain 1-16 printable ASCII characters, without backslash or surrounding spaces.");
        if (o.Port is < 1 or > 65535) errors.Add("Port must be between 1 and 65535.");
        if (!IPAddress.TryParse(o.ListenAddress, out _)) errors.Add("ListenAddress must be an IPv4 or IPv6 address.");
        if (o.MaxConcurrentAssociations is < 1 or > 100) errors.Add("MaxConcurrentAssociations must be 1-100.");
        if (o.MaxInstanceBytes is < 1024 or > 2147483648) errors.Add("MaxInstanceBytes must be 1 KiB-2 GiB.");
        if (o.AllowedCallingAeTitles is null || o.AllowedCallingAeTitles.Any(a => !ValidAe(a))) errors.Add("Invalid Calling AE allowlist.");
        if (o.AllowedRemoteAddresses is null || o.AllowedRemoteAddresses.Any(a => !IPAddress.TryParse(a, out _))) errors.Add("IP allowlist must contain literal IP addresses.");
        if (o.StoragePath is null || (o.StoragePath.Length > 0 && (!Path.IsPathFullyQualified(o.StoragePath) || o.StoragePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || o.StoragePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))))
            errors.Add("StoragePath must be an absolute path without parent traversal.");
        return errors.ToArray();
    }
    public static string InstancePath(string root, DateTime date, string study, string series, string sop)
    {
        if (!ValidUid(study) || !ValidUid(series) || !ValidUid(sop)) throw new InvalidDataException("Invalid required DICOM UID.");
        return Path.Combine(Path.GetFullPath(root), date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), study, series, sop + ".dcm");
    }
}
