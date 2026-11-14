using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessPresetNotificationTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static KeywordTriggerPreset Seed()
    {
        var preset = new KeywordTriggerPreset
        {
            Id = "custom.notification", Name = "Notification test", IsBuiltIn = true,
            Triggers = new() { new() { Id = "test", Keyword = "hello" } }
        };
        CoreSettings.Current.KeywordTriggerPresets = new() { preset };
        CoreSettings.Current.KeywordTriggers = new();
        Assert.True(AvApp.KeywordPresets.InstallPreset(preset.Id));
        return preset;
    }

    private static Border Card(AwarenessTabView view) => view.FindControl<WrapPanel>("AwarenessPresetItems")!
        .Children.OfType<Border>().Single(x => Equals(x.Tag, "custom.notification"));

    [Fact]
    public void DialogUninstallRefreshesAttachedCardsBeforeDialogCloses() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var preset = Seed();
        var view = new AwarenessTabView();
        var host = new Window { Content = view };
        var dialog = new AwarenessPresetDetailDialog(preset);
        try
        {
            host.Show(); dialog.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Thickness(1.5), Card(view).BorderThickness);
            dialog.FindControl<Button>("BtnInstall")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.IsVisible);
            Assert.False(preset.MasterEnabled);
            Assert.Empty(CoreSettings.Current.KeywordTriggers);
            Assert.Equal(new Thickness(1), Card(view).BorderThickness);
        }
        finally { dialog.Close(); host.Close(); }
    });

    [Fact]
    public void DetachedTabIgnoresQueuedEventsAndReattachesOnce() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var preset = Seed();
        var view = new AwarenessTabView();
        var host = new Window { Content = view };
        try
        {
            host.Show(); Dispatcher.UIThread.RunJobs();
            var before = Card(view);
            AvApp.KeywordPresets.UninstallPreset(preset.Id); // queued refresh
            host.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(before, Card(view));
            host.Content = view; Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Thickness(1), Card(view).BorderThickness);
            before = Card(view);
            AvApp.KeywordPresets.InstallPreset(preset.Id);
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(before, Card(view));
            Assert.Equal(new Thickness(1.5), Card(view).BorderThickness);
        }
        finally { host.Close(); }
    });
}
