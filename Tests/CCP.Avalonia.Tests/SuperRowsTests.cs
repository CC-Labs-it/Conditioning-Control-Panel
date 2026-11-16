using System;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Input;
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
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
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
            Assert.Equal(0.45, rows.Opacity);
            Assert.False(rows.IsHitTestVisible);
            Assert.Equal(KeyboardNavigationMode.None, KeyboardNavigation.GetTabNavigation(rows));
            SuperAccess.Set(SuperEffect.InnerBloom, true);
            Assert.True(rows.IsVisible);
            Assert.Equal(1, rows.Opacity);
            Assert.True(rows.IsHitTestVisible);
            Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(rows));
            var sliders = rows.GetLogicalDescendants().OfType<Slider>().ToArray();
            Assert.Equal(3, sliders.Length);
            Assert.True(sliders[0].Focus());
            Assert.Equal(4.5, sliders[0].Value);
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, "");
            Assert.Equal(5.0, sliders[0].Value);
            Assert.Equal(5.0, service.Current.FrothMaxSize);
            window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, "");
            Assert.Equal(5.5, sliders[0].Value);
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, "");
            Assert.Equal(5.0, sliders[0].Value);
            window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, "");
            Assert.Equal(4.5, sliders[0].Value);
            Assert.Equal(4.5, service.Current.FrothMaxSize);
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
            LocalizationManager.Instance.SetLanguage(oldLanguage);
        }
    });

    [Fact]
    public void PanicReturnsFrothRowsToLockedState() => AvaloniaTestDispatcher.Run(() =>
    {
        ShellTestPlatform.Ensure();
        var oldService = CoreSettings.ServiceProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldTrying = SuperAccess.TryingProvider;
        var oldEnded = SuperPreview.EndedByPanic;
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
            Assert.True(rows.IsVisible);
            Assert.Equal(0.45, rows.Opacity);
            Assert.False(rows.IsHitTestVisible);
            Assert.Equal(KeyboardNavigationMode.None, KeyboardNavigation.GetTabNavigation(rows));
            typeof(SuperPreview).GetField("_trying", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, SuperEffect.InnerBloom);
            typeof(SuperPreview).GetField("_startedTick", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, Environment.TickCount64);
            SuperAccess.RaiseChanged(SuperEffect.InnerBloom);
            Assert.True(rows.IsVisible);
            Assert.Equal(1, rows.Opacity);
            Assert.True(rows.IsHitTestVisible);
            Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(rows));
            PanicSurfaces.All.Single(s => s.Id == "super-preview").Stop(null);
            Assert.True(rows.IsVisible);
            Assert.Equal(0.45, rows.Opacity);
            Assert.False(rows.IsHitTestVisible);
            Assert.Equal(KeyboardNavigationMode.None, KeyboardNavigation.GetTabNavigation(rows));
            Assert.True(SuperPreview.EndedByPanic);
        }
        finally
        {
            SuperPreview.End(false);
            typeof(SuperPreview).GetProperty(nameof(SuperPreview.EndedByPanic))!.SetValue(null, oldEnded);
            window?.Close(); CoreSettings.ServiceProvider = oldService;
            CoreEntitlement.HasPremiumProvider = oldPremium; SuperAccess.TryingProvider = oldTrying;
            service.SealForReset();
        }
    });

    [Fact]
    public void FrothRowsRelocalizeAndStopListeningWhenDetached() => AvaloniaTestDispatcher.Run(() =>
    {
        ShellTestPlatform.Ensure();
        var loc = LocalizationManager.Instance;
        var oldLanguage = loc.CurrentLanguage;
        var oldService = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        Window? window = null;
        try
        {
            CoreSettings.ServiceProvider = () => service;
            service.Current.FrothPopReach = 0;
            service.Current.FrothCursorPush = 0;
            loc.SetLanguage("en");
            var page = new BubblePopFeatureControl();
            window = new Window { Content = page, Width = 800, Height = 800 };
            window.Show(); Dispatcher.UIThread.RunJobs();
            var rows = Assert.IsType<StackPanel>(Assert.Single(page.GetLogicalDescendants().OfType<SuperBox>()).Body);
            var row = rows.Children.OfType<Grid>().First();
            var title = Assert.IsType<TextBlock>(row.Children[0]);
            string englishTitle = title.Text!;
            string englishTip = Assert.IsType<string>(ToolTip.GetTip(row));
            loc.SetLanguage("de"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("super_froth_size"), title.Text);
            Assert.NotEqual(englishTitle, title.Text);
            Assert.Equal(Loc.Get("tooltip_super_froth_size"), ToolTip.GetTip(row));
            Assert.NotEqual(englishTip, ToolTip.GetTip(row));
            Assert.Equal(2, rows.GetLogicalDescendants().OfType<TextBlock>().Count(t => t.Text == Loc.Get("super_opt_off")));
            window.Content = null; Dispatcher.UIThread.RunJobs();
            string detachedTitle = title.Text!;
            loc.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(detachedTitle, title.Text);
            window.Content = page; Dispatcher.UIThread.RunJobs();
            Assert.Equal(englishTitle, title.Text);
        }
        finally
        {
            window?.Close(); CoreSettings.ServiceProvider = oldService;
            service.SealForReset(); loc.SetLanguage(oldLanguage);
        }
    });


    [Fact]
    public void RowTestRestoresLanguage() => AvaloniaTestDispatcher.Run(() =>
    {
        ShellTestPlatform.Ensure();
        var loc = LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("de");
            FrothRowsFollowSwitchAndWriteClampedSettings();
            Assert.Equal("de", loc.CurrentLanguage);
        }
        finally { loc.SetLanguage(language); }
    });

    [Fact]
    public void PanicTestRestoresPanicState() => AvaloniaTestDispatcher.Run(() =>
    {
        ShellTestPlatform.Ensure();
        var ended = SuperPreview.EndedByPanic;
        var property = typeof(SuperPreview).GetProperty(nameof(SuperPreview.EndedByPanic))!;
        try
        {
            property.SetValue(null, false);
            PanicReturnsFrothRowsToLockedState();
            Assert.False(SuperPreview.EndedByPanic);
        }
        finally { property.SetValue(null, ended); }
    });

}
