using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Bark;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The tube's startup greeting (WPF AvatarTubeWindow.Speech.cs:2558), random bubble
/// (Speech.cs:1712-1834 + AvatarRandomBubble.cs).</summary>
public sealed class TubeGreetingBubbleTests
{
    private sealed class PrivateSettings : IDisposable
    {
        private readonly string _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-tube-" + Guid.NewGuid());
        private readonly Func<ConditioningControlPanel.Services.SettingsService?>? _previous = CoreSettings.ServiceProvider;
        private readonly ConditioningControlPanel.Services.SettingsService _service;
        public PrivateSettings()
        {
            System.IO.Directory.CreateDirectory(_dir);
            _service = new ConditioningControlPanel.Services.SettingsService(System.IO.Path.Combine(_dir, "settings.json")) { SaveDebounceDueTimeMilliseconds = System.Threading.Timeout.Infinite };
            CoreSettings.ServiceProvider = () => _service;
        }
        public void Dispose()
        {
            _service.SealForReset(); CoreSettings.ServiceProvider = _previous;
            System.IO.Directory.Delete(_dir, recursive: true);
        }
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static BarkEngine Engine(List<string> said, params string[] triggers)
    {
        var e = new BarkEngine(() => new BarkRuleSet(triggers.Select(t => new BarkRule
        {
            Id = "t_" + t, Trigger = t, VariantPool = new() { new BarkVariant(t + ":{" + (t == "AppOpened" ? "away_bucket" : "streak_days") + "}") },
        })));
        e.Speak = s => said.Add(s.Text);
        e.Start();
        return e;
    }

    /// <summary>The line she ends up showing: queued presets are let through their gap, the typewriter is drained.</summary>
    private static string Said(AvatarTubeWindow tube, List<Action> gaps)
    {
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            foreach (var fire in gaps.ToArray()) fire();
            gaps.Clear();
            while (tube.TypewriterFrame is { } frame) frame();
            if (tube.QueuedSpeechCount == 0 && gaps.Count == 0) break;
            tube.EndSpeechBubble();
        }
        return tube.FindControl<TextBlock>("TxtSpeech")!.Text ?? "";
    }

    private static AvatarTubeWindow OpenTube(List<Action> gaps)
    {
        var tube = new AvatarTubeWindow(null);
        tube.Show();
        tube.SpeechDelayScheduler = (_, fire) => { var live = true; gaps.Add(() => { if (live) fire(); }); return () => live = false; };
        return tube;
    }

