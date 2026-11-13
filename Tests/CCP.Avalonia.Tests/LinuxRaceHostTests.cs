using System;
using System.Diagnostics;
using ConditioningControlPanel.Services.Race;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class LinuxRaceHostTests
{
    internal static Process StartCat(string program = "/bin/cat") => Process.Start(new ProcessStartInfo("/usr/bin/setsid", program) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
    [Theory, InlineData(true), InlineData(false)]
    public void EscapeDeliveryHasOneSourceAndDeduplicatesBySequence(bool x11Source)
    {
        long last = 0;
        Assert.Equal(new[] { !x11Source, false, !x11Source }, Array.ConvertAll(new long[] { 1, 1, 2 }, seq => RaceShellProtocol.AcceptEscape(ref last, seq, x11Source)));
    }
    [Fact]
    public void ProtocolRejectsMalformedAndUnboundedFrames()
    {
        Assert.ThrowsAny<Exception>(() => RaceShellProtocol.Parse("[]"));
        Assert.Throws<FormatException>(() => RaceShellProtocol.Parse(new string(' ', 65537)));
        Assert.Equal("ready", (string?)RaceShellProtocol.Parse("{\"type\":\"ready\"}")["type"]);
    }
}
