using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
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
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        var oldProvider = CoreAi.IsAvailableProvider;
        var settings = CoreSettings.Current;
        var old = (settings.AiChatEnabled, settings.AvatarMuted);
        var property = typeof(AvApp).GetProperty("Achievements", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousEngine = property.GetValue(null);
        var dir = Directory.CreateTempSubdirectory("tube-click-isolation-");
        var sentinel = new AchievementEngine(new AchievementStore(Path.Combine(dir.FullName, "achievements.json")));
        var tube = new AvatarTubeWindow(null);
        try
        {
            property.SetValue(null, sentinel);
            property.SetValue(null, null);
            LocalizationManager.Instance.SetLanguage("en");
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
        finally
        {
            tube.Close();
            property.SetValue(null, previousEngine);
            CoreAi.IsAvailableProvider = oldProvider;
            LocalizationManager.Instance.SetLanguage(oldLanguage);
            (settings.AiChatEnabled, settings.AvatarMuted) = old;
            dir.Delete(true);
        }
        Assert.Equal(0, sentinel.Progress.AvatarClickCount);
        return Task.CompletedTask;
    });

    [Fact]
    public Task OnlyLeftPressesReachClickBehavior() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var property = typeof(AvApp).GetProperty("Achievements", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = property.GetValue(null);
        var dir = Directory.CreateTempSubdirectory("tube-pointer-");
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir.FullName, "achievements.json")));
        var settings = CoreSettings.Current;
        var old = (settings.AiChatEnabled, settings.AvatarMuted, CoreAi.IsAvailableProvider);
        var tube = new AvatarTubeWindow(null);
        try
        {
            property.SetValue(null, engine);
            settings.AiChatEnabled = true; settings.AvatarMuted = false;
            CoreAi.IsAvailableProvider = () => true;
            var clock = new Clock(); tube.SpeechClock = clock;
            var avatar = tube.FindControl<Border>("AvatarBorder")!;
            var pointer = new global::Avalonia.Input.Pointer(global::Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
            void Press(RawInputModifiers modifiers, PointerUpdateKind kind) => avatar.RaiseEvent(
                new PointerPressedEventArgs(avatar, pointer, tube, default, 0,
                    new PointerPointProperties(modifiers, kind), KeyModifiers.None));
            settings.AvatarMuted = true;
            Press(RawInputModifiers.MiddleMouseButton, PointerUpdateKind.MiddleButtonPressed);
            Press(RawInputModifiers.MiddleMouseButton, PointerUpdateKind.MiddleButtonPressed);
            Assert.False(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            settings.AvatarMuted = false;
            Press(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed);
            Press(RawInputModifiers.LeftMouseButton | RawInputModifiers.MiddleMouseButton, PointerUpdateKind.MiddleButtonPressed);
            Assert.Equal(0, engine.Progress.AvatarClickCount);
            Assert.False(tube.FindControl<Border>("InputPanel")!.IsVisible);
            Press(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            Assert.Equal(1, engine.Progress.AvatarClickCount);
            clock.Now += TimeSpan.FromMilliseconds(200);
            Press(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            Assert.Equal(2, engine.Progress.AvatarClickCount);
            Assert.True(tube.FindControl<Border>("InputPanel")!.IsVisible);
        }
        finally
        {
            tube.Close(); property.SetValue(null, previous);
            (settings.AiChatEnabled, settings.AvatarMuted, CoreAi.IsAvailableProvider) = old;
            dir.Delete(true);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("en", "MUTED")]
    [InlineData("de", "STUMMGESCHALTET")]
    [InlineData("es", "SILENCIADO")]
    [InlineData("fr", "SON COUPÉ")]
    [InlineData("it", "AUDIO DISATTIVATO")]
    [InlineData("ja", "ミュート中")]
    [InlineData("ko", "음소거됨")]
    [InlineData("pt-BR", "SOM DESATIVADO")]
    [InlineData("ru", "ЗВУК ВЫКЛЮЧЕН")]
    [InlineData("zh-CN", "已静音")]
    public Task MutedIndicatorUsesSelectedLanguage(string language, string expected) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var loc = LocalizationManager.Instance;
        var oldLanguage = loc.CurrentLanguage;
        var muted = CoreSettings.Current.AvatarMuted;
        var property = typeof(AvApp).GetProperty("Achievements", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = property.GetValue(null);
        var tube = new AvatarTubeWindow(null);
        try
        {
            property.SetValue(null, null);
            loc.SetLanguage(language); CoreSettings.Current.AvatarMuted = true;
            var clock = new Clock(); tube.SpeechClock = clock;
            tube.HandleAvatarClick(); clock.Now += TimeSpan.FromMilliseconds(200); tube.HandleAvatarClick();
            Assert.Equal(expected, tube.FindControl<TextBlock>("TxtSpeech")!.Text);
        }
        finally
        {
            tube.Close(); property.SetValue(null, previous);
            CoreSettings.Current.AvatarMuted = muted; loc.SetLanguage(oldLanguage);
        }
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
