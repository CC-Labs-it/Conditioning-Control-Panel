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
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .With(new global::Avalonia.Media.FontManagerOptions
                {
                    DefaultFamilyName = "fonts:Inter#Inter",
                    FontFamilyMappings = new System.Collections.Generic.Dictionary<string, global::Avalonia.Media.FontFamily>
                    {
                        ["Segoe UI"] = new("fonts:Inter#Inter"), ["Segoe UI Black"] = new("fonts:Inter#Inter")
                    }
                }).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var seam = PrizeOwnership.IsGranted;
        var saved = CoreSettings.Current.BubbleMotionStyle;
        ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow? shell = null;
        Window? host = null;
        try
        {
            // Without the Get it row, this head hides the box when no style is owned.
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

            var choice = (StackPanel)((ComboBoxItem)cmb.Items[1]!).Content!;
            var pill = Assert.IsType<Border>(choice.Children[1]);
            Assert.Equal(Loc.Get("fx_v2_badge"), Assert.IsType<TextBlock>(pill.Child).Text);
            ToolTip.SetTip(cmb, "original motion tip");
            cmb.SelectedIndex = 1;
            Assert.Equal(BubbleMotionStyle.SpiralIn, CoreSettings.Current.BubbleMotionStyle);

            // A setting changed elsewhere moves the picker (WPF watch list :201), and a settings
            // tick only reselects: the items are the same instances (WPF SelectMotion).
            // (Hosted in its own window: the settings hook lives while the card is attached.)
            var live = new BubblePopFeatureControl();
            host = new Window { Content = live };
            host.Show();
            var liveCmb = live.FindControl<ComboBox>("CmbMotion")!;
            Dispatcher.UIThread.RunJobs();
            var firstItem = liveCmb.Items[0];
            CoreSettings.Current.BubbleMotionStyle = BubbleMotionStyle.Mix;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BubbleMotionStyle.Mix, (BubbleMotionStyle)((ComboBoxItem)liveCmb.SelectedItem!).Tag!);
            Assert.Same(firstItem, liveCmb.Items[0]);

            ConditioningControlPanel.CoreSession.IsSessionRunningProvider = () => true;
            shell.RefreshSessionFeatureLock();
            Assert.False(cmb.IsEnabled);
            Assert.Equal(shell.SessionFeatureLockReason, ToolTip.GetTip(cmb));
            ConditioningControlPanel.CoreSession.IsSessionRunningProvider = () => false;
            shell.RefreshSessionFeatureLock();
            Assert.True(cmb.IsEnabled);
            Assert.Equal("original motion tip", ToolTip.GetTip(cmb));

            // WPF GrantsChanged refreshes an attached picker without changing the saved choice.
            CoreSettings.Current.OfflineMode = false;
            CoreSettings.Current.UnifiedId = "bubble-motion-test";
            CoreSecrets.RetrieveProvider = _ => "test-only";
            PrizeOwnership.IsGranted = PrizeOwnership.Held;
            PrizeOwnership.ApplySnapshot("bubble-motion-test", 1, new[] { PrizeOwnership.BubbleRain });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { BubbleMotionStyle.FloatUp, BubbleMotionStyle.Rain, BubbleMotionStyle.Mix },
                liveCmb.Items.OfType<ComboBoxItem>().Select(i => (BubbleMotionStyle)i.Tag!).ToArray());
            Assert.Equal(BubbleMotionStyle.Mix, CoreSettings.Current.BubbleMotionStyle);
            Assert.Equal(BubbleMotionStyle.Mix, ((ComboBoxItem)liveCmb.SelectedItem!).Tag);
        }
        finally
        {
            ConditioningControlPanel.CoreSession.IsSessionRunningProvider = null;
            PrizeOwnership.IsGranted = seam;
            PrizeOwnership.ResetHeld();
            CoreSettings.Current.BubbleMotionStyle = saved;
            shell?.Close();
            host?.Close();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });
}
