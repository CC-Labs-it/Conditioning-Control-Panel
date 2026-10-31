using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF StudioTabView.xaml.cs + .Haptics.cs: the Haptics row's right-click flips the
/// page's own master box (premium gate included) and its dot follows the box live; the flash and
/// bubbles rows wear the v2 pill from prize ownership, beside the dot, never under it.</summary>
public sealed class StudioRackWireTests
{
    [Fact]
    public Task HapticsRowRightClickGoesThroughThePageBoxAndTheDotFollows() => Run((shell, rack) =>
    {
        var s = CoreSettings.Current.Haptics;
        var (oldEnabled, oldPremium, oldFree) =
            (s.Enabled, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        try
        {
            s.Enabled = false;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;   // a free day would open the gate (P02)
            var box = rack.HapticsPanel.ChkHapticsEnabled;
            box.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            var dot = rack.DotFor("haptics")!;
            Assert.Equal(0.35, dot.Opacity);

            RightClick(shell, rack, "haptics");
            Assert.True(s.Enabled);
            Assert.True(box.IsChecked);
            Assert.Equal(1.0, dot.Opacity);
            Assert.Equal(StudioTabView.DefaultRackKey, rack.SelectedRackKey);   // a toggle never selects

            box.IsChecked = false;                       // the page's own box: dot follows live
            Dispatcher.UIThread.RunJobs();
            Assert.False(s.Enabled);
            Assert.Equal(0.35, dot.Opacity);

            CoreEntitlement.HasPremiumProvider = () => false;   // the page's premium gate refuses
            RightClick(shell, rack, "haptics");
            Assert.False(s.Enabled);
            Assert.False(box.IsChecked);
            Assert.Equal(0.35, dot.Opacity);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            (s.Enabled, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) =
                (oldEnabled, oldPremium, oldFree);
        }
    });

    [Theory]
    [InlineData(PrizeOwnership.BubbleRain, false, true)]
    [InlineData(PrizeOwnership.JackpotRemix, true, false)]
    [InlineData("none", false, false)]
    public Task V2PillsFollowOwnershipAndSitBesideTheDot(string granted, bool flash, bool bubbles)
    {
        var old = PrizeOwnership.IsGranted;
        PrizeOwnership.IsGranted = id => id == granted;
        return Run((shell, rack) =>
        {
            try
            {
                foreach (var (key, worn) in new[] { ("flash", flash), ("bubbles", bubbles) })
                {
                    rack.FocusRackEntry(key);
                    Dispatcher.UIThread.RunJobs();
                    var (strip, tile, label) = rack.V2PillsFor(key);
                    Assert.Equal(worn, strip!.IsVisible);
                    Assert.Equal(worn, tile!.IsVisible);
                    Assert.NotEqual(Grid.GetColumn(strip), Grid.GetColumn(rack.DotFor(key)!));
                    // The tile caption is held off a worn pill and keeps its full width otherwise.
                    if (worn) Assert.True(label!.Margin.Right > StudioTabView.TilePillInset + StudioTabView.TileLabelGap);
                    else Assert.Equal(StudioTabView.TileLabelInset, label!.Margin.Right);
                }
                Assert.Null(rack.V2PillsFor("video").Strip);   // only the two v2 families carry one
            }
            finally { PrizeOwnership.IsGranted = old; }
        });
    }

    [Fact]
    public Task V2PillsRefreshOnGrantAndRevoke() => Run((shell, rack) =>
    {
        var previous = PrizeOwnership.IsGranted;
        var user = CoreAccount.UnifiedUserId;
        try
        {
            CoreAccount.UnifiedUserId = "rack-prizes";
            PrizeOwnership.IsGranted = ConditioningControlPanel.Services.Prizes.PrizeGrants.IsGranted;
            PrizeOwnership.ResetHeld();
            PrizeOwnership.ApplySnapshot("rack-prizes", 1, new[] { PrizeOwnership.FlashPendulum, PrizeOwnership.BubbleRain });
            Dispatcher.UIThread.RunJobs();
            Assert.True(rack.V2PillsFor("flash").Strip!.IsVisible);
            Assert.True(rack.V2PillsFor("bubbles").Tile!.IsVisible);
            PrizeOwnership.ApplySnapshot("rack-prizes", 2, System.Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            Assert.False(rack.V2PillsFor("flash").Strip!.IsVisible);
            Assert.False(rack.V2PillsFor("bubbles").Tile!.IsVisible);
        }
        finally
        {
            PrizeOwnership.ResetHeld();
            CoreAccount.UnifiedUserId = user;
            PrizeOwnership.IsGranted = previous;
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static void RightClick(MainShellWindow shell, StudioTabView rack, string key)
    {
        var row = rack.GetVisualDescendants().OfType<RadioButton>().First(r => r.Tag as string == key);
        row.BringIntoView();                             // the lower rows sit below the fold
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), shell)!.Value;
        Assert.Same(row, (shell.InputHitTest(at) as Visual)?.FindAncestorOfType<RadioButton>(includeSelf: true));
        shell.MouseDown(at, MouseButton.Right);
        shell.MouseUp(at, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    private static Task Run(Action<MainShellWindow, StudioTabView> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("studio");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            body(shell, shell.StudioRack!);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
}
