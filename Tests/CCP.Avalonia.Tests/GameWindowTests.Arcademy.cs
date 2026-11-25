using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Arcademy_LibraryRemove_PersistsAndPushesReplacement() => Run((_, _) =>
    {
        var s = CoreSettings.Current;
        s.TryAddLibrarySub("hypno");
        var w = new GameWindow(new GameWindow.Game("arcademy", "launcher_game_arcademy_title", "arcademy/index.html"));
        JObject? frame = null;
        w.Posted += json => frame = JObject.Parse(json);
        try
        {
            w.HandleMessage("{\"type\":\"library-remove\",\"name\":\"hypno\"}");
            Assert.DoesNotContain(s.RemoteSubLibrary, e => e.Name == "hypno");
            Assert.True(CoreSettings.Service!.HasPendingSave);
            CoreSettings.Service.SaveImmediate();
            Assert.DoesNotContain(new SettingsService().Current.RemoteSubLibrary, e => e.Name == "hypno");
            Assert.Equal("library", (string?)frame!["type"]);
            Assert.Empty((JArray)frame["subLibrary"]!);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void Arcademy_Settings_AllowKnownAndRefuseUnknown() => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("arcademy", "title", "arcademy/index.html"));
        try
        {
            CoreSettings.Current.ArcademyAudioMute = false;
            CoreSettings.Current.ArcademySettingsJson = "{}";
            w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"unknown\",\"value\":true}");
            Assert.Equal("{}", CoreSettings.Current.ArcademySettingsJson);
            w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"audioMute\",\"value\":true}");
            Assert.True(CoreSettings.Current.ArcademyAudioMute);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void Arcademy_Keybinds_AreStoredAndMalformedInputDoesNotEraseThem() => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("arcademy", "title", "arcademy/index.html"));
        try
        {
            w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"keybinds\",\"value\":{\"jump\":\"J\"}}");
            Assert.Equal("J", (string?)JObject.Parse(CoreSettings.Current.ArcademyKeybindsJson)["jump"]);
            w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"keybinds\",\"value\":\"bad\"}");
            Assert.Equal("J", (string?)JObject.Parse(CoreSettings.Current.ArcademyKeybindsJson)["jump"]);
        }
        finally { w.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Arcademy_Keybinds_RefusalEchoesStoredValue(bool oversized) => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("arcademy", "title", "arcademy/index.html"));
        try
        {
            CoreSettings.Current.ArcademyKeybindsJson = "{\"jump\":\"J\"}";
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            JToken value = oversized ? new JObject { ["jump"] = new string('x', 7001) } : new JValue("bad");
            w.HandleMessage(new JObject { ["type"] = "set-setting", ["key"] = "keybinds", ["value"] = value }.ToString());
            var reply = Assert.Single(frames);
            Assert.Equal("setting", (string?)reply["type"]);
            Assert.Equal("keybinds", (string?)reply["key"]);
            Assert.Equal("J", (string?)reply["value"]?["jump"]);
            Assert.Equal("J", (string?)JObject.Parse(CoreSettings.Current.ArcademyKeybindsJson)["jump"]);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void Arcademy_Heartbeat_UsesClassDeadlineAndStopsOnClose() => Run((_, _) =>
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldClock = GameWindow.ArcademyUtcNow;
        GameWindow.ArcademyUtcNow = () => now;
        var w = new GameWindow(new GameWindow.Game("arcademy", "launcher_game_arcademy_title", "arcademy/index.html"));
        try
        {
            w.Show();
            Assert.True(w.ArcademyWatchRunning);
            Assert.Equal("idle", w.CheckArcademyHeartbeat(now.AddMinutes(1)));
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.Equal("ok", w.CheckArcademyHeartbeat(now.AddSeconds(20)));
            w.HandleMessage("{\"type\":\"class-started\"}");
            Assert.Equal("ok", w.CheckArcademyHeartbeat(now.AddSeconds(12)));
            w.HandleMessage("{\"type\":\"class-left\"}");
            Assert.Equal("ok", w.CheckArcademyHeartbeat(now.AddSeconds(13)));
            now = now.AddSeconds(13);
            w.HandleMessage("{\"type\":\"pong\"}");
            w.HandleMessage("{\"type\":\"class-started\"}");
            Assert.Equal("ok", w.CheckArcademyHeartbeat(now.AddSeconds(12)));
            Assert.Equal("recovering", w.CheckArcademyHeartbeat(now.AddSeconds(13)));
            w.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(w.ArcademyWatchRunning);
            Assert.Null(GameWindow.OpenForTests("arcademy"));
        }
        finally { w.Close(); GameWindow.ArcademyUtcNow = oldClock; }
    });

    [Fact]
    public void Arcademy_Heartbeat_RecoversOnce() => Run((_, _) =>
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldClock = GameWindow.ArcademyUtcNow;
        var oldRetry = GameWindow.ArcademyRelaunchedOnce;
        GameWindow.ArcademyUtcNow = () => now;
        GameWindow.ArcademyRelaunchedOnce = false;
        var w = new GameWindow(new GameWindow.Game("arcademy", "launcher_game_arcademy_title", "arcademy/index.html"));
        try
        {
            w.Show();
            w.HandleMessage("{\"type\":\"ready\"}");
            now = now.AddSeconds(21);
            Assert.Equal("recovering", w.CheckArcademyHeartbeat(now));
            Dispatcher.UIThread.RunJobs();
            var replacement = GameWindow.OpenForTests("arcademy");
            Assert.NotNull(replacement);
            Assert.NotSame(w, replacement);
            Assert.False(w.ArcademyWatchRunning);
            replacement.HandleMessage("{\"type\":\"ready\"}");
            now = now.AddSeconds(21);
            Assert.Equal("recovering", replacement.CheckArcademyHeartbeat(now));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(GameWindow.OpenForTests("arcademy"));
            Assert.False(replacement.ArcademyWatchRunning);
        }
        finally { w.Close(); GameWindow.ArcademyUtcNow = oldClock; GameWindow.ArcademyRelaunchedOnce = oldRetry; }
    });
    [Fact]
    public void Arcademy_DoorGatesBootsAndPanicCloses() => Run((_, _) =>
    {
        var oldLab = CoreEntitlement.HasLabProvider;
        var oldDenied = CoreEntitlement.ShowDeniedHandler;
        var settings = CoreSettings.Current;
        bool oldAudio = settings.AudioOnlySession;
        var oldMotion = settings.MotionLevel;
        try
        {
            CoreEntitlement.ShowDeniedHandler = _ => { };
            CoreEntitlement.HasLabProvider = () => false;
            Assert.Null(GameWindow.Launch("arcademy"));
            CoreEntitlement.HasLabProvider = () => true;
            settings.AudioOnlySession = true;
            Assert.Null(GameWindow.Launch("arcademy"));
            settings.AudioOnlySession = false;
            var w = Assert.IsType<GameWindow>(GameWindow.Launch("arcademy"));
            Assert.EndsWith("/arcademy/index.html", w.PageUrl!.AbsolutePath);
            Assert.True(w.Web.InjectCarrier);
            Assert.Same(w, GameWindow.Launch("arcademy"));
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.True(w.IsReady);
            // WPF SeedNativeState (ArcademyHostService.cs:544-568): an idle boot sends no resume.
            Assert.DoesNotContain(frames, f => (string?)f["type"] == "suspend");
            var init = Assert.Single(frames, f => (string?)f["type"] == "init");
            Assert.Equal(1, (int?)init["protocol"]);
            Assert.NotNull(init["meta"]);
            Assert.NotNull(init["caps"]);
            w.HandleMessage("{\"type\":\"meta-command\",\"op\":\"set\",\"key\":\"test_value\",\"value\":7}");
            Assert.Contains(frames, f => (string?)f["type"] == "meta" && (string?)f["key"] == "test_value" && (int?)f["value"] == 7);
            settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(frames, f => (string?)f["type"] == "setting" && (string?)f["key"] == "motionLevel" && (int?)f["value"] == 0);
            w.HandleMessage("{\"type\":\"fullscreen-request\",\"on\":true}");
            Assert.Contains(frames, f => (string?)f["type"] == "fullscreen" && (bool?)f["on"] == true);
            settings.AudioOnlySession = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(frames, f => (string?)f["type"] == "suspend" && (bool?)f["on"] == true);
            GameWindow.CloseAllForPanic();
            Assert.Null(GameWindow.OpenForTests("arcademy"));
        }
        finally { settings.AudioOnlySession = oldAudio; settings.MotionLevel = oldMotion; CoreEntitlement.HasLabProvider = oldLab; CoreEntitlement.ShowDeniedHandler = oldDenied; }
    });

    [Fact]
    public void Arcademy_BootDeadlineTracksProgress() => Run((_, _) =>
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldClock = GameWindow.ArcademyUtcNow;
        GameWindow.ArcademyUtcNow = () => now;
        var oldShow = GameWindow.ShowArcademyBootError;
        var shown = new List<string>();
        GameWindow.ShowArcademyBootError = shown.Add;
        var w = new GameWindow(GameWindow.ArcademySpec);
        var w2 = new GameWindow(GameWindow.ArcademySpec);
        try
        {
            w.Show();
            Assert.False(w.CheckArcademyBoot(now.AddSeconds(44)));
            now = now.AddSeconds(40);
            w.HandleMessage("{\"type\":\"heartbeat\"}");
            Assert.False(w.CheckArcademyBoot(now.AddSeconds(44)));
            Assert.Empty(shown);
            Assert.True(w.CheckArcademyBoot(now.AddSeconds(45)));
            Assert.Null(GameWindow.OpenForTests("arcademy"));
            // WPF ArcademyHostService.cs:5986-6005: both the deadline and a page boot-error explain themselves.
            Assert.Equal(Loc.GetF("arcademy_boot_error_body", Loc.Get("launcher_game_arcademy_title"), "boot deadline: no progress for 45s"), Assert.Single(shown));
            w2.Show();
            w2.HandleMessage("{\"type\":\"boot-error\",\"msg\":\"renderer failed\"}");
            Assert.Null(GameWindow.OpenForTests("arcademy"));
            Assert.Equal(2, shown.Count);
            Assert.EndsWith("renderer failed", shown[1]);
        }
        finally { w.Close(); w2.Close(); GameWindow.ArcademyUtcNow = oldClock; GameWindow.ShowArcademyBootError = oldShow; }
    });

    // WPF ArcademyHostService.cs:204,494-504: the refusal is explained, never silent.
    [Fact]
    public void Arcademy_AudioOnlyRefusalShowsToast() => Run((_, _) =>
    {
        var (oldLab, oldAudio, oldToasts) = (CoreEntitlement.HasLabProvider, CoreSettings.Current.AudioOnlySession, global::ConditioningControlPanel.Avalonia.App.Notifications);
        var host = new global::Avalonia.Controls.StackPanel();
        try
        {
            global::ConditioningControlPanel.Avalonia.App.Notifications = new();
            global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(host);
            CoreEntitlement.HasLabProvider = () => true;
            CoreSettings.Current.AudioOnlySession = true;
            Assert.Null(GameWindow.Launch("arcademy"));
            var text = ConditioningControlPanel.Localization.Loc.Get("arcademy_audio_only_refused");
            Assert.Contains(global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(host)
                .OfType<global::Avalonia.Controls.TextBlock>(), t => t.Text == text);
        }
        finally
        {
            foreach (var toast in host.Children.OfType<global::Avalonia.Controls.Border>()) (toast.Tag as DispatcherTimer)?.Stop();
            (CoreEntitlement.HasLabProvider, CoreSettings.Current.AudioOnlySession, global::ConditioningControlPanel.Avalonia.App.Notifications) = (oldLab, oldAudio, oldToasts);
        }
    });

    // WPF ArcademyHostService.cs:318,741 (autoplay grant) and :5939 (heartbeat every 5 s).
    [Fact]
    public void Arcademy_AutoplayGrantAndHeartbeatCadence() => Run((_, _) =>
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var (oldClock, oldRetry) = (GameWindow.ArcademyUtcNow, GameWindow.ArcademyRelaunchedOnce);
        GameWindow.ArcademyUtcNow = () => now;
        GameWindow.ArcademyRelaunchedOnce = true;
        var w = new GameWindow(GameWindow.ArcademySpec);
        try
        {
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.Show();
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.Contains("--autoplay-policy=no-user-gesture-required", w.Web.WindowsBrowserArguments);
            Assert.Equal(OperatingSystem.IsWindows(), (bool?)frames.Single(f => (string?)f["type"] == "init")["autoplayOk"]);
            Assert.Equal("wait", w.ArcademyWatchTick(now.AddSeconds(4.9)));
            Assert.Equal("ok", w.ArcademyWatchTick(now.AddSeconds(20)));
            Assert.Equal("wait", w.ArcademyWatchTick(now.AddSeconds(24.5)));
            Assert.Equal("recovering", w.ArcademyWatchTick(now.AddSeconds(25)));
        }
        finally { w.Close(); (GameWindow.ArcademyUtcNow, GameWindow.ArcademyRelaunchedOnce) = (oldClock, oldRetry); }
    });
}
