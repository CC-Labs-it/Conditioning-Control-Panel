using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Race;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class LinuxRaceHostTests
{
    [Fact]
    public void ProtocolRejectsMalformedAndUnboundedFrames()
    {
        Assert.ThrowsAny<Exception>(() => RaceShellProtocol.Parse("[]"));
        Assert.Throws<FormatException>(() => RaceShellProtocol.Parse(new string(' ', 65537)));
        Assert.Equal("ready", (string?)RaceShellProtocol.Parse("{\"type\":\"ready\"}")["type"]);
    }
    [Fact]
    public void SharedSessionInitReadyHeartbeatAndExit()
    {
        var sent = new List<JObject>(); var closed = 0;
        using var s = new GlassroadSession(o => sent.Add(JObject.FromObject(o)), () => closed++, () => { });
        s.PostGlassroadInit();
        s.HandleGlassroad(new JObject { ["type"] = "heartbeat" });
        s.HandleGlassroad(new JObject { ["type"] = "ready" });
        Assert.True(s.Ready);
        Assert.Equal(3, sent.Count(x => (string?)x["type"] == "init"));
        Assert.NotNull(sent[0]["gentle"]);
        var later = DateTime.UtcNow.AddSeconds(25);
        Assert.Equal("ping", s.CheckGlassroadWatch(later));
        s.HandleGlassroad(new JObject { ["type"] = "pong" });
        Assert.Equal("ok", s.CheckGlassroadWatch(DateTime.UtcNow));
        Assert.Equal("ping", s.CheckGlassroadWatch(later));
        Assert.Equal("closed", s.CheckGlassroadWatch(later.AddSeconds(5)));
        s.HandleGlassroad(new JObject { ["type"] = "exit" });
        s.HandleGlassroad(new JObject { ["type"] = "menu-back" });
        Assert.Equal(3, closed);
    }
    [Fact]
    public void SilentBootClosesAndDisposedSessionDoesNotWatch()
    {
        var closed = 0;
        var s = new GlassroadSession(_ => { }, () => closed++, () => { });
        Assert.Equal("boot-timeout", s.CheckGlassroadWatch(DateTime.UtcNow.AddSeconds(46)));
        Assert.Equal(1, closed); s.Dispose();
        Assert.Equal("idle", s.CheckGlassroadWatch(DateTime.UtcNow.AddMinutes(1)));
    }
    [Fact]
    public void PanicKillsExactShellAndItsProcessGroup()
    {
        if (!OperatingSystem.IsLinux()) return;
        var start = new ProcessStartInfo("/usr/bin/setsid") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/bin/sh"); start.ArgumentList.Add("-c"); start.ArgumentList.Add("sleep 60 & echo $!; wait");
        using var p = Process.Start(start)!;
        var child = int.Parse(p.StandardOutput.ReadLine()!);
        using var host = new LinuxRaceHost(p);
        try { host.Kill(); Assert.True(p.WaitForExit(5000)); Assert.True(System.Threading.SpinWait.SpinUntil(() => !System.IO.File.Exists($"/proc/{child}/stat") || System.IO.File.ReadAllText($"/proc/{child}/stat").Split(' ')[2] == "Z", 5000)); }
        finally { host.Kill(); try { Process.GetProcessById(child).Kill(); } catch { } }
    }
}
