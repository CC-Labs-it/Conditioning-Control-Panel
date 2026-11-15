using System;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
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
}
