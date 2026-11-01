using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class AwarenessForegroundPrivacyTests
{
    [Fact]
    public Task SavedDenyAppStopsSameTitleForegroundAndAllowsItAgainAfterRemoval() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        // Keep the same settings service for the card and the observer.
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.AwarenessDenySeeded = true;
        s.AwarenessDenyList = new();
        s.AwarenessModeEnabled = true;
        s.AwarenessConsentGiven = true;
        WindowAwarenessService.V2Lifecycle = null;
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        AwarenessPause.Resume();
        var sample = (Title: "League of Legends", Process: "other-app");
        using var observer = AvApp.CreateWindowAwareness(() => sample);
        var count = 0;
        observer.ActivityChanged += (_, _) => count++;
        observer.Start();
        observer.PollOnce();
        Assert.Equal(1, count);

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var vm = new AwarenessPrivacyViewModel(() => shell);
            vm.AddDenyCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var picker = Assert.Single(shell.OwnedWindows.OfType<AwarenessAppPickerDialog>());
            picker.FindControl<TextBox>("TxtNewItem")!.Text = "private-editor";
            picker.FindControl<Button>("BtnAdd")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            picker.FindControl<Button>("BtnSave")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && !s.AwarenessDenyList.Contains("private-editor"); i++)
            { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            Assert.Contains("private-editor", s.AwarenessDenyList);

            // Same title, different process: the filter must run before the unchanged-title shortcut.
            sample.Process = "private-editor";
            observer.PollOnce();
            Assert.Equal(1, count);
            Assert.Equal(ActivityCategory.Unknown, observer.CurrentActivity);
            Assert.Equal("", observer.CurrentPageTitle);
            Assert.Equal("", observer.CurrentServiceName);

            vm.Sync();
            Assert.Single(vm.DenyList, c => c.Label == "private-editor").RemoveCommand.Execute(null);
            observer.PollOnce();
            Assert.Equal(2, count);
            Assert.Equal(ActivityCategory.Gaming, observer.CurrentActivity);
        }
        finally { shell.Close(); AwarenessPause.Resume(); }
    });

    [Fact]
    public void ProcessIdentityAppliesSeededGroupsAndUnknownProcessRetainsTitleFilter()
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.AwarenessDenySeeded = false;
        settings.Current.AwarenessDenyList = new();
        settings.Current.AwarenessModeEnabled = true;
        settings.Current.AwarenessConsentGiven = true;
        WindowAwarenessService.V2Lifecycle = null;
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        AwarenessPause.Resume();
        var sample = (Title: "League of Legends", Process: "bitwarden");
        using var observer = AvApp.CreateWindowAwareness(() => sample);
        var count = 0;
        observer.ActivityChanged += (_, _) => count++;
        observer.Start();
        observer.PollOnce();
        Assert.Equal(0, count);
        sample.Process = ""; // X11 client has no usable PID
        observer.PollOnce();
        Assert.Equal(1, count);
        sample.Title = "YouTube - Private Browsing";
        observer.PollOnce();
        Assert.Equal(1, count);
        Assert.Equal(ActivityCategory.Unknown, observer.CurrentActivity);
    }
}
