using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// PossessionDirector and PossessionRemember moved from the WPF head into Core. The director's
/// bookkeeping tests moved with it (from ConditioningControlPanel.Tests/PossessionDeckWpfTests.cs);
/// the rest pin that what WPF read from App.* now reaches the head through the seams.
/// </summary>
public class PossessionDirectorTests
{
    private static AppSettings Haunted(PossessionIntensity intensity = PossessionIntensity.Eerie, bool photosafe = false) => new()
    {
        LockdownPossessionEnabled = true,
        LockdownPossessionIntensity = (int)intensity,
        LockdownPhotosafe = photosafe,
    };

    [Fact]
    public void RestartReset_ReArmsTheLadder_ButSpendsTheRungItJustSet()
    {
        using var lockdown = new LockdownService();
        using var director = new PossessionDirector(lockdown, Array.Empty<IPossessionEffect>());

        director.ApplyRestartReset(PossessionRung.Settle);
        Assert.Equal(PossessionRung.Settle, director.CurrentRung);
        Assert.Contains(PossessionRung.Settle, director.AnnouncedRungs);
        Assert.Single(director.AnnouncedRungs);

        director.ApplyRestartReset(PossessionRung.Settle);
        Assert.Single(director.AnnouncedRungs);

        director.ApplyRestartReset(PossessionRung.Melt);
        Assert.Equal(PossessionRung.Melt, director.CurrentRung);
        Assert.Single(director.AnnouncedRungs);
        Assert.Contains(PossessionRung.Melt, director.AnnouncedRungs);
        Assert.DoesNotContain(PossessionRung.Settle, director.AnnouncedRungs);
    }

    [Fact]
    public void ShouldFinishReassembly_OnlyWhenNoNewLockdownStartedUnderneathIt()
    {
        Assert.True(PossessionDirector.ShouldFinishReassembly(3, 3, haunting: false));
        Assert.False(PossessionDirector.ShouldFinishReassembly(3, 4, haunting: false));
        Assert.False(PossessionDirector.ShouldFinishReassembly(3, 3, haunting: true));
        Assert.False(PossessionDirector.ShouldFinishReassembly(3, 4, haunting: true));
    }

    [Fact]
    public void Tripwire_BarksAndShakesThroughTheHead_UnlessPhotosafe()
    {
        foreach (var photosafe in new[] { false, true })
        {
            var barks = new List<(string, int, int)>();
            var shakes = new List<(double, int)>();
            var head = new PossessionDirectorHead
            {
                Settings = () => Haunted(photosafe: photosafe),
                BarkTripwire = (k, r, t) => barks.Add((k, r, t)),
                Shake = (i, ms) => shakes.Add((i, ms)),
            };
            using var lockdown = new LockdownService();
            using var director = new PossessionDirector(lockdown, Array.Empty<IPossessionEffect>(), head);

            director.OnLockdownActivated();
            Assert.True(director.IsHaunting);
            director.OnEscapeAttempted(new EscapeAttempt("alt_tab", 2, 5, DateTime.Now));

            Assert.Equal(new[] { ("alt_tab", 2, 5) }, barks);
            if (photosafe) Assert.Empty(shakes);
            else Assert.Equal(new[] { (0.4, 250) }, shakes);   // WPF: App.ScreenShake?.Shake(0.4, 250)
        }
    }

    [Fact]
    public void Activation_IsRefusedWhenPossessionIsOff()
    {
        var head = new PossessionDirectorHead { Settings = () => new AppSettings { LockdownPossessionEnabled = false } };
        using var lockdown = new LockdownService();
        using var director = new PossessionDirector(lockdown, Array.Empty<IPossessionEffect>(), head);
        director.OnLockdownActivated();
        Assert.False(director.IsHaunting);
    }

    private sealed class Host : IPossessionHost
    {
        public List<PossessionTarget> List { get; } = new();
        public IReadOnlyList<PossessionTarget> Targets => List;
        public bool IsUsable => true;
    }

    [Fact]
    public void TargetFor_ClimbsTheHeadsParentChainToTheRegisteredControl()
    {
        // A click on a card lands on the text inside it; the head's ParentOf walks up to the card.
        object card = new(), text = new(), stranger = new();
        var parents = new Dictionary<object, object> { [text] = card };
        var host = new Host();
        var target = new PossessionTarget { Visual = card, Role = PossessionRole.Card, Key = "card" };
        host.List.Add(target);
        var head = new PossessionDirectorHead { ParentOf = n => parents.TryGetValue(n, out var p) ? p : null };
        using var lockdown = new LockdownService();
        using var director = new PossessionDirector(lockdown, Array.Empty<IPossessionEffect>(), head);
        director.AttachHost(host);

        Assert.Same(target, director.TargetFor(text));
        Assert.Same(target, director.TargetFor(text, PossessionRole.Card));
        Assert.Null(director.TargetFor(text, PossessionRole.Button));
        Assert.Null(director.TargetFor(stranger));
    }

    // ---- The WPF head's wiring (WPF tests cannot run on Linux, so pin the mapping by source) ---------

