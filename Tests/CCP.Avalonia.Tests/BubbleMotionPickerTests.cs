using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF BubblePopFeatureControl Bubbles v2 box (xaml:195-224, RebuildMotionPicker): the
/// motion picker lists only owned styles, writes BubbleMotionStyle, and is a SessionLock.Owned
/// dose control the running-session sweep greys (row shell-session-feature-lock).</summary>
public sealed class BubbleMotionPickerTests
{
    [Fact]
    public Task PickerListsOwnedStylesWritesTheSettingAndLocksDuringASession() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var seam = PrizeOwnership.IsGranted;
        var saved = CoreSettings.Current.BubbleMotionStyle;
        ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow? shell = null;
        Window? host = null;
        try
        {
            // Nothing owned: WPF collapses the whole box.
            PrizeOwnership.IsGranted = _ => false;
            var bare = new BubblePopFeatureControl();
            Assert.False(bare.FindControl<Border>("V2Box")!.IsVisible);

            CoreSettings.Current.BubbleMotionStyle = BubbleMotionStyle.FloatUp;
            PrizeOwnership.IsGranted = id => id == PrizeOwnership.BubbleSpiralIn;
            shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            shell.Show();
            var bubbles = shell.StudioRack!.HostedFeaturePanels.OfType<BubblePopFeatureControl>().Single();
            Assert.True(bubbles.FindControl<Border>("V2Box")!.IsVisible);
            var cmb = bubbles.FindControl<ComboBox>("CmbMotion")!;
            Assert.Equal(new[] { BubbleMotionStyle.FloatUp, BubbleMotionStyle.SpiralIn, BubbleMotionStyle.Mix },
                cmb.Items.OfType<ComboBoxItem>().Select(i => (BubbleMotionStyle)i.Tag!).ToArray());

            cmb.SelectedIndex = 1;
            Assert.Equal(BubbleMotionStyle.SpiralIn, CoreSettings.Current.BubbleMotionStyle);

            ToolTip.SetTip(cmb, "original tip");

            // A setting changed elsewhere moves the picker (WPF watch list :201), and a settings
            // tick only reselects: the items are the same instances (WPF SelectMotion).
            // (Hosted in its own window: the settings hook lives while the card is attached.)
            var live = new BubblePopFeatureControl();
            host = new Window { Content = live };
            host.Show();
            var liveCmb = live.FindControl<ComboBox>("CmbMotion")!;
            var firstItem = liveCmb.Items[0];
            CoreSettings.Current.BubbleMotionStyle = BubbleMotionStyle.Mix;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BubbleMotionStyle.Mix, (BubbleMotionStyle)((ComboBoxItem)liveCmb.SelectedItem!).Tag!);
            Assert.Same(firstItem, liveCmb.Items[0]);

