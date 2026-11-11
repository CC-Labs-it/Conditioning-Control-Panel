using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WorkshopTriggerSettingsTests
{
    [Fact]
    public void SettingsRestorePersistAndFollowExternalChanges() => AvaloniaTestDispatcher.Run(() =>
    {
        var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
        if (Application.Current == null)
            builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        else builder.AfterSetupCallback(builder);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.TriggerModeEnabled = true;
        service.Current.TriggerIntervalSeconds = 42;
        var cell = new WorkshopTriggersCell();
        var window = new Window { Content = cell };
        try
        {
            window.Show();
            var toggle = cell.FindControl<CheckBox>("ChkTriggerModeCompanion")!;
            var panel = cell.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!;
            var slider = cell.FindControl<Slider>("SliderTriggerIntervalCompanion")!;
            var label = cell.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!;
            Assert.True(toggle.IsChecked);
            Assert.True(panel.IsVisible);
            Assert.Equal(42, slider.Value);
            Assert.Equal("42s", label.Text);
            toggle.IsChecked = false;
            slider.Value = 75;
            Assert.False(service.Current.TriggerModeEnabled);
            Assert.Equal(75, service.Current.TriggerIntervalSeconds);
            Assert.False(panel.IsVisible);
            Assert.Equal("75s", label.Text);
            Assert.True(service.HasPendingSave);
            service.SaveImmediate();
            var reloaded = new SettingsService();
            Assert.False(reloaded.Current.TriggerModeEnabled);
            Assert.Equal(75, reloaded.Current.TriggerIntervalSeconds);
            service.Current.TriggerModeEnabled = true; // avatar menu writes the same setting
            Dispatcher.UIThread.RunJobs();
            Assert.True(toggle.IsChecked);
            Assert.True(panel.IsVisible);
        }
        finally { window.Close(); }
    });
    [Fact]
    public void ReplacementQueuedBeforeDetachDoesNotResubscribeAndReattachStillWorks() => AvaloniaTestDispatcher.Run(() =>
    {
        var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
        if (Application.Current == null)
            builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        else builder.AfterSetupCallback(builder);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var cell = new WorkshopTriggersCell();
        var window = new Window { Content = cell };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var replacement = new AppSettings { TriggerModeEnabled = false };
            service.RestoreFrom(replacement); // queues the replacement callback
            window.Content = null; // detach before it runs
            Dispatcher.UIThread.RunJobs();

            var handlers = (MulticastDelegate?)typeof(AppSettings)
                .GetField("PropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(replacement);
            Assert.DoesNotContain(handlers?.GetInvocationList() ?? Array.Empty<Delegate>(), h => ReferenceEquals(h.Target, cell));
            var toggle = cell.FindControl<CheckBox>("ChkTriggerModeCompanion")!;
            var detachedValue = toggle.IsChecked;
            replacement.TriggerModeEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(detachedValue, toggle.IsChecked);

            window.Content = cell;
            Dispatcher.UIThread.RunJobs();
            Assert.True(toggle.IsChecked);
            replacement.TriggerModeEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(toggle.IsChecked);
        }
        finally { window.Close(); }
    });

}
