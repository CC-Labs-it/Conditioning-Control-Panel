using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF BackRoomHostService.cs:306-325: both directions preserve a run until Yes.
    [Theory]
    [InlineData("breakoutdemo", "breakout")]
    [InlineData("breakout", "breakoutdemo")]
    public void Breakout_SwitchRequiresConsent_AndKeepsOneHost(string from, string to) => Run((_, launcher) =>
    {
        launcher.Hide();
        CoreEntitlement.HasLabProvider = () => true;
        var current = GameWindow.Launch(from)!;
        current.HandleMessage("{\"type\":\"ready\"}");
        int prompts = 0, closed = 0;
        current.Closed += (_, _) => closed++;
        var url = current.PageUrl;
        bool leave = false;
        var previous = GameWindow.ConfirmBreakoutSwitch;
        GameWindow.ConfirmBreakoutSwitch = owner =>
        {
            Assert.Same(current, owner);
            Assert.True(current.IsVisible);
            prompts++;
            return Task.FromResult(leave);
        };
        try
        {
            Assert.Same(current, GameWindow.Launch(from));
            Assert.Equal(0, prompts);
            Assert.Null(GameWindow.Launch(to));
            Assert.Equal(1, prompts);
            Assert.Equal(0, closed);
            Assert.Same(current, GameWindow.Launch(from));
            Assert.Equal(url, current.PageUrl);
            Assert.True(current.IsReady);
            leave = true;
            GameWindow.Launch(to);
            Assert.Equal(2, prompts);
            Assert.Equal(1, closed);
            Assert.False(current.IsVisible);
            var next = GameWindow.Launch(to)!;
            Assert.NotSame(current, next);
            Assert.Equal(to, next.Spec.Id);
            Assert.True(next.IsVisible);
            Assert.Equal(2, prompts);
        }
        finally { GameWindow.ConfirmBreakoutSwitch = previous; }
    });

    [Fact]
    public void Breakout_PendingSwitchDoesNotDuplicatePrompt_OrReopenAfterPanic() => Run((_, launcher) =>
    {
        launcher.Hide();
        CoreEntitlement.HasLabProvider = () => true;
        var current = GameWindow.Launch("breakoutdemo")!;
        var answer = new TaskCompletionSource<bool>();
        int prompts = 0;
        var previous = GameWindow.ConfirmBreakoutSwitch;
        GameWindow.ConfirmBreakoutSwitch = _ => { prompts++; return answer.Task; };
        try
        {
            GameWindow.Launch("breakout");
            GameWindow.Launch("breakout");
            Assert.Equal(1, prompts);
            Assert.Same(current, GameWindow.Launch("breakoutdemo"));
            GameWindow.CloseAllForPanic();
            answer.SetResult(true);
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally { GameWindow.ConfirmBreakoutSwitch = previous; }
    });

    // WPF BackRoomHostService.cs:589-639, 689-720: init, edits, cloud restore and detach.
    [Theory]
    [InlineData("breakout", MotionLevel.Full, "full", false)]
    [InlineData("breakout", MotionLevel.Reduced, "reduced", true)]
    [InlineData("breakout", MotionLevel.Off, "off", true)]
    [InlineData("breakoutdemo", MotionLevel.Off, "off", true)]
    public void Breakout_MotionFollowsInitEditsAndSettingsReplacement(string id, MotionLevel initial, string wire, bool reduced) => Run((_, launcher) =>
    {
        launcher.Hide();
        CoreEntitlement.HasLabProvider = () => true;
        var service = CoreSettings.Service!;
        var settings = service.Current;
        settings.MotionLevel = initial;
        var window = GameWindow.Launch(id)!;
        var sent = new List<JObject>();
        window.Posted += json => sent.Add(JObject.Parse(json));
        window.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal(wire, (string?)sent[0]["motion"]);
        Assert.Equal(reduced, (bool)sent[0]["reduced"]!);
        sent.Clear();
        settings.MotionLevel = initial == MotionLevel.Full ? MotionLevel.Off : MotionLevel.Full;
        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(sent);
        Assert.Equal(initial == MotionLevel.Full ? "off" : "full", (string?)sent[^1]["motion"]);
        Assert.Equal(initial == MotionLevel.Full, (bool)sent[^1]["reduced"]!);
        service.RestoreFrom(new AppSettings { MotionLevel = MotionLevel.Reduced });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("reduced", (string?)sent[^1]["motion"]);
        Assert.True((bool)sent[^1]["reduced"]!);
        sent.Clear();
        settings.MotionLevel = MotionLevel.Reduced; // replaced model is detached
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(sent);
        service.Current.MotionLevel = MotionLevel.Off;
        Dispatcher.UIThread.RunJobs();
        Assert.Single(sent);
        Assert.Equal("off", (string?)sent[0]["motion"]);
        Assert.True((bool)sent[0]["reduced"]!);
        window.Close();
        sent.Clear();
        service.Current.MotionLevel = MotionLevel.Full;
        service.RestoreFrom(new AppSettings { MotionLevel = MotionLevel.Off });
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(sent);
    });
    [Fact]
    public void Breakout_SettingsWireCarriesSafetyAndPlayerChoices() => Run((_, launcher) =>
    {
        launcher.Hide();
        var s = CoreSettings.Current;
        s.MotionLevel = MotionLevel.Reduced;
        s.BackRoomFxIntensity = ConditioningControlPanel.Services.BackRoom.BackRoomFxIntensity.Full;
        s.BackRoomTunnel = true;
        s.BackRoomInvertLook = true;
        s.SkillPoints = 42;
        var window = GameWindow.Launch("breakoutdemo")!;
        var sent = new List<JObject>();
        window.Posted += json => sent.Add(JObject.Parse(json));
        window.HandleMessage("{\"type\":\"ready\"}");
        var init = sent[0];
        Assert.Equal("calm", (string?)init["intensity"]);
        Assert.Equal("full", (string?)init["intensityChoice"]);
        Assert.True((bool)init["gates"]!["tunnel"]!);
        Assert.True((bool)init["invertLook"]!);
        Assert.Equal(42, (int)init["sp"]!);
        Assert.NotNull(init["media"]!["effective"]);
        sent.Clear();
        s.BackRoomTunnel = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False((bool)sent[^1]["gates"]!["tunnel"]!);
        window.Close();
    });
    [Fact]
    public void Breakout_MediaRequestsHonorConsentAndSource() => Run((_, launcher) =>
    {
        launcher.Hide();
        var s = CoreSettings.Current;
        s.BackRoomMediaSource = "online";
        s.RemoteMediaConsented = false;
        s.BackRoomMediaSubs = new();
        s.FypOnlineNiches = new();
        s.FypOnlineCustomSubs = new();
        Assert.NotEmpty(GameWindow.BreakoutChannels(s));
        var window = GameWindow.Launch("breakoutdemo")!;
        int calls = 0;
        window.BreakoutClipsForTests = () => { calls++; return new[] {
            new ConditioningControlPanel.Services.Chaos.GameMediaManifest.Entry("clip", "/ccp.assets/.temp/clip.webm") }; };
        var sent = new List<JObject>();
        window.Posted += json => { var frame = JObject.Parse(json); if ((string?)frame["type"] == "media") sent.Add(frame); };
        void Request(string id, string source)
        {
            window.HandleMessage($"{{\"type\":\"media-request\",\"station\":\"breakout\",\"reqId\":\"{id}\",\"source\":\"{source}\"}}");
            var end = DateTime.UtcNow.AddSeconds(10);
            while (sent.Count == 0 && DateTime.UtcNow < end) Dispatcher.UIThread.RunJobs();
            Assert.Single(sent);
        }
        Request("a", "online");
        Assert.Equal("local", (string?)sent[0]["source"]);
        Assert.Equal(0, calls);
        sent.Clear();
        s.RemoteMediaConsented = true;
        Request("b", "auto");
        Assert.Equal("online", (string?)sent[0]["source"]);
        Assert.Equal("online", (string?)sent[0]["gifs"]![0]!["src"]);
        Assert.Contains("clip.webm", (string?)sent[0]["gifs"]![0]!["url"]);
        sent.Clear();
        Request("c", "bundled");
        Assert.Equal("bundled", (string?)sent[0]["source"]);
        Assert.Empty(sent[0]["gifs"]!);
        sent.Clear();
        s.RemoteMediaConsented = false;
        Request("d", "mixed");
        Assert.Equal("local", (string?)sent[0]["source"]);
        Assert.All(sent[0]["gifs"]!, g => Assert.NotEqual("online", (string?)g["src"]));
        window.Close();
    });
}
