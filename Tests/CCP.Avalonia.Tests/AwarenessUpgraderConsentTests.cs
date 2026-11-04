using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessUpgraderConsentTests
{
    [Fact]
    public void AcceptedConsentRestartsTheAwarenessLifecycle()
    {
        var oldProvider = CoreSettings.ServiceProvider;
        var oldHook = WindowAwarenessService.V2Lifecycle;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        var service = new SettingsService();
        var events = new List<bool>();
        CoreSettings.ServiceProvider = () => service;
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        WindowAwarenessService.V2Lifecycle = events.Add;
        var s = CoreSettings.Current;
        s.AwarenessModeEnabled = true;
        s.AwarenessConsentGiven = true;
        try
        {
            App.WindowAwareness.Stop();
            App.WindowAwareness.Start();
            events.Clear();
            MainShellWindow.RestartAwarenessAfterV2Consent();
            Assert.Equal(new[] { false, true }, events);
        }
        finally
        {
            App.WindowAwareness.Stop();
            WindowAwarenessService.V2Lifecycle = oldHook;
            CoreEntitlement.HasPremiumProvider = oldPremium;
            CoreEntitlement.IsFreeTodayProvider = oldFree;
            CoreSettings.ServiceProvider = oldProvider;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task UpgraderRestartsOnlyAfterAccepting(bool accept) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldHook = WindowAwarenessService.V2Lifecycle;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        var events = new List<bool>();
        // Keep one settings instance throughout the test.
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        WindowAwarenessService.V2Lifecycle = events.Add;
        var s = CoreSettings.Current;
        s.UseAwarenessV2 = true;
        s.AwarenessModeEnabled = true;
        s.AwarenessConsentGiven = true;
        s.AwarenessConsentShownV2 = false;
        var shell = new MainShellWindow();
        try
        {
            App.WindowAwareness.Stop();
            App.WindowAwareness.Start();
            events.Clear();
            shell.Show();
            shell.EnsureAwarenessV2Consent();
            for (var i = 0; i < 200 && !shell.OwnedWindows.OfType<AwarenessConsentDialog>().Any(); i++)
            { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            var dialog = Assert.Single(shell.OwnedWindows.OfType<AwarenessConsentDialog>());
            dialog.FindControl<Button>(accept ? "BtnAccept" : "BtnDecline")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 200 && accept && events.Count < 2; i++)
            { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(accept ? new[] { false, true } : Array.Empty<bool>(), events);
            Assert.Equal(accept, s.AwarenessConsentShownV2);
        }
        finally
        {
            App.WindowAwareness.Stop();
            foreach (var window in shell.OwnedWindows.ToList()) window.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            WindowAwarenessService.V2Lifecycle = oldHook;
            CoreEntitlement.HasPremiumProvider = oldPremium;
            CoreEntitlement.IsFreeTodayProvider = oldFree;
            CoreSettings.ServiceProvider = oldProvider;
        }
    });
}