    [Theory]
    [InlineData(@"OnUi = a => DispatcherHelper.RunOnUI(a)")]
    [InlineData(@"Settings = () => App.Settings?.Current,")]
    [InlineData(@"MainHost = () => App.MainWindowRef as IPossessionHost")]
    [InlineData(@"Scenes = PossessionSceneCatalog.CreateAll")]
    [InlineData(@"AttachReactive = events.Attach,")]
    [InlineData(@"DetachReactive = events.Detach,")]
    [InlineData(@"DetachAllReactive = events.DetachAll,")]
    [InlineData(@"IsVisible = el => ((FrameworkElement)el).IsVisible")]
    [InlineData(@"BarkRung = rung => App.Bark?.NotifyPossessionRung(rung)")]
    [InlineData(@"BarkTimerRestarted = (reason, restart) => App.Bark?.NotifyPossessionTimerRestarted(reason, restart)")]
    [InlineData(@"BarkEffect = (id, target) => App.Bark?.NotifyPossessionEffect(id, target)")]
    [InlineData(@"BarkTripwire = (kind, repeat, total) => App.Bark?.NotifyPossessionTripwire(kind, repeat, total)")]
    [InlineData(@"Shake = (intensity, ms) => App.ScreenShake?.Shake(intensity, ms)")]
    [InlineData(@"PossessionRemember.Settings = () => App.Settings?.Current;")]
    [InlineData(@"PossessionRemember.WindowReady = () => App.MainWindowRef is { IsLoaded: true, IsVisible: true };")]
    [InlineData(@"PossessionRemember.HasCompanion = () => App.AvatarWindow != null;")]
    [InlineData(@"PossessionRemember.ChargeDoor = ChargeDoor;")]
    [InlineData(@"PossessionRemember.SayRemembered = SayRemembered;")]
    [InlineData(@"PossessionRemember.Install(App.Lockdown);")]
    public void WpfHead_MapsEachHookToTheCallWpfMadeBefore(string line)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization"))) dir = dir.Parent;
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName,
            "ConditioningControlPanel", "Services", "Possession", "PossessionWpfHead.cs"));
        src = System.Text.RegularExpressions.Regex.Replace(src, @"/\*.*?\*/|//[^\n]*", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        static string Squash(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
        Assert.Contains(Squash(line), Squash(src));
    }

    // ---- PossessionRemember -----------------------------------------------------------------------

    [Theory]
    [InlineData(PossessionIntensity.FullDoki, true, true)]
    [InlineData(PossessionIntensity.Eerie, true, false)]     // only a Full Doki run is remembered
    [InlineData(PossessionIntensity.FullDoki, false, false)] // possession switched off at the end
    public void Remember_ArmsOnlyForAFullDokiRunThatWasEnabledAtDeactivate(PossessionIntensity intensity, bool enabled, bool armed)
    {
        var saved = PossessionRemember.Settings;
        try
        {
            var s = Haunted(intensity);
            s.LockdownPossessionEnabled = enabled;
            PossessionRemember.Settings = () => s;
            PossessionRemember.OnLockdownDeactivated();
            Assert.Equal(armed, s.LockdownPossessionRememberPending);
        }
        finally { PossessionRemember.Settings = saved; }
    }

    [Fact]
    public void Remember_SpendsTheFlagFirst_ThenChargesOnceTwentySecondsAfterTheWindowIsUp()
    {
        var saved = (PossessionRemember.Settings, PossessionRemember.StartTimer, PossessionRemember.WindowReady,
            PossessionRemember.HasCompanion, PossessionRemember.ChargeDoor, PossessionRemember.SayRemembered, PossessionRemember.UtcNow);
        try
        {
            PossessionRemember.ResetForTests();
            var s = Haunted(PossessionIntensity.FullDoki);
            s.LockdownPossessionRememberPending = true;
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            Action? tick = null;
            bool ready = false;
            int charges = 0, lines = 0;
            PossessionRemember.Settings = () => s;
            PossessionRemember.UtcNow = () => now;
            PossessionRemember.StartTimer = (_, t) => { tick = t; return null; };
            PossessionRemember.WindowReady = () => ready;
            PossessionRemember.HasCompanion = () => true;
            PossessionRemember.ChargeDoor = () => { charges++; return true; };
            PossessionRemember.SayRemembered = () => lines++;

            PossessionRemember.SchedulePendingCharge();
            Assert.False(s.LockdownPossessionRememberPending);   // a crash from here on still starts clean
            Assert.NotNull(tick);

            tick!(); now += TimeSpan.FromSeconds(30); tick();   // splash time does not count
            Assert.Equal(0, charges);
            ready = true;
            tick(); now += TimeSpan.FromSeconds(19); tick();
            Assert.Equal(0, charges);
            now += TimeSpan.FromSeconds(1); tick();
            Assert.Equal((1, 1), (charges, lines));
            now += TimeSpan.FromSeconds(30); tick();
            Assert.Equal((1, 1), (charges, lines));               // once per launch
        }
        finally
        {
            PossessionRemember.ResetForTests();
            (PossessionRemember.Settings, PossessionRemember.StartTimer, PossessionRemember.WindowReady,
                PossessionRemember.HasCompanion, PossessionRemember.ChargeDoor, PossessionRemember.SayRemembered, PossessionRemember.UtcNow) = saved;
        }
    }
}
