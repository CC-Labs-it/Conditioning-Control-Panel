using System;
using System.Diagnostics;
using ConditioningControlPanel.Services.Race;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class LinuxRaceHostTests
{
    internal static Process StartCat(string program = "/bin/cat") => Process.Start(new ProcessStartInfo("/usr/bin/setsid", program) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
    [Theory]
    [InlineData("x11", true, false)]
    [InlineData("wayland", true, true)]
    [InlineData("x11", false, true)]
    [InlineData(null, true, true)]
    [InlineData(null, false, true)]
    public void EscapeDeliveryHasOneSourceAndDeduplicatesBySequence(string? backend, bool listening, bool accepted)
    {
        long last = 0;
        Assert.Equal(accepted, RaceShellProtocol.AcceptEscape(ref last, 1, listening, backend));
        Assert.False(RaceShellProtocol.AcceptEscape(ref last, 1, listening, backend));
        Assert.Equal(accepted, RaceShellProtocol.AcceptEscape(ref last, 2, listening, backend));
    }
    [Fact]
    public void StartupEscapeOfUnknownBackendCountsOnceAcrossX11AndShell()
    {
        long last = 0;   // tiny clock values: never within 500 ms of a real Environment.TickCount64 claim
        Assert.True(RaceShellProtocol.Claim(false, 1000));
        Assert.False(RaceShellProtocol.AcceptEscape(ref last, 1, true, null, 1200));
        Assert.True(RaceShellProtocol.AcceptEscape(ref last, 2, true, null, 1600));
        Assert.False(RaceShellProtocol.Claim(false, 1700));
        Assert.True(RaceShellProtocol.Claim(false, 2200));
        Assert.True(RaceShellProtocol.AcceptEscape(ref last, 3, true, "wayland", 2300));
    }
    [Fact]
    public void ProtocolRejectsMalformedAndUnboundedFrames()
    {
        Assert.ThrowsAny<Exception>(() => RaceShellProtocol.Parse("[]"));
        Assert.Throws<FormatException>(() => RaceShellProtocol.Parse(new string(' ', 65537)));
        Assert.Equal("ready", (string?)RaceShellProtocol.Parse("{\"type\":\"ready\"}")["type"]);
    }
}
