using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using Xunit;
namespace DicomAgent.UnitTests;
public sealed class ConfigurationTests
{
    [Fact] public void DefaultsAreValid() => Assert.Empty(ConfigurationRules.Validate(new()));
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void InvalidPort(int port) => Assert.NotEmpty(ConfigurationRules.Validate(new() { Port = port }));
    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void BoundaryPorts(int port) => Assert.Empty(ConfigurationRules.Validate(new() { Port = port }));
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("AE\\BAD")]
    [InlineData("01234567890123456")]
    [InlineData("é")]
    [InlineData("AE\n")]
    public void InvalidAe(string ae) => Assert.False(ConfigurationRules.ValidAe(ae));
    [Theory]
    [InlineData("AE")]
    [InlineData("DICOM_AGENT")]
    [InlineData("AE TITLE")]
    public void ValidAe(string ae) => Assert.True(ConfigurationRules.ValidAe(ae));
    [Theory]
    [InlineData("../1")]
    [InlineData("1/2")]
    [InlineData("1.02")]
    [InlineData("1..2")]
    [InlineData("1.2/../../bad")]
    public void RejectUidTraversal(string uid) => Assert.Throws<InvalidDataException>(() => ConfigurationRules.InstancePath(Path.GetTempPath(), DateTime.UtcNow, uid, "1.2", "1.3"));
    [Fact]
    public void FilenameContainsOnlyUids()
    {
        var path = ConfigurationRules.InstancePath(Path.GetTempPath(), new DateTime(2026, 11, 9), "1.2", "1.3", "1.4");
        Assert.Equal(Path.Combine(Path.GetTempPath(), "2026-11-09", "1.2", "1.3", "1.4.dcm"), path);
    }
    [Fact]
    public void DefaultStorageResolution()
    {
        Assert.Equal("/var/lib/dicom-agent", AgentPaths.DefaultDataDirectory(false, "ignored"));
        Assert.Equal(Path.Combine(@"C:\ProgramData", "DicomAgent"), AgentPaths.DefaultDataDirectory(true, @"C:\ProgramData"));
    }
    [Fact]
    public void ValidateAddressesAndLimits()
    {
        Assert.NotEmpty(ConfigurationRules.Validate(new() { ListenAddress = "hostname" }));
        Assert.NotEmpty(ConfigurationRules.Validate(new() { AllowedRemoteAddresses = ["any"] }));
        Assert.NotEmpty(ConfigurationRules.Validate(new() { AllowedCallingAeTitles = ["bad\\ae"] }));
        Assert.NotEmpty(ConfigurationRules.Validate(new() { StoragePath = "../relative" }));
        Assert.NotEmpty(ConfigurationRules.Validate(new() { MaxConcurrentAssociations = 0 }));
        Assert.NotEmpty(ConfigurationRules.Validate(new() { MaxInstanceBytes = 0 }));
    }
    [Fact]
    public async Task RejectOversizedIpcBeforeAllocating()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(int.MaxValue));
        await Assert.ThrowsAsync<InvalidDataException>(() => ManagementWire.ReadAsync<ManagementResponse>(stream, default));
    }
}
