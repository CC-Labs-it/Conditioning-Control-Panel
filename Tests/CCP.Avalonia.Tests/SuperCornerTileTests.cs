using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Super;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The gold cut corner on dashboard tiles (WPF Controls/SuperCorner + FeatureCard/SplitFeatureCard.ApplySuper)
/// and the weekly try's hosted-only rule.</summary>
public sealed class SuperCornerTileTests
{
    private static void Isolated(bool premium, Action<SettingsService, Func<int>> body) => AvaloniaTestDispatcher.Run(() =>
    {
        ShellTestPlatform.Ensure();
        var provider = CoreSettings.ServiceProvider;
        var hasPremium = CoreEntitlement.HasPremiumProvider;
        var denied = CoreEntitlement.ShowDeniedHandler;
        var trying = SuperAccess.TryingProvider;
        var all = Environment.GetEnvironmentVariable("CCP_SUPER_ALL");
        var hosted = SuperBox.HostedOnThisHead.ToArray();
        var ended = SuperPreview.EndedByPanic;
        var service = new SettingsService();
        int refusals = 0;
        try
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => premium;
            CoreEntitlement.ShowDeniedHandler = _ => refusals++;
            SuperAccess.TryingProvider = () => SuperPreview.Trying;
            body(service, () => refusals);
        }
        finally
        {
            SuperPreview.End(panic: false);
            typeof(SuperPreview).GetProperty(nameof(SuperPreview.EndedByPanic))!.SetValue(null, ended);
            SuperBox.HostedOnThisHead.Clear();
            SuperBox.HostedOnThisHead.UnionWith(hosted);
            SuperAccess.TryingProvider = trying;
            CoreEntitlement.ShowDeniedHandler = denied;
            CoreEntitlement.HasPremiumProvider = hasPremium;
            CoreSettings.ServiceProvider = provider;
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", all);
            service.SealForReset();
        }
    });

    [Fact]
    public void OnlyAHostedEffectGetsACorner()
    {
        Assert.NotNull(SuperCorner.For("Scrawl", false, () => false));
        Assert.Null(SuperCorner.For("Undertow", true, () => false));     // owner call 2026-11-25: not hosted here
        Assert.Null(SuperCorner.For(null, false, () => false));
        Assert.Null(SuperCorner.For("NotAnEffect", false, () => false));
    }

    [Fact]
    public void TheCornerIsTheCutTriangleOnly()
    {
        var tl = new SuperCorner(SuperEffect.Scrawl, bottomRight: false);
        Assert.True(tl.HitTest(new Point(4, 4)));
        Assert.False(tl.HitTest(new Point(26, 26)));
        var br = new SuperCorner(SuperEffect.Creep, bottomRight: true);
        Assert.True(br.HitTest(new Point(26, 26)));
        Assert.False(br.HitTest(new Point(4, 4)));
    }

    [Fact]
    public void AFreeAccountSeesTheCornerLockedAndAPressRefuses() => Isolated(false, (service, refusals) =>
    {
        var corner = new SuperCorner(SuperEffect.Scrawl, bottomRight: false);
        var w = new Window { Content = new Grid { Children = { corner } }, Width = 200, Height = 200 };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(corner.IsVisible);
            Assert.False(corner.Lit);
            corner.Press();
            Assert.Equal(1, refusals());
            Assert.Empty(service.Current.SuperModes);
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.True(corner.Lit);
            corner.Press();
            Assert.False(corner.Lit);
            Assert.Equal(1, refusals());
        }
        finally { w.Close(); }
    });

    [Fact]
    public void TilesWearTheirCornersAndStepTheirMarksAside() => Isolated(true, (_, _) =>
    {
        var card = new FeatureCard { Super = "FlickerDeck" };
        Assert.NotNull(card.SuperCornerControl);
        Assert.Equal(SuperEffect.FlickerDeck, card.SuperCornerControl!.Effect);
        Assert.Equal(SuperCorner.Leg + 2, card.FindControl<StackPanel>("BadgeColumn")!.Margin.Left);
        card.Super = null;
        Assert.Null(card.SuperCornerControl);
        Assert.Equal(8, card.FindControl<StackPanel>("BadgeColumn")!.Margin.Left);

        var split = new SplitFeatureCard { SuperA = "Vortex", SuperB = "Creep" };
        Assert.Equal(SuperEffect.Vortex, split.CornerA!.Effect);
        Assert.True(split.CornerB!.BottomRight);
        Assert.Equal(SuperCorner.Leg + 2, split.FindControl<Border>("TitlePillA")!.Margin.Left);
        var drain = new SplitFeatureCard { SuperB = "Undertow" };
        Assert.Null(drain.CornerB);
        Assert.Equal(8, drain.FindControl<Border>("TitlePillB")!.Margin.Right);
    });

    [Fact]
    public void TheWeeklyTryIsOnlyForAnEffectThisHeadHosts() => Isolated(false, (service, _) =>
    {
        var week = SuperPreview.ThisWeek;
        SuperBox.HostedOnThisHead.Remove(week);
        Assert.False(SuperPreview.CanTry(week));
        Assert.False(SuperPreview.TryStart(week));
        Assert.Equal(SuperPreviewRule.NeverUsed, service.Current.SuperPreviewUsedWeek);   // nothing spent
        SuperBox.HostedOnThisHead.Add(week);
        Assert.True(SuperPreview.CanTry(week));
        Assert.True(SuperPreview.TryStart(week));
        Assert.Equal(week, SuperPreview.Trying);
        PanicSurfaces.All.Single(s => s.Id == "super-preview").Stop(null);   // panic ends the try on every press
        Assert.Null(SuperPreview.Trying);
        Assert.True(SuperPreview.EndedByPanic);
    });
}
