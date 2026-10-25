using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using System;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrainDrainAudioPanicTests
{
    [Fact]
    public System.Threading.Tasks.Task CardEnablesAndRefreshesRealHost() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var enabled = CoreSettings.Current.BrainDrainEnabled;
        var reload = CoreBrainDrain.ReloadClipsProvider;
        var count = CoreBrainDrain.ClipCountProvider;
        try
        {
            CoreSettings.Current.BrainDrainEnabled = false;
            CoreBrainDrain.ClipCountProvider = () => 3;
            var refreshes = 0;
            CoreBrainDrain.ReloadClipsProvider = () => refreshes++;
            var card = new ConditioningControlPanel.Avalonia.Views.Controls.Studio.BrainDrainFeatureControl();
            card.FindControl<global::Avalonia.Controls.CheckBox>("ChkEnable")!.IsChecked = true;
            Assert.True(CoreSettings.Current.BrainDrainEnabled);
            card.FindControl<global::Avalonia.Controls.Button>("BtnRefreshAudio")!.RaiseEvent(
                new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            Assert.Equal(1, refreshes);
            Assert.False(card.FindControl<global::Avalonia.Controls.Border>("NoAudioHint")!.IsVisible);
        }
        finally
        {
            CoreSettings.Current.BrainDrainEnabled = enabled;
            CoreBrainDrain.ReloadClipsProvider = reload;
            CoreBrainDrain.ClipCountProvider = count;
        }
        return System.Threading.Tasks.Task.CompletedTask;
    });

    [Fact]
    public System.Threading.Tasks.Task ShowingTheCardRescansTheClipFolder() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var reload = CoreBrainDrain.ReloadClipsProvider;
        var count = CoreBrainDrain.ClipCountProvider;
        Window? window = null;
        try
        {
            var clips = 0;
            var refreshes = 0;
            CoreBrainDrain.ClipCountProvider = () => clips;
            CoreBrainDrain.ReloadClipsProvider = () => { refreshes++; clips = 2; };
            var card = new ConditioningControlPanel.Avalonia.Views.Controls.Studio.BrainDrainFeatureControl();
            Assert.True(card.FindControl<Border>("NoAudioHint")!.IsVisible);
            window = new Window { Content = card };
            window.Show();   // WPF OnLoaded:43 re-scans before painting the count
            Assert.Equal(1, refreshes);
            Assert.False(card.FindControl<Border>("NoAudioHint")!.IsVisible);
        }
        finally
        {
            window?.Close();
            CoreBrainDrain.ReloadClipsProvider = reload;
            CoreBrainDrain.ClipCountProvider = count;
        }
        return System.Threading.Tasks.Task.CompletedTask;
    });

    [Fact]
    public void WatchdogFallbackStopsBrainDrain()
    {
        // WPF RunEmergencyPanicTeardown (MainWindow.xaml.cs:1122): a wedged UI thread must not leave
        // the background clip timer starting new clips after panic.
        var old = CoreBrainDrain.StopProvider;
        var stops = 0;
        try
        {
            CoreBrainDrain.StopProvider = () => stops++;
            ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.RunEmergencyTeardown();
            Assert.Equal(1, stops);
        }
        finally { CoreBrainDrain.StopProvider = old; }
    }

    [Fact]
    public void RegisteredPanicStopsAudioEvenWithoutShell()
    {
        var old = CoreBrainDrain.StopProvider;
        var stops = 0;
        try
        {
            CoreBrainDrain.StopProvider = () => stops++;
            var surface = Assert.Single(PanicSurfaces.All.Where(s => s.Id == "brain-drain-audio"));
            surface.Stop(null);
            surface.Stop(null);
            Assert.Equal(2, stops);
        }
        finally { CoreBrainDrain.StopProvider = old; }
    }
}
