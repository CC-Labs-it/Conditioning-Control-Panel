using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Super;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The gold boxes' option rows on this head (twins of WPF 7.4.1's XAML rows) and SuperDim.</summary>
public sealed class SuperPageRowsTests
{
    private static void WithRows(bool premium, Action<SettingsService> body) => AvaloniaTestDispatcher.Run(() =>
    {
        ShellTestPlatform.Ensure();
        var loc = LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        var provider = CoreSettings.ServiceProvider;
        var hasPremium = CoreEntitlement.HasPremiumProvider;
        var trying = SuperAccess.TryingProvider;
        var all = Environment.GetEnvironmentVariable("CCP_SUPER_ALL");
        var service = new SettingsService();
        try
        {
            loc.SetLanguage("en");
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => premium;
            SuperAccess.TryingProvider = () => null;
            body(service);
        }
        finally
        {
            SuperAccess.TryingProvider = trying;
            CoreEntitlement.HasPremiumProvider = hasPremium;
            CoreSettings.ServiceProvider = provider;
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", all);
            service.SealForReset();
            loc.SetLanguage(language);
        }
    });

    private static Window Show(Control c)
    {
        var w = new Window { Content = c, Width = 700, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static string[] Keys(Control rows) =>
        rows.GetLogicalDescendants().OfType<Grid>().Select(g => g.Tag).OfType<string>().ToArray();

    [Fact]
    public void EveryPageCarriesItsOwnRowsAndEveryStringResolves() => WithRows(true, _ =>
    {
        foreach (var (page, effect, rows) in new (Func<Control>, SuperEffect, int)[]
        {
            (() => new PinkFilterFeatureControl(), SuperEffect.Creep, 6),
            (() => new FlashFeatureControl(), SuperEffect.FlickerDeck, 3),
            (() => new SpiralFeatureControl(), SuperEffect.Vortex, 11),
            (() => new SubliminalFeatureControl(), SuperEffect.Afterglow, 4),
            (() => new BouncingTextFeatureControl(), SuperEffect.Scrawl, 5),
            (() => new VideoFeatureControl(), SuperEffect.LightsDown, 4),
            (() => new BrainDrainFeatureControl(), SuperEffect.Undertow, 3),
            (() => new BubblePopFeatureControl(), SuperEffect.InnerBloom, 3),
        })
        {
            var box = Assert.Single(page().GetLogicalDescendants().OfType<SuperBox>());
            var body = Assert.IsType<SuperOptionRows>(box.Body);
            Assert.Equal(effect, body.Kind);
            body.Refresh();
            var keys = Keys(body);
            Assert.Equal(rows, keys.Length);
            foreach (var key in keys)
            {
                Assert.NotEqual(key, Loc.Get(key));
                var row = body.GetLogicalDescendants().OfType<Grid>().First(g => (string?)g.Tag == key);
                if (ToolTip.GetTip(row) is string tip) Assert.NotEqual("tooltip_" + key, tip);
            }
        }
    });

    [Fact]
    public void SlidersWriteCoreClampsAndToggleWritesTheSetting() => WithRows(true, service =>
    {
        var rows = SuperPageRows.Steam();
        var w = Show(rows);
        try
        {
            Assert.True(rows.IsVisible);
            var sliders = rows.GetLogicalDescendants().OfType<Slider>().ToArray();
            sliders[0].Value = 3;                       // reach: Core floor is 10
            Assert.Equal(10, service.Current.SteamReach);
            sliders[2].Value = 999;                     // fog return: Core ceiling 300
            Assert.Equal(300, service.Current.SteamFogReturn);
            var words = rows.GetLogicalDescendants().OfType<CheckBox>().First();
            Assert.Equal(service.Current.SteamWordsInFog, words.IsChecked);
            words.IsChecked = !words.IsChecked;
            Assert.Equal(words.IsChecked, service.Current.SteamWordsInFog);
            var creep = rows.GetLogicalDescendants().OfType<ComboBox>().Single();
            Assert.Equal(3, creep.Items.Count);
            creep.SelectedIndex = 2;
            Assert.Equal(CreepSpeed.Fast, service.Current.SuperCreepSpeed);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void ScrawlStampSizeRowSnapsAndClamps() => WithRows(true, service =>
    {
        var rows = SuperPageRows.Scrawl();
        var w = Show(rows);
        try
        {
            var size = rows.GetLogicalDescendants().OfType<Grid>().First(g => (string?)g.Tag == "super_scrawl_size").Children.OfType<Slider>().Single();
            size.Value = 999;
            Assert.Equal(ConditioningControlPanel.SuperCore.ScrawlOptions.StampSizeMax, service.Current.ScrawlStampSize);
            var slam = rows.GetLogicalDescendants().OfType<Grid>().First(g => (string?)g.Tag == "super_scrawl_slam");
            slam.Children.OfType<Slider>().Single().Value = 0;
            Assert.Contains(slam.Children.OfType<TextBlock>(), t => t.Text == Loc.Get("super_opt_off"));
        }
        finally { w.Close(); }
    });

    [Fact]
    public void DeckRollEndsDragEachOther() => WithRows(true, service =>
    {
        var rows = SuperPageRows.Deck();
        var w = Show(rows);
        try
        {
            var sliders = rows.GetLogicalDescendants().OfType<Slider>().ToArray();
            sliders[0].Value = 8;                       // fewest past most: most follows
            var (lo, hi) = ConditioningControlPanel.SuperCore.DeckOptions.BreakRange(service.Current.DeckFlips, service.Current.DeckFlipsFewest, service.Current.DeckFlipsMost);
            Assert.Equal(8, lo);
            Assert.Equal(8, hi);
            Assert.Equal(8, sliders[1].Value);
            sliders[1].Value = 2;                       // most under fewest: fewest follows
            (lo, hi) = ConditioningControlPanel.SuperCore.DeckOptions.BreakRange(service.Current.DeckFlips, service.Current.DeckFlipsFewest, service.Current.DeckFlipsMost);
            Assert.Equal((2, 2), (lo, hi));
        }
        finally { w.Close(); }
    });

    [Fact]
    public void WhirlpoolShowsFourRowsAndFoldsTheRest() => WithRows(true, service =>
    {
        var rows = SuperPageRows.Whirlpool();
        var w = Show(rows);
        try
        {
            var fold = rows.Children.OfType<MoreFold>().Single();
            Assert.False(fold.IsOpen);
            Assert.Equal(4, rows.Children.OfType<Grid>().Count());
            Assert.Equal(7, fold.Children.OfType<Grid>().Count());
            Assert.All(fold.Children.OfType<Grid>(), g => Assert.False(g.IsVisible));
            var every = fold.Children.OfType<Grid>().First(g => (string?)g.Tag == "super_pool_every");
            Assert.Equal(1, every.Opacity);
            service.Current.WhirlpoolSpiral = "held_one";   // one spiral held: the fold opens and "every" dims
            rows.Refresh();
            Assert.True(fold.IsOpen);
            Assert.Equal(SuperDim.DimOpacity, every.Opacity);
            Assert.False(every.IsHitTestVisible);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void FreeAccountSeesHostedRowsLockedAndUnhostedRowsFolded() => WithRows(false, _ =>
    {
        var hosted = SuperPageRows.Afterglow();
        var undertow = SuperPageRows.Undertow();
        var panel = new StackPanel { Children = { hosted, undertow } };
        var w = Show(panel);
        try
        {
            Assert.Contains(SuperEffect.Afterglow, SuperBox.HostedOnThisHead);
            Assert.True(hosted.IsVisible);
            Assert.Equal(SuperRows.LockedOpacity, hosted.Opacity);
            Assert.False(hosted.IsHitTestVisible);
            // Owner call 2026-11-25: Undertow waits for a Brain Drain lane, its rows stay folded.
            Assert.DoesNotContain(SuperEffect.Undertow, SuperBox.HostedOnThisHead);
            Assert.False(undertow.IsVisible);
            CoreEntitlement.HasPremiumProvider = () => true;
            undertow.Refresh();
            Assert.False(undertow.IsVisible);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void SuperDimFadesAndLocksWhileTheEffectIsLit() => WithRows(true, service =>
    {
        var row = new Border { Width = 10, Height = 10 };
        var w = Show(row);
        try
        {
            SuperAccess.Set(SuperEffect.Creep, false);
            SuperDim.SetWhen(row, SuperEffect.Creep);
            Assert.Equal(1, row.Opacity);
            SuperAccess.Set(SuperEffect.Creep, true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SuperDim.DimOpacity, row.Opacity);
            Assert.False(row.IsHitTestVisible);
            SuperAccess.Set(SuperEffect.Creep, false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, row.Opacity);
            Assert.True(row.IsHitTestVisible);
            SuperDim.SetWhen(row, null);
        }
        finally { w.Close(); }
    });
}
