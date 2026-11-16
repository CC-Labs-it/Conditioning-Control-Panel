using System;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Super;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SuperRowsTests
{
    [Fact]
    public void FrothRowsFollowSwitchAndWriteClampedSettings() => AvaloniaTestDispatcher.Run(() =>
    {
        ShellTestPlatform.Ensure();
        LocalizationManager.Instance.SetLanguage("en");
        var oldService = CoreSettings.ServiceProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldTrying = SuperAccess.TryingProvider;
        var service = new SettingsService();
        Window? window = null;
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => true;
            SuperAccess.TryingProvider = () => null;
            var page = new BubblePopFeatureControl();
            window = new Window { Content = page, Width = 800, Height = 800 };
            window.Show(); Dispatcher.UIThread.RunJobs();
            var box = Assert.Single(page.GetLogicalDescendants().OfType<SuperBox>());
            var rows = Assert.IsType<StackPanel>(box.Body);
            SuperAccess.Set(SuperEffect.InnerBloom, false);
            Assert.False(rows.IsVisible);
            SuperAccess.Set(SuperEffect.InnerBloom, true);
            Assert.True(rows.IsVisible);
            var sliders = rows.GetLogicalDescendants().OfType<Slider>().ToArray();
            Assert.Equal(3, sliders.Length);
            sliders[0].Value = 5.7;
            sliders[1].Value = 0;
            sliders[2].Value = 0;
            Assert.Equal(5.5, service.Current.FrothMaxSize);
            Assert.Equal(0, service.Current.FrothPopReach);
            Assert.Equal(0, service.Current.FrothCursorPush);
            Assert.Equal(2, rows.GetLogicalDescendants().OfType<TextBlock>().Count(t => t.Text == Loc.Get("super_opt_off")));
            sliders[1].Value = 137;
            Assert.Equal(140, service.Current.FrothPopReach);
        }
        finally
        {
            window?.Close(); CoreSettings.ServiceProvider = oldService;
            CoreEntitlement.HasPremiumProvider = oldPremium; SuperAccess.TryingProvider = oldTrying;
            service.SealForReset();
        }
    });

    [Fact]
    public void PanicHidesFrothPreviewRows() => AvaloniaTestDispatcher.Run(() =>
    {
        ShellTestPlatform.Ensure();
        var oldService = CoreSettings.ServiceProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldTrying = SuperAccess.TryingProvider;
        var service = new SettingsService();
        Window? window = null;
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => false;
            SuperAccess.TryingProvider = () => SuperPreview.Trying;
            var page = new BubblePopFeatureControl();
            window = new Window { Content = page, Width = 800, Height = 800 };
            window.Show(); Dispatcher.UIThread.RunJobs();
            var rows = Assert.IsType<StackPanel>(Assert.Single(page.GetLogicalDescendants().OfType<SuperBox>()).Body);
            Assert.False(rows.IsVisible);
            typeof(SuperPreview).GetField("_trying", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, SuperEffect.InnerBloom);
            typeof(SuperPreview).GetField("_startedTick", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, Environment.TickCount64);
            SuperAccess.RaiseChanged(SuperEffect.InnerBloom);
            Assert.True(rows.IsVisible);
            PanicSurfaces.All.Single(s => s.Id == "super-preview").Stop(null);
            Assert.False(rows.IsVisible);
            Assert.True(SuperPreview.EndedByPanic);
        }
        finally
        {
            SuperPreview.End(false); window?.Close(); CoreSettings.ServiceProvider = oldService;
            CoreEntitlement.HasPremiumProvider = oldPremium; SuperAccess.TryingProvider = oldTrying;
            service.SealForReset();
        }
    });
}
