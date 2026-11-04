using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionReadoutTests
{
    [Fact]
    public Task ReadoutFollowsDirectorAndStopsWithLockdownAndShell() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = LockdownService.Current;
        using var lockdown = new LockdownService();
        var now = DateTime.UtcNow;
        lockdown.UtcNow = () => now;
        LockdownService.Current = lockdown;
        var settings = CoreSettings.Current;
        var saved = (settings.LockdownPossessionEnabled, settings.LockdownPossessionIntensity);
        settings.LockdownPossessionEnabled = true;
        settings.LockdownPossessionIntensity = (int)PossessionIntensity.FullDoki;
        var shell = new MainShellWindow();
        try
        {
            var tab = shell.Named<LockdownTabView>("LockdownTab")!;
            var text = tab.FindControl<TextBlock>("TxtPossessionRung")!;
            var pips = tab.FindControl<StackPanel>("PossessionPips")!;
            var director = shell.PossessionDirector!;
            lockdown.Activate(TimeSpan.FromMinutes(10));
            Dispatcher.UIThread.RunJobs();
            Assert.True(text.IsVisible); Assert.True(pips.IsVisible);
            Assert.Equal(Color.Parse("#33FF8A5C"), ((ISolidColorBrush)((Border)pips.Children[1]).Background!).Color);
            now += TimeSpan.FromMinutes(9);
            typeof(LockdownService).GetMethod("OnCountdownTick", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(lockdown, new object?[] { null, EventArgs.Empty });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PossessionRung.ItKnows, director.CurrentRung);
            Assert.Equal(Color.Parse("#FF8A5C"), ((ISolidColorBrush)((Border)pips.Children[4]).Background!).Color);
            Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("lockdown_poss_rung_4"), text.Text);
            settings.LockdownPossessionEnabled = false;
            tab.ShowLockdownState();
            Assert.False(text.IsVisible); Assert.False(pips.IsVisible);
            settings.LockdownPossessionEnabled = true;
            lockdown.Deactivate(); Dispatcher.UIThread.RunJobs();
            Assert.False(text.IsVisible); Assert.False(pips.IsVisible);
            var eventField = typeof(PossessionDirector).GetField("RungChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.NotNull(eventField.GetValue(director));
            shell.Show(); shell.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Null(eventField.GetValue(director));
        }
        finally
        {
            lockdown.Deactivate(); shell.Close(); LockdownService.Current = previous;
            (settings.LockdownPossessionEnabled, settings.LockdownPossessionIntensity) = saved;
        }
        return Task.CompletedTask;
    });
}
