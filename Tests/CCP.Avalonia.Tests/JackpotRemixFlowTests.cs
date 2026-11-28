using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Remix;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class JackpotRemixFlowTests
{
    [Fact]
    public Task OwnedToggleAndCachedRemixReachAnimatedFlashAndCleanUp() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var cache = new FlashLuminanceCacheIsolation();
        var s = CoreSettings.Current;
        var old = (s.JackpotRemixEnabled, s.MotionLevel, s.FlashClickable, s.CorruptionMode, s.HydraLimit, s.FlashStayUntilPopped, s.GlobalTargetMonitor);
        var own = PrizeOwnership.IsGranted;
        var present = FlashOverlay.Present;
        var multiply = FlashOverlay.Multiply;
        var xp = CoreProgression.AddXPProvider;
        var look = (s.FlashGlowEnabled, s.FlashMotionStyle, s.LighterEffects, s.PerformanceMode, s.AutoPerformanceMode);
        var hadLucky = s.UnlockedSkills.Contains("lucky_bimbo");
        var (rng, proc, chime) = (FlashOverlay.Rng, FlashOverlay.LuckyProc, FlashOverlay.LuckyChime);
        var dir = Directory.CreateTempSubdirectory("ccp-remix-flow-").FullName;
        var fixture = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "Assets/web/dtrh/assets/bubbles/effects/spirals/sp6.gif"));
        var cached = Path.Combine(dir, "remix-fixture.gif");
        File.Copy(fixture, cached);
        var card = new FlashFeatureControl();
        var shell = new Window { Content = card };
        try
        {
            PrizeOwnership.IsGranted = _ => true;
            s.JackpotRemixEnabled = false;
            shell.Show();
            // Refresh after the ownership grant, as the purchase row does.
            shell.Close();
            card = new FlashFeatureControl(); shell = new Window { Content = card }; shell.Show();
            Assert.True(card.FindControl<Grid>("RowJackpotRemix")!.IsVisible);
            card.FindControl<CheckBox>("ChkJackpotRemix")!.IsChecked = true;
            Assert.True(s.JackpotRemixEnabled);
            s.MotionLevel = MotionLevel.Full; s.FlashClickable = true; s.CorruptionMode = true;
            s.HydraLimit = 2; s.FlashStayUntilPopped = false; s.GlobalTargetMonitor = ConditioningControlPanel.Services.UI.MonitorTarget.All;
            FlashOverlay.Present = (_, _, _) => { s.HydraLimit = 1; return true; };
            var children = 0; FlashOverlay.Multiply = (_, _, _, _) => children++;
            var payments = 0; CoreProgression.AddXPProvider = (_, _) => payments++;
            // WPF FlashService.cs:1868, 1903-1909, 2069: a remix keeps the lucky glow and the motion setting.
            (s.FlashGlowEnabled, s.FlashMotionStyle, s.LighterEffects, s.PerformanceMode, s.AutoPerformanceMode)
                = (true, FlashMotionStyle.DriftBounce, LighterEffectsMode.Off, false, false);
            if (!hadLucky) s.UnlockedSkills.Add("lucky_bimbo");
            FlashOverlay.Rng = new LuckyRandom();
            var procs = 0; FlashOverlay.LuckyProc = (_, _) => procs++; FlashOverlay.LuckyChime = _ => { };
            FlashOverlay._remix = new JackpotRemixDirector(() => new[] { cached }, () => false, () => false,
                () => s.JackpotRemixEnabled, dir, () => true, () => MotionLevel.Full, () => false,
                (_, _, _) => Task.FromResult<string?>(null), () => ulong.MaxValue, odds: 1);
            var screen = shell.Screens.All[0];
            Assert.False(await FlashOverlay.TryShowRemix(s, new[] { screen }, null, false, FlashOverlay.Generation)); Assert.True(File.Exists(cached));
            Assert.True(await FlashOverlay.TryShowRemix(s, new[] { screen, screen }, null, true, FlashOverlay.Generation));
            Assert.False(File.Exists(cached));
            var window = Assert.Single(FlashOverlay.Active).Window;
            Assert.True(window.IsRemix);
            Assert.Equal(1, payments);
            Assert.Equal(1, procs);
            Assert.True(window.GlowPadPx > 0);
            Assert.Equal(FlashMotionStyle.DriftBounce, window.MotionStyle);
            File.Copy(fixture, cached);
            FlashOverlay._remix!.Dispose();
            FlashOverlay._remix = new JackpotRemixDirector(() => new[] { cached }, () => false, () => false,
                () => s.JackpotRemixEnabled, dir, () => true, () => MotionLevel.Full, () => false,
                (_, _, _) => Task.FromResult<string?>(null), () => ulong.MaxValue, odds: 1);
            Assert.True(await FlashOverlay.TryShowRemix(s, new[] { screen, screen }, null, true, FlashOverlay.Generation));
            Assert.Single(FlashOverlay.Active); // a full cap consumes the roll without an ordinary fallback
            Assert.Equal(1, payments);
            var first = window.Picture;
            for (var i = 0; i < 30 && ReferenceEquals(first, window.Picture); i++) await Task.Delay(100);
            Assert.NotSame(first, window.Picture);
            FlashOverlay.Pop(window);
            Assert.Equal(0, children);
            FlashOverlay.CloseAll(false);
            Assert.Empty(FlashOverlay.Active);
        }
        finally
        {
            FlashOverlay.CloseAll(false); shell.Close();
            PrizeOwnership.IsGranted = own; FlashOverlay.Present = present; FlashOverlay.Multiply = multiply;
            CoreProgression.AddXPProvider = xp;
            (s.FlashGlowEnabled, s.FlashMotionStyle, s.LighterEffects, s.PerformanceMode, s.AutoPerformanceMode) = look;
            if (!hadLucky) s.UnlockedSkills.Remove("lucky_bimbo");
            (FlashOverlay.Rng, FlashOverlay.LuckyProc, FlashOverlay.LuckyChime) = (rng, proc, chime);
            (s.JackpotRemixEnabled, s.MotionLevel, s.FlashClickable, s.CorruptionMode, s.HydraLimit, s.FlashStayUntilPopped, s.GlobalTargetMonitor) = old;
            Directory.Delete(dir, true);
        }
    });

    [Theory]
    [InlineData(false, true, false, 2)]
    [InlineData(false, true, true, 1)]
    [InlineData(true, true, false, 0)]
    [InlineData(false, false, false, 0)]
    public Task RemixLuminanceFollowsSuccessfulSpawns(bool suppress, bool enabled, bool refuseSecond, int expectedCount) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            using var cache = new FlashLuminanceCacheIsolation();
            var s = CoreSettings.Current;
            var old = (s.GlobalTargetMonitor, s.FlashClickable, s.FlashStayUntilPopped, s.HydraLimit);
            var saved = (CoreHaptics.Service, FlashOverlay.SetLuminance, FlashOverlay.Present, CoreProgression.AddXPProvider);
            var warning = typeof(FlashOverlay).GetField("_warnedUnavailable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var warned = (bool)warning.GetValue(null)!;
            var settings = new HapticSettings { LuminanceSyncEnabled = true, LuminanceSyncIntensity = 0.6 };
            using var haptics = new HapticService(settings);
            var calls = new List<(double Value, int Ms)>();
            var dir = Directory.CreateTempSubdirectory("ccp-remix-cues-").FullName;
            var fixture = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "Assets/web/dtrh/assets/bubbles/effects/spirals/sp6.gif"));
            var path = Path.Combine(dir, "remix-fixture.gif");
            File.Copy(fixture, path);
            var host = new Window();
            try
            {
                CoreHaptics.Service = haptics;
                FlashOverlay.SetLuminance = (value, ms) => calls.Add((value, ms));
                var (frames, _) = SpiralOverlay.Decode(path, maxFrames: 60);
                Assert.True(frames.Count > 1);
                FlashOverlay.ApplyLuminance(frames[0], "", TimeSpan.FromMilliseconds(3300));
                var expected = Assert.Single(calls).Value;
                foreach (var frame in frames) frame.Dispose();
                calls.Clear();
                settings.LuminanceSyncEnabled = enabled;
                var attempts = 0;
                FlashOverlay.Present = (_, _, _) => ++attempts != 2 || !refuseSecond;
                CoreProgression.AddXPProvider = (_, _) => { };
                (s.GlobalTargetMonitor, s.FlashClickable, s.FlashStayUntilPopped, s.HydraLimit) =
                    (ConditioningControlPanel.Services.UI.MonitorTarget.All, false, false, 20);
                FlashOverlay._remix = new JackpotRemixDirector(() => new[] { path }, () => false, () => false,
                    () => true, dir, () => true, () => MotionLevel.Full, () => false,
                    (_, _, _) => Task.FromResult<string?>(null), () => ulong.MaxValue, odds: 1);
                var screen = host.Screens.All[0];
                Assert.True(await FlashOverlay.TryShowRemix(s, new[] { screen, screen }, 2300, true, FlashOverlay.Generation, suppress));
                Assert.Equal(2, attempts);
                Assert.Equal(refuseSecond ? 1 : 2, FlashOverlay.Active.Count);
                Assert.Equal(expectedCount, calls.Count);
                Assert.All(calls, call => { Assert.Equal(expected, call.Value, 6); Assert.Equal(3300, call.Ms); });
            }
            finally
            {
                FlashOverlay.CloseAll(false);
                warning.SetValue(null, warned);
                host.Close();
                (CoreHaptics.Service, FlashOverlay.SetLuminance, FlashOverlay.Present, CoreProgression.AddXPProvider) = saved;
                (s.GlobalTargetMonitor, s.FlashClickable, s.FlashStayUntilPopped, s.HydraLimit) = old;
                Directory.Delete(dir, true);
            }
        });

    [Fact]
    public async Task RefusedRemixSpawnRestoresUnavailableWarningState()
    {
        var field = typeof(FlashOverlay).GetField("_warnedUnavailable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var previous = (bool)field.GetValue(null)!;
        try
        {
            field.SetValue(null, false);
            await RemixLuminanceFollowsSuccessfulSpawns(false, true, true, 1);
            Assert.False((bool)field.GetValue(null)!);
        }
        finally { field.SetValue(null, previous); }
    }

    private sealed class LuckyRandom : Random { public override double NextDouble() => 0; }

    [Fact]
    public void ProfileSnapshotAndRevocationRefreshTheToggle() => AvaloniaTestDispatcher.Run(() =>
    {
        var seam = PrizeOwnership.IsGranted;
        const string account = "remix-profile-test";
        var ownership = new ConditioningControlPanel.Services.Prizes.OwnershipService(() => account, null, action => action());
        var feed = new ConditioningControlPanel.Services.Prizes.PrizesFeed(() => ownership, () => account, action => action());
        ownership.OwnershipChanged += (_, _) => ConditioningControlPanel.Services.Prizes.PrizeGrants.RaiseGrantsChanged();
        var value = CoreSettings.Current.JackpotRemixEnabled;
        Window? shell = null;
        try
        {
            PrizeOwnership.IsGranted = ownership.IsGranted;
            var card = new FlashFeatureControl();
            shell = new Window { Content = card }; shell.Show();
            var row = card.FindControl<Grid>("RowJackpotRemix")!;
            var rows = (StackPanel)row.Parent!;
            Assert.True(rows.Children.IndexOf(card.FindControl<Grid>("RowShatter")!) < rows.Children.IndexOf(row));
            Assert.False(row.IsVisible);
            feed.Apply(account, account, new PrizesBlock { Revision = 1, Grants = new() { PrizeOwnership.JackpotRemix } }, "profile");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(row.IsVisible);
            card.FindControl<CheckBox>("ChkJackpotRemix")!.IsChecked = true;
            Assert.True(CoreSettings.Current.JackpotRemixEnabled);
            feed.Apply(account, account, new PrizesBlock { Revision = 2, Grants = new() }, "profile");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(row.IsVisible);
            Assert.True(CoreSettings.Current.JackpotRemixEnabled);
        }
        finally
        {
            shell?.Close();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            PrizeOwnership.IsGranted = seam;
            CoreSettings.Current.JackpotRemixEnabled = value;
        }
    });

    [Fact]
    public void ProfileTestLeavesSharedOwnershipAndFeedIntact() => AvaloniaTestDispatcher.Run(() =>
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var service = (ConditioningControlPanel.Services.Prizes.OwnershipService)typeof(PrizeOwnership)
            .GetField("Service", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        var feed = PrizeOwnership.Feed;
        var fields = new[] { "_grants", "_heldAccountId", "_revision" }.Select(name => service.GetType().GetField(name, flags)!).ToArray();
        var values = fields.Select(field => field.GetValue(service)).ToArray();
        var feedAccount = feed.GetType().GetField("_accountId", flags)!;
        var previousFeedAccount = feedAccount.GetValue(feed);
        var gate = service.GetType().GetField("_gate", flags)!.GetValue(service)!;
        var feedGate = feed.GetType().GetField("_gate", flags)!.GetValue(feed)!;
        var account = CoreAccount.UnifiedUserId;
        try
        {
            CoreAccount.UnifiedUserId = "sentinel";
            feed.NoteAccount("sentinel");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            service.ApplySnapshot("sentinel", 71, new[] { PrizeOwnership.FlashPendulum });
            ProfileSnapshotAndRevocationRefreshTheToggle();
            Assert.Equal("sentinel", CoreAccount.UnifiedUserId);
            Assert.Equal(71, service.Revision);
            Assert.True(service.IsGranted(PrizeOwnership.FlashPendulum));
            Assert.Equal("sentinel", feedAccount.GetValue(feed));
        }
        finally
        {
            lock (gate) for (var j = 0; j < fields.Length; j++) fields[j].SetValue(service, values[j]);
            lock (feedGate) feedAccount.SetValue(feed, previousFeedAccount);
            CoreAccount.UnifiedUserId = account;
        }
    });

    [Fact]
    public void StartingAndStoppingScheduleStartsAndDisposesDirector() => AvaloniaTestDispatcher.Run(() =>
    {
        var wasRunning = CoreFlash.IsRunning;
        var (start, stop, show, ambient, busy) = (CoreFlash.StartProvider, CoreFlash.StopProvider, CoreFlash.ShowProvider, CoreFlash.AmbientShowProvider, CoreFlash.IsBusyProvider);
        try
        {
            CoreFlash.Stop();
            FlashOverlay.SeedCore(() => null, () => { });
            CoreFlash.Start();
            Assert.NotNull(FlashOverlay._remix);
            CoreFlash.Stop();
            Assert.Null(FlashOverlay._remix);
        }
        finally
        {
            CoreFlash.Stop();
            (CoreFlash.StartProvider, CoreFlash.StopProvider, CoreFlash.ShowProvider, CoreFlash.AmbientShowProvider, CoreFlash.IsBusyProvider) = (start, stop, show, ambient, busy);
            if (wasRunning) CoreFlash.Start();
        }
    });

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
