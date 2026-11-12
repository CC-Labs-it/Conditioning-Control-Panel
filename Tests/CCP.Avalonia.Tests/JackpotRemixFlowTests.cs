using System;
using System.IO;
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
using Xunit;

namespace CCP.Avalonia.Tests;

public class JackpotRemixFlowTests
{
    [Fact]
    public Task OwnedToggleAndCachedRemixReachAnimatedFlashAndCleanUp() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var s = CoreSettings.Current;
        var old = (s.JackpotRemixEnabled, s.MotionLevel, s.FlashClickable, s.CorruptionMode);
        var own = PrizeOwnership.IsGranted;
        var present = FlashOverlay.Present;
        var multiply = FlashOverlay.Multiply;
        var xp = CoreProgression.AddXPProvider;
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
            FlashOverlay.Present = (_, _, _) => true;
            var children = 0; FlashOverlay.Multiply = (_, _, _, _) => children++;
            var payments = 0; CoreProgression.AddXPProvider = (_, _) => payments++;
            FlashOverlay._remix = new JackpotRemixDirector(() => new[] { cached }, () => false, () => false,
                () => s.JackpotRemixEnabled, dir, () => true, () => MotionLevel.Full, () => false,
                (_, _, _) => Task.FromResult<string?>(null), () => ulong.MaxValue, odds: 1);
            var screen = shell.Screens.All[0];
            Assert.False(await FlashOverlay.TryShowRemix(s, new[] { screen }, null, false, FlashOverlay.Generation)); Assert.True(File.Exists(cached));
            Assert.True(await FlashOverlay.TryShowRemix(s, new[] { screen }, null, true, FlashOverlay.Generation));
            Assert.False(File.Exists(cached));
            var window = Assert.Single(FlashOverlay.Active).Window;
            Assert.True(window.IsRemix);
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
            (s.JackpotRemixEnabled, s.MotionLevel, s.FlashClickable, s.CorruptionMode) = old;
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public void ProfileSnapshotAndRevocationRefreshTheToggle() => AvaloniaTestDispatcher.Run(() =>
    {
        var saved = CoreAccount.UnifiedUserId;
        var seam = PrizeOwnership.IsGranted;
        var value = CoreSettings.Current.JackpotRemixEnabled;
        Window? shell = null;
        try
        {
            CoreAccount.UnifiedUserId = "remix-profile-test";
            PrizeOwnership.ResetHeld();
            PrizeOwnership.IsGranted = ConditioningControlPanel.Services.Prizes.PrizeGrants.IsGranted;
            var card = new FlashFeatureControl();
            shell = new Window { Content = card }; shell.Show();
            var row = card.FindControl<Grid>("RowJackpotRemix")!;
            var rows = (StackPanel)row.Parent!;
            Assert.True(rows.Children.IndexOf(card.FindControl<Grid>("RowShatter")!) < rows.Children.IndexOf(row));
            Assert.False(row.IsVisible);
            PrizeOwnership.Feed.Apply("remix-profile-test", "remix-profile-test", new PrizesBlock { Revision = 1, Grants = new() { PrizeOwnership.JackpotRemix } }, "profile");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(row.IsVisible);
            card.FindControl<CheckBox>("ChkJackpotRemix")!.IsChecked = true;
            Assert.True(CoreSettings.Current.JackpotRemixEnabled);
            PrizeOwnership.Feed.Apply("remix-profile-test", "remix-profile-test", new PrizesBlock { Revision = 2, Grants = new() }, "profile");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(row.IsVisible);
            Assert.True(CoreSettings.Current.JackpotRemixEnabled);
        }
        finally
        {
            shell?.Close(); PrizeOwnership.Feed.Clear();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            CoreAccount.UnifiedUserId = saved; PrizeOwnership.IsGranted = seam;
            CoreSettings.Current.JackpotRemixEnabled = value;
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
