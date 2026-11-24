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
using ConditioningControlPanel.Services.UI;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF StudioTabView.xaml.cs + .Haptics.cs: the Haptics row's right-click flips the
/// page's own master box and its dot follows the box live; the flash and
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
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            var box = rack.HapticsPanel.ChkHapticsEnabled;
            box.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            var dot = rack.DotFor("haptics")!;
            Assert.True(IsRing(dot));

            RightClick(shell, rack, "haptics");
            Assert.True(s.Enabled);
            Assert.True(box.IsChecked);
            Assert.False(IsRing(dot));
            Assert.Equal(StudioTabView.DefaultRackKey, rack.SelectedRackKey);   // a toggle never selects

            box.IsChecked = false;                       // the page's own box: dot follows live
            Dispatcher.UIThread.RunJobs();
            Assert.False(s.Enabled);
            Assert.True(IsRing(dot));

            // WPF MainWindow.Haptics.cs:465 uses HapticsAccess, which is now open to everyone.
            CoreEntitlement.HasPremiumProvider = () => false;
            RightClick(shell, rack, "haptics");
            Assert.True(s.Enabled);
            Assert.True(box.IsChecked);
            Assert.False(IsRing(dot));
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

    /// <summary>Declutter wave 1 (WPF StudioRackRulesTests): on = filled dot + full label, off =
    /// hollow ring + dimmed label, never opacity alone; the group header counts what is on and
    /// moves with the dots; a row with no switch (Visuals) counts on neither side.</summary>
    [Fact]
    public Task OffIsARingAndADimmedLabelAndTheGroupHeaderCounts() => Run((shell, rack) =>
    {
        var s = CoreSettings.Current;
        var old = (s.FlashEnabled, s.MandatoryVideosEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled);
        try
        {
            s.FlashEnabled = s.MandatoryVideosEnabled = s.SubliminalEnabled = s.SpiralEnabled = s.PinkFilterEnabled = false;
            Dispatcher.UIThread.RunJobs();
            var dot = rack.DotFor("subliminal")!;
            var label = rack.LabelFor("subliminal")!;
            Assert.True(IsRing(dot));
            Assert.Equal(StudioRackRules.OffRingThickness, dot.StrokeThickness);
            Assert.Equal(StudioRackRules.OffLabelOpacity, label.Opacity);
            Assert.Equal((7d, 7d), (dot.Width, dot.Height));
            var effects = rack.GroupCountTexts[0];
            Assert.Contains("0", effects);
            Assert.Contains("5", effects);                 // five switches; Visuals has none

            s.SubliminalEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(IsRing(dot));
            Assert.Equal(1.0, label.Opacity);
            Assert.Contains("1", rack.GroupCountTexts[0]);
            Assert.DoesNotContain("0", rack.GroupCountTexts[0]);
            Assert.Equal(4, rack.GroupCountTexts.Count);
            Assert.All(rack.GroupCountTexts, t => Assert.NotEqual("", t));
        }
        finally
        {
            (s.FlashEnabled, s.MandatoryVideosEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled) = old;
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public void TheSelectedRowWearsThePaneSurfaceAndTheStudioHueBarInTheStripsPink()
    {
        var dir = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(SourcePath())!);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "CCP.Avalonia", "Views"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string Read(params string[] parts) =>
            System.IO.File.ReadAllText(System.IO.Path.Combine(new[] { dir!.FullName, "CCP.Avalonia" }.Concat(parts).ToArray()));

        var axaml = Read("Views", "Tabs", "StudioTabView.axaml");
        string After(string marker) { var t = axaml.Substring(axaml.IndexOf(marker, StringComparison.Ordinal)); return t.Substring(0, t.IndexOf("/>", StringComparison.Ordinal)); }
        Assert.Contains($"{{DynamicResource {StudioRackRules.SelectedFillBrushKey}}}", After("Name=\"RowFill\""));
        Assert.Contains($"{{DynamicResource {StudioRackRules.SelectedBarBrushKey}}}", After("Name=\"RowBar\""));
        // The fill is the pane's surface, so the rack card sits one step under it.
        Assert.NotEqual(StudioRackRules.SelectedFillBrushKey, StudioRackRules.RackGroundBrushKey);
        Assert.Contains($"Background=\"{{DynamicResource {StudioRackRules.RackGroundBrushKey}}}\"",
            axaml.Substring(axaml.IndexOf("Width=\"230\"", StringComparison.Ordinal), 160));

        var rgb = NavStripTable.Pink.ToString("X6");
        var colors = Read("Theme", "Colors.xaml");
        var brushes = Read("Theme", "Brushes.xaml");
        Assert.Contains($"<Color x:Key=\"SectionHueStudio\">#FF{rgb}</Color>", colors);
        Assert.Contains($"<Color x:Key=\"SectionHueStudioWash\">#14{rgb}</Color>", colors);
        Assert.Contains($"<Color x:Key=\"SectionHueStudioBorder\">#40{rgb}</Color>", colors);
        Assert.Contains($"x:Key=\"SectionHueStudioBrush\" Color=\"#FF{rgb}\"", brushes);
        Assert.Contains($"x:Key=\"SectionHueStudioBorderBrush\" Color=\"#40{rgb}\"", brushes);
    }

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string here = "") => here;

    /// <summary>A hollow ring: a stroke and no painted fill. The opposite is the filled disc.</summary>
    private static bool IsRing(global::Avalonia.Controls.Shapes.Ellipse dot)
    {
        bool filled = dot.Fill is global::Avalonia.Media.ISolidColorBrush f && f.Color.A > 0;
        bool ring = dot.Stroke != null && dot.StrokeThickness > 0;
        Assert.True(filled ^ ring, "a rack dot is a filled disc or a hollow ring, never both or neither");
        Assert.Equal(1.0, dot.Opacity);
        return ring;
    }

    private static void RightClick(MainShellWindow shell, StudioTabView rack, string key)
    {
        var row = rack.GetVisualDescendants().OfType<RadioButton>().First(r => r.Tag as string == key);
        row.BringIntoView();                             // the lower rows sit below the fold
        HeadlessFrame.Publish(shell);
        var at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), shell)!.Value;
        Assert.Same(row, (shell.InputHitTest(at) as Visual)?.FindAncestorOfType<RadioButton>(includeSelf: true));
        shell.MouseDown(at, MouseButton.Right);
        shell.MouseUp(at, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    private static Task Run(Action<MainShellWindow, StudioTabView> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("studio");
            HeadlessFrame.Publish(shell);
            body(shell, shell.StudioRack!);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
}
