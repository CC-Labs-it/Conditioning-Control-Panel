using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
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
        Setup();
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
            ConditioningControlPanel.CoreSession.IsSessionRunningProvider = () => false;
            shell.RefreshSessionFeatureLock();
            Assert.True(cmb.IsEnabled);
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
    public Task GrantsAfterConstructionRefreshOnUiThreadAndDetachStopsListening() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.OfflineMode, settings.UnifiedId, CoreAccount.UnifiedUserId,
            CoreSecrets.RetrieveProvider, PrizeOwnership.IsGranted, settings.BubbleMotionStyle);
        Window? host = null;
        try
        {
            settings.OfflineMode = false;
            settings.UnifiedId = "bubble-owner";
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "test-token" : null;
            PrizeOwnership.IsGranted = PrizeOwnership.Held;
            PrizeOwnership.ResetHeld();
            settings.BubbleMotionStyle = BubbleMotionStyle.SpiralIn;
            var panel = new BubblePopFeatureControl();
            var picker = panel.FindControl<ComboBox>("CmbMotion")!;
            var box = panel.FindControl<Border>("V2Box")!;
            host = new Window { Content = panel };
            host.Show();
            Assert.False(box.IsVisible);
            // The same accepted snapshot a purchase reply supplies, arriving off the UI thread.
            Task.Run(() => PrizeOwnership.ApplySnapshot("bubble-owner", 1, new[] { PrizeOwnership.BubbleRain })).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsVisible);
            Assert.Equal(new[] { BubbleMotionStyle.FloatUp, BubbleMotionStyle.Rain, BubbleMotionStyle.Mix },
                picker.Items.OfType<ComboBoxItem>().Select(i => (BubbleMotionStyle)i.Tag!).ToArray());
            Assert.Equal(BubbleMotionStyle.SpiralIn, settings.BubbleMotionStyle); // repaint must not save fallback
            PrizeOwnership.ApplySnapshot("bubble-owner", 2, new[] { PrizeOwnership.BubbleRain, PrizeOwnership.BubbleSpiralIn });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BubbleMotionStyle.SpiralIn, ((ComboBoxItem)picker.SelectedItem!).Tag);
            Assert.Equal(4, picker.Items.Count);
            settings.UnifiedId = "other-owner";
            CoreAccount.UnifiedUserId = "other-owner";
            Dispatcher.UIThread.RunJobs();
            Assert.False(box.IsVisible);
            settings.UnifiedId = "bubble-owner";
            CoreAccount.UnifiedUserId = "bubble-owner";
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsVisible);
            var first = picker.Items[0];
            PrizeOwnership.ApplySnapshot("bubble-owner", 3, System.Array.Empty<string>());
            host.Content = null; // also invalidate the repaint already queued above
            Dispatcher.UIThread.RunJobs();
            Assert.Same(first, picker.Items[0]);
            var changed = (System.Delegate?)typeof(ConditioningControlPanel.Services.Prizes.PrizeGrants).GetField("GrantsChanged",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null);
            Assert.DoesNotContain(changed?.GetInvocationList() ?? System.Array.Empty<System.Delegate>(), d => ReferenceEquals(d.Target, panel));
            host.Content = panel;
            Assert.False(box.IsVisible); // reload catches the snapshot missed while detached
            Assert.Single(picker.Items);
        }
        finally
        {
            host?.Close(); Dispatcher.UIThread.RunJobs();
            (settings.OfflineMode, settings.UnifiedId, CoreAccount.UnifiedUserId,
                CoreSecrets.RetrieveProvider, PrizeOwnership.IsGranted, settings.BubbleMotionStyle) = old;
            PrizeOwnership.ResetHeld();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task GrantProbeRestoresIdentitySettingsAndSeams()
    {
        var s = CoreSettings.Current;
        var before = (s.OfflineMode, s.UnifiedId, CoreAccount.UnifiedUserId,
            CoreSecrets.RetrieveProvider, PrizeOwnership.IsGranted, s.BubbleMotionStyle);
        await GrantsAfterConstructionRefreshOnUiThreadAndDetachStopsListening();
        Assert.Equal(before, (s.OfflineMode, s.UnifiedId, CoreAccount.UnifiedUserId,
            CoreSecrets.RetrieveProvider, PrizeOwnership.IsGranted, s.BubbleMotionStyle));
    }

    private static void Setup()
    {
        if (Application.Current is null)
        {
            // Same portable font mapping as QuestsDepthTests: this must also run alone on Linux.
            var fonts = AppFonts.Options();
            var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(AppFonts.Inter);
            fonts.FontFamilyMappings = mappings;
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        }
    }

}
