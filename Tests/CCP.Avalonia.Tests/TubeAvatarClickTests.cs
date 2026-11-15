using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class TubeAvatarClickTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public Task DoubleClickOpensChatOrShowsMutedForTwoSeconds() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var oldProvider = CoreAi.IsAvailableProvider;
        var settings = CoreSettings.Current;
        var old = (settings.AiChatEnabled, settings.AvatarMuted);
        var tube = new AvatarTubeWindow(null);
        try
        {
            settings.AiChatEnabled = true; settings.AvatarMuted = false;
            CoreAi.IsAvailableProvider = () => true;
            var clock = new Clock(); tube.SpeechClock = clock;
            tube.HandleAvatarClick(); clock.Now += TimeSpan.FromMilliseconds(200); tube.HandleAvatarClick();
            Assert.True(tube.FindControl<Border>("InputPanel")!.IsVisible);
            tube.FindControl<Border>("InputPanel")!.IsVisible = false;
            settings.AvatarMuted = true;
            Action? fire = null; TimeSpan delay = default;
            tube.SpeechDelayScheduler = (d, f) => { delay = d; fire = f; return () => { }; };
            clock.Now += TimeSpan.FromSeconds(1); tube.HandleAvatarClick();
            clock.Now += TimeSpan.FromMilliseconds(200); tube.HandleAvatarClick();
            Assert.False(tube.FindControl<Border>("InputPanel")!.IsVisible);
            Assert.Equal("MUTED", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.Equal(TimeSpan.FromSeconds(2), delay);
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            fire!(); Assert.False(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
        }
        finally { tube.Close(); CoreAi.IsAvailableProvider = oldProvider; (settings.AiChatEnabled, settings.AvatarMuted) = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TwentyClicksReachAchievementEngine() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var property = typeof(AvApp).GetProperty("Achievements", BindingFlags.Static | BindingFlags.NonPublic)!;
        var old = property.GetValue(null);
        var dir = Directory.CreateTempSubdirectory("tube-click-");
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir.FullName, "achievements.json")));
        property.SetValue(null, engine);
        var tube = new AvatarTubeWindow(null);
        try
        {
            for (var i = 0; i < 20; i++) tube.HandleAvatarClick();
            Assert.True(engine.Progress.IsUnlocked("neon_obsession"));
        }
        finally { tube.Close(); property.SetValue(null, old); dir.Delete(true); }
        return Task.CompletedTask;
    });
}