    [Fact]
    public Task GreetingRaisesAppOpenedFallsBackToTheAbsenceLineAndCallsTheStreak() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        using var settings = new PrivateSettings();
        var s = CoreSettings.Current;
        var saved = (s.LastSeenUtc, s.CurrentStreak, s.LastAnnouncedStreakMilestone, s.AvatarMuted, BarkHead.Engine, AvatarTubeWindow.AbsenceGreetingShownThisLaunch);
        var phrase = new CustomCompanionPhrase { Id = "test-startup", Text = "startup line", Category = "StartupGreeting" };
        s.CustomCompanionPhrases.Add(phrase);
        s.AvatarMuted = false;
        var gaps = new List<Action>();
        var tube = OpenTube(gaps);
        try
        {
            Assert.True(tube.GreetingPending);   // opening arms the 2 s greeting
            tube.PanicSilence();
            Assert.False(tube.GreetingPending);  // a panic drops it

            // The bark answers: AppOpened carries the away bucket, the new 7-day milestone is announced once.
            var said = new List<string>();
            BarkHead.Engine = Engine(said, "AppOpened", "StreakMilestone");
            AvatarTubeWindow.AbsenceGreetingShownThisLaunch = false;
            s.LastSeenUtc = DateTime.UtcNow.AddHours(-10);
            (s.CurrentStreak, s.LastAnnouncedStreakMilestone) = (8, 0);
            tube.ShowGreeting();
            Assert.Equal(new[] { "AppOpened:back", "StreakMilestone:7" }, said);
            Assert.Contains("t_AppOpened", s.BarkVariantRotation.Keys);
            Assert.Equal(7, s.LastAnnouncedStreakMilestone);
            Assert.True(DateTime.UtcNow - s.LastSeenUtc!.Value < TimeSpan.FromMinutes(1));   // refreshed on open

            // No AppOpened rule: her own welcome-back line for a 10 h absence; the latch already holds 7.
            said.Clear();
            BarkHead.Engine = Engine(said, "StreakMilestone");
            AvatarTubeWindow.AbsenceGreetingShownThisLaunch = false;
            s.LastSeenUtc = DateTime.UtcNow.AddHours(-10);
            tube.ShowGreeting();
            Assert.Contains(Said(tube, gaps), new[] { "Welcome back! 💖", "Yay, you're here again! ✨", "Hi again, cutie~ 💕" }
                .Concat(new[] { "Welcome back, " + CoreAccount.DisplayName?.Trim() + "! 💖" }));
            Assert.Empty(said);

            // Later in the same launch (a re-created tube): a StartupGreeting phrase, no welcome-back.
            tube.ShowGreeting();
            Assert.Contains(Said(tube, gaps), ConditioningControlPanel.Services.Awareness.AwarenessReactionPhrases.Enabled("StartupGreeting"));
        }
        finally
        {
            tube.Close();
            s.CustomCompanionPhrases.Remove(phrase);
            (s.LastSeenUtc, s.CurrentStreak, s.LastAnnouncedStreakMilestone, s.AvatarMuted, BarkHead.Engine, AvatarTubeWindow.AbsenceGreetingShownThisLaunch) = saved;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RandomBubbleNeedsFocusPopsForXpAndPraiseAndPanicClearsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var saved = (s.RandomBubbleEnabled, s.AvatarMuted, AvatarTubeWindow.AppHasFocus, CoreProgression.AddXPProvider, AvatarTubeWindow.TestLive, CoreAudio.PlayOneShotProvider);
        var sounds = new List<string>();
        CoreAudio.PlayOneShotProvider = (path, _, tag, _, _) => sounds.Add(tag + ":" + System.IO.Path.GetFileName(path));
        var phrase = new CustomCompanionPhrase { Id = "test-bubble", Text = "catch it", Category = "RandomBubble" };
        s.CustomCompanionPhrases.Add(phrase);
        (s.RandomBubbleEnabled, s.AvatarMuted) = (true, false);
        var xp = new List<(double, string)>();
        CoreProgression.AddXPProvider = (a, src) => xp.Add((a, src));
        var gaps = new List<Action>();
        var tube = OpenTube(gaps);
        AvatarTubeWindow.TestLive = tube;
        try
        {
            Assert.True(tube.RandomBubbleTimerRunning);   // RandomBubbleEnabled: the 3-5 min loop runs while open

            AvatarTubeWindow.AppHasFocus = () => false;
            tube.RandomBubbleTick();
            Dispatcher.UIThread.RunJobs();
            Assert.False(tube.HasBubbleUp);                // another app in front: nothing

            AvatarTubeWindow.AppHasFocus = () => true;
            tube.RandomBubbleTick();
            Assert.Contains(Said(tube, gaps), ConditioningControlPanel.Services.Awareness.AwarenessReactionPhrases.Enabled("RandomBubble"));

            tube.SpawnRandomBubble();
            var bubble = Assert.Single(tube.RandomBubbles);
            Assert.True(bubble.Window.IsVisible);
            Assert.True(bubble.Window.Topmost);
            var y = bubble.Window.Position.Y;
            bubble.Step();
            Assert.True(bubble.Window.Position.Y < y);       // floats up

            var grid = Assert.IsType<Grid>(bubble.Window.Content);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            PointerPressedEventArgs Press(PointerUpdateKind kind, RawInputModifiers buttons)
            {
                var e = new PointerPressedEventArgs(grid, pointer, bubble.Window, default, 0, new PointerPointProperties(buttons, kind), KeyModifiers.None);
                grid.RaiseEvent(e); return e;
            }
            Assert.False(Press(PointerUpdateKind.RightButtonPressed, RawInputModifiers.RightMouseButton | RawInputModifiers.LeftMouseButton).Handled);
            Assert.False(Press(PointerUpdateKind.MiddleButtonPressed, RawInputModifiers.MiddleMouseButton).Handled);
            Assert.False(bubble.IsPopping);
            Assert.Empty(xp);
            Assert.True(Press(PointerUpdateKind.LeftButtonPressed, RawInputModifiers.LeftMouseButton).Handled);
            Assert.True(bubble.IsPopping);
            Assert.Equal(new[] { (5.0, "AvatarInteraction") }, xp);
            Assert.Contains(sounds, x => x.StartsWith("avatar-bubble-pop:Pop", StringComparison.Ordinal));
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("avatar_bubble_pop_praise"), Said(tube, gaps));
            bubble.Pop();
            Assert.Single(xp);                               // a popping bubble pays once
            for (int i = 0; i < 12 && bubble.IsAlive; i++) bubble.Step();
            Assert.False(bubble.IsAlive);
            Assert.Empty(tube.RandomBubbles);

            // Panic: every live bubble closes unpaid.
            tube.SpawnRandomBubble();
            var live = Assert.Single(tube.RandomBubbles);
            PanicSurfaces.All.Single(x => x.Id == "tube-speech").Stop(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(tube.RandomBubbles);
            Assert.False(live.Window.IsVisible);
            Assert.Single(xp);
            // The real panic handler arms the shared quiet before stopping surfaces.
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            tube.RandomBubbleTick();
            tube.SpawnRandomBubble();
            Assert.Empty(tube.RandomBubbles);
            Assert.Equal(0, tube.QueuedSpeechCount);
            Assert.False(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
        }
        finally
        {
            tube.Close();
            s.CustomCompanionPhrases.Remove(phrase);
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            (s.RandomBubbleEnabled, s.AvatarMuted, AvatarTubeWindow.AppHasFocus, CoreProgression.AddXPProvider, AvatarTubeWindow.TestLive, CoreAudio.PlayOneShotProvider) = saved;
        }
        Assert.False(tube.RandomBubbleTimerRunning);         // closing the tube stops the loop
        return Task.CompletedTask;
    });

