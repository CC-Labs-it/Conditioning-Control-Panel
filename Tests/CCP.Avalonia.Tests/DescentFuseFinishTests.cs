using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class DescentFuseFinishTests
{
    [Theory]
    [InlineData("#803A2B4C", -1, "#803A2B4C")]
    [InlineData("#803A2B4C", 0, "#803A2B4C")]
    [InlineData("#803A2B4C", 1, "#80352746")]
    [InlineData("#803A2B4C", 4, "#80251A33")]
    [InlineData("#803A2B4C", 99, "#80251A33")]
    public void DimmingMatchesWpfRounding_PreservesAlpha_AndClampsSteps(string input, int step, string expected) =>
        Assert.Equal(Color.Parse(expected), ConditioningControlPanel.Avalonia.Helpers.DescentFuseChrome.Dim(Color.Parse(input), step));

    [Fact]
    public void StartupSeedsTheRealSpeaker_AndRetriesUntilATubeExists()
    {
        var previous = AvApp.DescentCountdown;
        var settingsProvider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        try
        {
            settings.Current.DescentCeremonyAtUtc = null;
            AvApp.StartDescentCountdown();
            Assert.NotNull(AvApp.DescentCountdown!.Speaker);
            Assert.False(AvApp.DescentCountdown.Speaker!("not delivered"));
        }
        finally
        {
            AvApp.DescentCountdown?.Dispose();
            AvApp.DescentCountdown = previous;
            settings.SaveImmediate();
            CoreSettings.ServiceProvider = settingsProvider;
        }
    }

    [Fact]
    public void ReusedCorePolicy_SaysEachPhaseOnce_RetriesAndResetsSilenceForANewFuse()
    {
        var provider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var said = new System.Collections.Generic.List<string>();
        var ready = false;
        using var fuse = new DescentCountdownService
        {
            Speaker = line => { if (!ready) return false; said.Add(line); return true; },
        };
        try
        {
            settings.Current.DescentCeremonyAtUtc = DateTime.UtcNow.AddDays(6).ToString("o");
            fuse.Start();
            Assert.Empty(said);
            ready = true;
            foreach (var (hours, copy) in new[]
            {
                (144d, DescentFuseCopy.CompanionWhisper), (48d, DescentFuseCopy.CompanionClock),
                (23d, DescentFuseCopy.CompanionDimming), (0.5d, DescentFuseCopy.CompanionVigil),
                (0.1d, DescentFuseCopy.CompanionTerminal),
            })
            {
                settings.Current.DescentCeremonyAtUtc = DateTime.UtcNow.AddHours(hours).ToString("o");
                fuse.OnTimerTick();
                Assert.Equal(VocabTokens.Apply(copy), said.Last());
                var count = said.Count;
                fuse.OnTimerTick();
                Assert.Equal(count, said.Count);
            }
            Assert.Equal(5, said.Count);
            settings.Current.DescentCeremonyAtUtc = DateTime.UtcNow.AddDays(6).ToString("o");
            fuse.OnTimerTick();
            Assert.Equal(5, said.Count); // countdown-only silence after Terminal
            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddDays(5).ToString("o"));
            fuse.OnTimerTick(); // same-phase reschedule re-offers on the next tick
            Assert.Equal(6, said.Count); // a reschedule is a new fuse, as WPF
        }
        finally { settings.SaveImmediate(); CoreSettings.ServiceProvider = provider; }
    }

    [Fact]
    public Task ChromeCatchesUp_WalksOnTicks_RestoresAndKeepsTheActiveModsAccent() => Run(shell =>
    {
        var fuse = AvApp.DescentCountdown!;
        var s = CoreSettings.Current;
        Color Resource(string key) => (Color)Application.Current!.Resources[key]!;
        object Brush(string key) => Application.Current!.Resources[key + "Brush"]!;
        var original = Resource("DarkerBg");
        var accent = Resource("PinkColor");
        var tinted = Resource("AccentTintedBg");

        // A phase announcement repaints immediately; ticks inside a step must keep the brush.
        fuse.ApplyCeremonyAt(DateTime.UtcNow.AddHours(23).ToString("o"));
        Assert.Equal(Color.Parse("#18182B"), Resource("DarkerBg")); // #1A1A2E, 11% toward #0A0514
        Assert.Equal(Color.Parse("#22213D"), Resource("PanelBg"));
        Assert.Equal(Color.Parse("#1C1B36"), Resource("SurfaceBg"));
        Assert.Equal(Color.Parse("#43425A"), Resource("PanelAccent"));
        Assert.Equal(Color.Parse("#59586D"), Resource("PanelAccentHover"));
        Assert.Equal(Color.Parse("#141424"), Resource("PreviewBg"));
        Assert.Equal(Color.Parse("#B022213D"), Resource("PanelBgTransparent"));
        var brush = Brush("DarkerBg");
        fuse.OnTimerTick();
        Assert.Same(brush, Brush("DarkerBg"));
        Assert.Equal(accent, Resource("PinkColor"));
        Assert.Equal(tinted, Resource("AccentTintedBg"));

        // Same phase, different six-hour step: only a tick sees this move.
        s.DescentCeremonyAtUtc = DateTime.UtcNow.AddHours(17).ToString("o");
        fuse.OnTimerTick();
        Assert.Equal(Color.Parse("#161528"), Resource("DarkerBg"));
        Assert.NotSame(brush, Brush("DarkerBg"));
        s.DescentCeremonyAtUtc = DateTime.UtcNow.AddHours(11).ToString("o");
        fuse.OnTimerTick();
        Assert.Equal(Color.Parse("#151325"), Resource("DarkerBg"));
        s.DescentCeremonyAtUtc = DateTime.UtcNow.AddHours(5).ToString("o");
        fuse.OnTimerTick();
        Assert.Equal(Color.Parse("#131123"), Resource("DarkerBg"));

        fuse.ApplyCeremonyAt(DateTime.UtcNow.AddSeconds(-1).ToString("o"));
        Assert.Equal(Color.Parse("#131123"), Resource("DarkerBg")); // hold through zero
        fuse.ApplyCeremonyAt(DateTime.UtcNow.AddHours(-13).ToString("o"));
        Assert.Equal(original, Resource("DarkerBg")); // the existing Core twelve-hour release
        fuse.ApplyCeremonyAt(DateTime.UtcNow.AddHours(5).ToString("o"));

        // A real mod change derives neutrals anew, never from the already dimmed palette.
        shell.SwitchActiveModFromLauncher(BuiltInMods.DronificationId);
        var modBg = Color.Parse(AvApp.Mods!.GetBackgroundColorHex());
        var modAccent = Color.Parse(AvApp.Mods.GetAccentColorHex());
        Assert.Equal(modAccent, Resource("PinkColor"));
        var switched = Resource("DarkerBg");
        Assert.NotEqual(modBg, switched);
        shell.ApplyActiveModChange();
        Assert.Equal(switched, Resource("DarkerBg"));
        fuse.ApplyCeremonyAt(null);
        Assert.Equal(modBg, Resource("DarkerBg"));
        Assert.Equal(modAccent, Resource("PinkColor"));

        // Already-lit launches catch up, and a migration ack restores even after zero disarmed ticks.
        fuse.ApplyCeremonyAt(DateTime.UtcNow.AddMinutes(5).ToString("o"));
        var caughtUp = new MainShellWindow();
        try { Assert.Equal(switched, Resource("DarkerBg")); }
        finally { caughtUp.Close(); }
        s.DescentMigrationCompleted = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(modBg, Resource("DarkerBg"));
        Assert.NotEqual(original, Resource("DarkerBg"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task RealPhaseLinesRetry_SpeakOnce_StopAtTerminal_AndUseTheExistingPanicSurface() => Run(shell =>
    {
        var app = Application.Current!;
        var lifetime = app.ApplicationLifetime;
        using var desktop = new TubeLifetime(() => shell.Tube);
        app.ApplicationLifetime = desktop;
        var oneShot = CoreAudio.PlayOneShotProvider;
        var stoppable = CoreAudio.PlayStoppableProvider;
        var sounds = 0;
        CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => sounds++;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { sounds++; return () => { }; };
        try
        {
            var fuse = AvApp.DescentCountdown!;
            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddDays(6).ToString("o")); // Whisper: no tube yet
            fuse.OnTimerTick();
            CoreSettings.Current.AvatarEnabled = true;
            CoreSettings.Current.AvatarMuted = false;
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            Assert.NotNull(tube);
            tube.ChatHistory.Clear();
            fuse.OnTimerTick();
            Assert.Equal(DescentFuseCopy.CompanionWhisper, tube.ChatHistory.Single().Text);
            fuse.OnTimerTick();
            Assert.Single(tube.ChatHistory);

            foreach (var (hours, copy) in new[]
            {
                (48d, DescentFuseCopy.CompanionClock), (23d, DescentFuseCopy.CompanionDimming),
                (0.5d, DescentFuseCopy.CompanionVigil), (0.1d, DescentFuseCopy.CompanionTerminal),
            })
            {
                CoreSettings.Current.DescentCeremonyAtUtc = DateTime.UtcNow.AddHours(hours).ToString("o");
                fuse.OnTimerTick();
                Assert.Equal(VocabTokens.Apply(copy), tube.ChatHistory.Last().Text);
                Assert.False(tube.FindControl<Border>("AiBadge")!.IsVisible);
            }
            Assert.Equal(5, tube.ChatHistory.Count);
            Assert.Equal(0, sounds); // playSound:false, no invented voice clip
            CoreSettings.Current.DescentCeremonyAtUtc = DateTime.UtcNow.AddDays(6).ToString("o");
            fuse.OnTimerTick();
            Assert.Equal(5, tube.ChatHistory.Count); // the countdown stays silent after Terminal
            Assert.True(tube.HasBubbleUp);
            PanicSurfaces.All.Single(x => x.Id == "tube-speech").Stop(shell);
            Assert.False(tube.HasBubbleUp);
            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddDays(5).ToString("o")); // an owner reschedule resets silence
            fuse.OnTimerTick();
            Assert.Equal(6, tube.ChatHistory.Count);
            Assert.Equal(DescentFuseCopy.CompanionWhisper, tube.ChatHistory.Last().Text);
            tube.GigglePriority("user chat still works", playSound: false);
            Assert.True(tube.HasBubbleUp); // silence is scoped to the countdown, never a global mute
        }
        finally
        {
            shell.Tube?.CloseSafe();
            CoreAudio.PlayOneShotProvider = oneShot;
            CoreAudio.PlayStoppableProvider = stoppable;
            app.ApplicationLifetime = lifetime;
        }
        return Task.CompletedTask;
    });

    // Supply the actual shown tube to Live without starting App.StartDesktop or a second dispatcher.
    private sealed class TubeLifetime(Func<Window?> tube) : ClassicDesktopStyleApplicationLifetime, IClassicDesktopStyleApplicationLifetime
    {
        System.Collections.Generic.IReadOnlyList<Window> IClassicDesktopStyleApplicationLifetime.Windows =>
            tube() is { IsVisible: true } window ? new[] { window } : Array.Empty<Window>();
    }

    private static Task Run(Func<MainShellWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var provider = CoreSettings.ServiceProvider;
        var previous = AvApp.DescentCountdown;
        using var mods = new CoreModsSnapshot();
        var resources = Application.Current!.Resources.ToDictionary(x => x.Key, x => x.Value);
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        MainShellWindow? shell = null;
        try
        {
            settings.Current.DescentCeremonyAtUtc = null;
            settings.Current.DescentMigrationCompleted = false;
            settings.Current.AvatarEnabled = false;
            settings.Current.OfflineMode = true;
            settings.Current.Welcomed = true;
            settings.Current.HasAcceptedAgeVerification = true;
            settings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
            settings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
            AvApp.StartMods();
            AvApp.StartDescentCountdown();
            shell = new MainShellWindow();
            await body(shell);
        }
        finally
        {
            shell?.RequestExit();
            AvApp.DescentCountdown?.Dispose();
            AvApp.DescentCountdown = previous;
            settings.SaveImmediate();
            settings.SealForReset();
            CoreSettings.ServiceProvider = provider;
            AvApp.ResetReleaseContent();
            foreach (var key in Application.Current.Resources.Keys.ToArray()) Application.Current.Resources.Remove(key);
            foreach (var entry in resources) Application.Current.Resources[entry.Key] = entry.Value;
        }
    });
}