            ConditioningControlPanel.CoreSession.IsSessionRunningProvider = () => true;
            shell.RefreshSessionFeatureLock();
            Assert.False(cmb.IsEnabled);
            Assert.NotEqual("original tip", ToolTip.GetTip(cmb));
            ConditioningControlPanel.CoreSession.IsSessionRunningProvider = () => false;
            shell.RefreshSessionFeatureLock();
            Assert.True(cmb.IsEnabled);
            Assert.Equal("original tip", ToolTip.GetTip(cmb));
        }
        finally
        {
            ConditioningControlPanel.CoreSession.IsSessionRunningProvider = null;
            PrizeOwnership.IsGranted = seam;
            CoreSettings.Current.BubbleMotionStyle = saved;
            shell?.Close();
            host?.Close();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PurchaseSnapshotRefreshesTheLockedPickerAndRevocationFallsBack() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.UnifiedId, s.OfflineMode, s.BubbleMotionStyle, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider,
                     PrizeOwnership.IsGranted, CoreSession.IsSessionRunningProvider);
        ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow? shell = null;
        try
        {
            s.UnifiedId = "test-account"; s.OfflineMode = false;
            CoreSecrets.RetrieveProvider = name => name == CoreSecrets.AuthToken ? "test-token" : null;
            s.BubbleMotionStyle = BubbleMotionStyle.Rain;
            CoreAccount.UnifiedUserId = "test-account";
            PrizeOwnership.ResetHeld();
            PrizeOwnership.IsGranted = PrizeOwnership.Held;
            CoreSession.IsSessionRunningProvider = () => true;
            shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            shell.Show();
            shell.ShowTab("studio");
            shell.StudioRack!.FocusRackEntry("bubbles");
            Dispatcher.UIThread.RunJobs();
            shell.RefreshSessionFeatureLock();
            var panel = shell.StudioRack!.HostedFeaturePanels.OfType<BubblePopFeatureControl>().Single();
            var box = panel.FindControl<Border>("V2Box")!;
            var picker = panel.FindControl<ComboBox>("CmbMotion")!;
            Assert.False(box.IsVisible);
            Assert.False(picker.IsEnabled);

            PrizeOwnership.ApplySnapshot("test-account", 2, new[] { PrizeOwnership.BubbleRain });
            Assert.True(PrizeOwnership.Held(PrizeOwnership.BubbleRain));
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsVisible);
            Assert.False(picker.IsEnabled);
            Assert.Equal(BubbleMotionStyle.Rain, ((ComboBoxItem)picker.SelectedItem!).Tag);
            Assert.Equal(BubbleMotionStyle.Rain, s.BubbleMotionStyle);

            PrizeOwnership.ApplySnapshot("test-account", 1, System.Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsVisible); // stale snapshots must not revoke
            PrizeOwnership.ApplySnapshot("test-account", 3, System.Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            Assert.False(box.IsVisible);
            Assert.Equal(BubbleMotionStyle.FloatUp, ((ComboBoxItem)picker.SelectedItem!).Tag);
            Assert.Equal(BubbleMotionStyle.Rain, s.BubbleMotionStyle); // do not overwrite a synced choice
            PrizeOwnership.ApplySnapshot("test-account", 4, new[] { PrizeOwnership.BubbleRain });
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsVisible);
            s.UnifiedId = "other-account";
            CoreAccount.UnifiedUserId = "other-account";
            Dispatcher.UIThread.RunJobs();
            Assert.False(box.IsVisible);
        }
        finally
        {
            shell?.Close();
            PrizeOwnership.ResetHeld();
            (s.UnifiedId, s.OfflineMode, s.BubbleMotionStyle, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider,
             PrizeOwnership.IsGranted, CoreSession.IsSessionRunningProvider) = saved;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });


    [Fact]
    public Task OverlaySpawnsUsingTheCurrentChoiceGrantsAndMotionLevel() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var overlay = typeof(ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlay);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var windows = (System.Collections.IList)overlay.GetField("Windows", flags)!.GetValue(null)!;
        var targets = (System.Collections.IList)overlay.GetField("Targets", flags)!.GetValue(null)!;
        var running = overlay.GetField("_running", flags)!;
        var pending = overlay.GetField("_pending", flags)!;
        var field = ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlay.Field;
        var s = CoreSettings.Current;
        var saved = (s.BubbleMotionStyle, s.MotionLevel, s.GentleVisuals, s.BubbleSpeedBoost, PrizeOwnership.IsGranted,
                     UiMotionPreference.OsProbe,
                     Running: (bool)running.GetValue(null)!, Pending: (bool)pending.GetValue(null)!);
        var screen = new Window().Screens.All[0];
        var window = new ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlayWindow(0, screen.Bounds, 1);
        try
        {
            Assert.Empty(field.Bubbles); Assert.Empty(windows); Assert.Empty(targets);
            windows.Add(window); targets.Add(screen); running.SetValue(null, true); pending.SetValue(null, true);
            PrizeOwnership.IsGranted = id => id == PrizeOwnership.BubbleRain;
            s.BubbleMotionStyle = BubbleMotionStyle.Rain; s.MotionLevel = MotionLevel.Full; s.GentleVisuals = false; s.BubbleSpeedBoost = 0;
            UiMotionPreference.OsProbe = () => true; UiMotionPreference.CacheStamp = long.MinValue;
            ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlay.Spawn();
            Assert.Equal(BubbleMotionStyle.Rain, Assert.Single(field.Bubbles).Motion);
            field.Bubbles.Clear();
            s.GentleVisuals = true;
            ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlay.Spawn();
            var gentle = Assert.Single(field.Bubbles);
            var startY = gentle.Y; gentle.Step();
            Assert.InRange(gentle.Y - startY, 0.5, 1.0);
            field.Bubbles.Clear(); s.GentleVisuals = false;
            UiMotionPreference.OsProbe = () => false; UiMotionPreference.CacheStamp = long.MinValue;
            ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlay.Spawn();
            var osReduced = Assert.Single(field.Bubbles);
            startY = osReduced.Y; osReduced.Step();
            Assert.InRange(osReduced.Y - startY, 0.5, 1.0);
            field.Bubbles.Clear(); UiMotionPreference.OsProbe = () => true; UiMotionPreference.CacheStamp = long.MinValue;
            s.MotionLevel = MotionLevel.Off;
            ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlay.Spawn();
            Assert.Equal(BubbleMotionStyle.FloatUp, Assert.Single(field.Bubbles).Motion);
            field.Bubbles.Clear();
            s.MotionLevel = MotionLevel.Full; PrizeOwnership.IsGranted = _ => false;
            ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlay.Spawn();
            Assert.Equal(BubbleMotionStyle.FloatUp, Assert.Single(field.Bubbles).Motion);
        }
        finally
        {
            field.Bubbles.Clear(); windows.Clear(); targets.Clear(); window.Close();
            running.SetValue(null, saved.Running); pending.SetValue(null, saved.Pending);
            (s.BubbleMotionStyle, s.MotionLevel, s.GentleVisuals, s.BubbleSpeedBoost, PrizeOwnership.IsGranted) =
                (saved.BubbleMotionStyle, saved.MotionLevel, saved.GentleVisuals, saved.BubbleSpeedBoost, saved.IsGranted);
            (UiMotionPreference.OsProbe, UiMotionPreference.CacheStamp) = (saved.OsProbe, long.MinValue);
        }
        return Task.CompletedTask;
    });

}
