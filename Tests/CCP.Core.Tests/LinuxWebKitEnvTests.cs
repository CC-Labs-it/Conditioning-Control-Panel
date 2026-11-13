using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// live-safety S18: on NVIDIA every WebKitGTK view stayed blank ("Failed to create GBM buffer") until
// WEBKIT_DISABLE_DMABUF_RENDERER=1. Set only on NVIDIA (it also turns off web view GPU acceleration) and never over the user.
public sealed class LinuxWebKitEnvTests
{
    [Theory]
    [InlineData("/proc/driver/nvidia/version")]
    [InlineData("/sys/module/nvidia")]
    public void NvidiaDriverWithNothingSetDisablesTheDmabufRenderer(string probe) =>
        Assert.Equal("1", LinuxWebKitEnv.DmabufDefault(null, p => p == probe));

    [Fact]
    public void WithoutNvidiaTheEnvironmentIsLeftAlone() =>
        Assert.Null(LinuxWebKitEnv.DmabufDefault(null, p => p == "/sys/module/amdgpu" || p == "/sys/module/nouveau"));

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("")]
    public void AValueTheUserSetWins(string current) =>
        Assert.Null(LinuxWebKitEnv.DmabufDefault(current, _ => true));
}
