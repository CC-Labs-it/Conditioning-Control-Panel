using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TabAmbientRegistryTests
{
    [Fact]
    public Task PremiumRoomRegistersOnceAndParksThroughRealNavigation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode);
        MainShellWindow? shell = null;
        try
        {
            (settings.MotionLevel, settings.PerformanceMode) = (MotionLevel.Full, false);
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show(); shell.Activate(); Settle();
            var registry = (Dictionary<string, List<AmbientFxCanvas>>)typeof(MainShellWindow)
                .GetField("_tabFxCanvases", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            var mosaic = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<AmbientFxCanvas>("MosaicFx")!;
            Assert.Contains(mosaic, registry["settings"]);

            // The old Exclusives key reaches Home > Premium, as on WPF. The canvas belongs
            // to that destination, rather than being an orphan outside the shell's governor.
            shell.ShowTab("exclusives"); Settle();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var room = view.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!;
            Assert.True(registry.ContainsKey("premium"));
            Assert.Same(room, Assert.Single(registry["premium"]));
            Assert.True(room.IsTicking);
            Assert.False(mosaic.IsTicking);

            shell.ShowTab("settings"); Settle();
            Assert.False(room.IsTicking);
            Assert.True(mosaic.IsTicking);
            // The registry pause survives another visibility change until navigation resumes it.
            view.IsVisible = true; Settle();
            Assert.False(room.IsTicking);
            shell.ShowTab("premium"); Settle();
            Assert.True(room.IsTicking);
            Assert.Single(registry["premium"]);

            settings.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(room.IsTicking);
            settings.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(room.IsTicking);
            settings.PerformanceMode = true;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(room.IsTicking);

            // WPF retired Play's Rabbit Hole canvas on 2026-09-18: no replacement loop.
            shell.ShowTab("play"); Settle();
            Assert.Empty(shell.Named<PlayTabView>("PlayTab")!.GetVisualDescendants().OfType<AmbientFxCanvas>());
            Assert.False(registry.ContainsKey("play"));
        }
        finally
        {
            shell?.Close(); Settle();
            (settings.MotionLevel, settings.PerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