    [Fact]
    public void CategoryAudioPrefersOverrideThenCustomAndSkipsMissingFiles()
    {
        var s = CoreSettings.Current;
        var phrase = new CustomCompanionPhrase { Id = Guid.NewGuid().ToString(), Category = "ReviewAudio", Text = "review audio" };
        var dir = CompanionPhrase.DefaultAudioFolder;
        System.IO.Directory.CreateDirectory(dir);
        var a = System.IO.Path.GetRandomFileName(); var b = System.IO.Path.GetRandomFileName();
        var pa = System.IO.Path.Combine(dir, a); var pb = System.IO.Path.Combine(dir, b);
        try
        {
            System.IO.File.WriteAllText(pa, "a"); System.IO.File.WriteAllText(pb, "b");
            phrase.AudioFileName = b; s.CustomCompanionPhrases.Add(phrase); s.PhraseAudioOverrides[phrase.Id] = a;
            Assert.Equal(pa, AvatarTubeWindow.CategoryAudio(phrase.Category, phrase.Text));
            System.IO.File.Delete(pa);
            Assert.Equal(pb, AvatarTubeWindow.CategoryAudio(phrase.Category, phrase.Text));
            System.IO.File.Delete(pb);
            Assert.Null(AvatarTubeWindow.CategoryAudio(phrase.Category, phrase.Text));
        }
        finally { s.PhraseAudioOverrides.Remove(phrase.Id); s.CustomCompanionPhrases.Remove(phrase); System.IO.File.Delete(pa); System.IO.File.Delete(pb); }
    }

}
