using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Super;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The gold V2 box (WPF Controls/SuperBox.cs): star, tier refusal, weekly try, panic, placement.</summary>
public sealed class SuperBoxTests
{
    /// <summary>Runs <paramref name="body"/> on a shown box with isolated settings, tier and preview state.</summary>
    private static void WithBox(SuperEffect effect, bool premium, Action<SuperBox, SettingsService, Func<int>> body) => AvaloniaTestDispatcher.Run(() =>
    {
        ShellTestPlatform.Ensure();
        LocalizationManager.Instance.SetLanguage("en");
        var provider = CoreSettings.ServiceProvider;
        var hasPremium = CoreEntitlement.HasPremiumProvider;
        var denied = CoreEntitlement.ShowDeniedHandler;
        var trying = SuperAccess.TryingProvider;
        var all = Environment.GetEnvironmentVariable("CCP_SUPER_ALL");
        var service = new SettingsService();
        int refusals = 0;
        Window? window = null;
        try
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => premium;
            CoreEntitlement.ShowDeniedHandler = _ => refusals++;
            SuperAccess.TryingProvider = () => SuperPreview.Trying;
            typeof(AppSettings).GetProperty(SuperBase.SettingFor(effect))!.SetValue(service.Current, true);
            var box = new SuperBox { Effect = effect };
            window = new Window { Content = box, Width = 600, Height = 300 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            body(box, service, () => refusals);
        }
        finally
        {
            SuperPreview.End(panic: false);
            window?.Close();
            SuperAccess.TryingProvider = trying;
            CoreEntitlement.HasPremiumProvider = hasPremium;
            CoreEntitlement.ShowDeniedHandler = denied;
            CoreSettings.ServiceProvider = provider;
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", all);
            service.SealForReset();
        }
    });

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string[] CornerTexts(SuperBox box) => box.Corner.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();

    [Fact]
    public void StarTogglesAccessForUnlockedTier() => WithBox(SuperEffect.Scrawl, premium: true, (box, _, refusals) =>
    {
        Assert.True(box.IsLit);
        Assert.True(box.StarLit);
        Click(box.Star);
        Assert.False(SuperAccess.IsSwitchedOn(SuperEffect.Scrawl));
        Assert.False(box.StarLit);
        Click(box.Star);
        Assert.True(SuperAccess.IsSwitchedOn(SuperEffect.Scrawl));
        Assert.True(box.StarLit);
        Assert.Equal(0, refusals());
        Assert.Empty(box.Corner.Children); // a supporter gets no try corner
    });

    [Fact]
    public void LockedTierRoutesToDoorAndDoesNotSwitch() => WithBox(Locked(), premium: false, (box, service, refusals) =>
    {
        Assert.False(box.IsLit);
        Click(box.Star);
        Assert.Equal(1, refusals());
        Assert.Empty(service.Current.SuperModes);
        Assert.False(box.StarLit);
        Assert.Contains(Loc.Get("super_get_basic"), CornerTexts(box));
    });

    /// <summary>Not this week's effect, so a free account has no try for it.</summary>
    private static SuperEffect Locked() => SuperPreview.ThisWeek == SuperEffect.Scrawl ? SuperEffect.Creep : SuperEffect.Scrawl;

    [Fact]
    public void TryStartsOnceAWeekShowsCountdownAndPanicEndsIt() => WithBox(SuperPreview.ThisWeek, premium: false, (box, service, _) =>
    {
        var effect = box.Effect;
        var tryChip = Assert.Single(box.Corner.Children.OfType<Button>());
        Assert.Contains(Loc.Get("super_try"), CornerTexts(box));
        Click(tryChip);
        Assert.Equal(effect, SuperPreview.Trying);
        Assert.Equal(SuperPreviewRule.WeekIndex(ServerClock.UtcNow), service.Current.SuperPreviewUsedWeek);
        Assert.True(box.IsLit);
        Assert.Equal(Loc.GetF("super_try_left", 10), box.RingText);

        PanicSurfaces.All.Single(s => s.Id == "super-preview").Stop(null);
        Assert.Null(SuperPreview.Trying);
        Assert.True(SuperPreview.EndedByPanic);
        Assert.False(box.IsLit);
        Assert.Null(box.RingText);
        // Spent for the week: no second try, the corner says when it comes back.
        Assert.False(SuperPreview.TryStart(effect));
        Assert.Contains(Loc.Get("super_back_monday"), CornerTexts(box));
        Assert.DoesNotContain(Loc.Get("super_try"), CornerTexts(box));
    });

    [Fact]
    public void BoxSitsOnAllEightFeaturePages() => AvaloniaTestDispatcher.Run(() =>
    {
        ShellTestPlatform.Ensure();
        foreach (var (page, effect) in new (Func<Control>, SuperEffect)[]
        {
            (() => new FlashFeatureControl(), SuperEffect.FlickerDeck),
            (() => new BubblePopFeatureControl(), SuperEffect.InnerBloom),
            (() => new SpiralFeatureControl(), SuperEffect.Vortex),
            (() => new PinkFilterFeatureControl(), SuperEffect.Creep),
            (() => new SubliminalFeatureControl(), SuperEffect.Afterglow),
            (() => new BouncingTextFeatureControl(), SuperEffect.Scrawl),
            (() => new VideoFeatureControl(), SuperEffect.LightsDown),
            (() => new BrainDrainFeatureControl(), SuperEffect.Undertow),
        })
        {
            var control = page();
            var box = Assert.Single(control.GetLogicalDescendants().OfType<SuperBox>());
            Assert.Equal(effect, box.Effect);
        }
    });
}
