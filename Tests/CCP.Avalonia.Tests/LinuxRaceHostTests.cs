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
    [Theory]
    [InlineData("x11", true, false)]
    [InlineData("wayland", true, true)]
    [InlineData("x11", false, true)]
    [InlineData(null, true, false)]
    public void EscapeDeliveryHasOneSourceAndDeduplicatesBySequence(string? backend, bool listening, bool accepted)
    {
        long last = 0;
        Assert.Equal(accepted, RaceShellProtocol.AcceptEscape(ref last, 1, listening, backend));
        Assert.False(RaceShellProtocol.AcceptEscape(ref last, 1, listening, backend));
        Assert.Equal(accepted, RaceShellProtocol.AcceptEscape(ref last, 2, listening, backend));
    }
    [Fact]
    public void LaunchUrlTravelsOnStdinAndNotInProcessArguments()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-race-" + Guid.NewGuid());
        System.IO.Directory.CreateDirectory(dir);
        var engine = System.IO.Path.Combine(dir, "engine"); var shell = System.IO.Path.Combine(dir, "shell");
        var args = System.IO.Path.Combine(dir, "args"); var frame = System.IO.Path.Combine(dir, "frame");
        System.IO.File.WriteAllText(shell, "");
        System.IO.File.WriteAllText(engine, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{args}'\nIFS= read -r line\nprintf '%s' \"$line\" > '{frame}'\nsleep 60\n");
        System.IO.File.SetUnixFileMode(engine, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute);
        var url = new Uri("http://127.0.0.1:1234/race?token=fake-test-token");
        try
        {
            Assert.True(LinuxRaceHost.TryLaunch(url, engine, shell));
            Assert.True(System.Threading.SpinWait.SpinUntil(() => System.IO.File.Exists(frame) && new System.IO.FileInfo(frame).Length > 0, 10000));
            Assert.DoesNotContain("fake-test-token", System.IO.File.ReadAllText(args));
            var launch = JObject.Parse(System.IO.File.ReadAllText(frame));
            Assert.Equal("launch", (string?)launch["type"]);
            Assert.Equal(url.AbsoluteUri, (string?)launch["url"]);
        }
        finally { LinuxRaceHost.Current?.Dispose(); System.IO.Directory.Delete(dir, true); }
    }
    [Fact]
    public void InitTransportFailureDoesNotEscapeTheUiCallback()
    {
        var attempts = 0;
        using var session = new GlassroadSession(_ => { attempts++; throw new System.IO.IOException("closed pipe"); }, () => { }, () => { });
        Assert.Null(Record.Exception(session.PostGlassroadInit));
        Assert.Equal(1, attempts);
    }
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
