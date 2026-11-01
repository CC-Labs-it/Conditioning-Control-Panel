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
            Assert.True(room.Layers.HasFlag(AmbientFxLayers.VaultMotes));
            view.UpdateVaultZones();
            var zones = (VaultZone[])typeof(AmbientFxCanvas).GetField("_vaultZones", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(room)!;
            Assert.NotEmpty(zones);
            Assert.All(zones, z => Assert.True(z.Bounds.Bottom >= 0 && z.Bounds.Top <= room.Bounds.Height));
            var card = view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "ExCard" &&
                c.TranslatePoint(default, room) is { Y: >= 0 } at && at.Y <= room.Bounds.Height);
            var cardRect = new Rect(card.TranslatePoint(default, room)!.Value, card.Bounds.Size);
            Assert.Equal(((ExclusiveCardRow)card.DataContext!).IsLocked ? 1 : 2, zones.Count(z => z.Bounds == cardRect));
            Assert.Contains(zones, z => !z.Diamond && z.Color == global::Avalonia.Media.Color.Parse("#FFC85A"));

            int updates = view.VaultZoneUpdates;
            view.QueueVaultZones(); view.QueueVaultZones(); view.QueueVaultZones();
            Assert.Equal(updates, view.VaultZoneUpdates); // queued, never synchronous
            Settle(); Assert.Equal(updates + 1, view.VaultZoneUpdates);
            updates = view.VaultZoneUpdates;
            shell.InvalidateArrange(); Settle();
            Assert.Equal(updates, view.VaultZoneUpdates); // unrelated window layout
            var scroll = view.FindControl<ScrollViewer>("VaultContentScroll")!;
            scroll.Offset = new Vector(0, 60); scroll.Offset = new Vector(0, 90);
            Settle(); Assert.True(view.VaultZoneUpdates > updates);
            updates = view.VaultZoneUpdates;
            double roomHeight = room.Bounds.Height;
            view.Height = view.Bounds.Height - 20; Settle();
            Assert.NotEqual(roomHeight, room.Bounds.Height);
            Assert.True(view.VaultZoneUpdates > updates); // actual viewport, not Viewbox scaling
            view.Height = double.NaN; Settle();
            updates = view.VaultZoneUpdates;
            view.QueueVaultZones(); // queued while shown, executed after leaving
            shell.ShowTab("settings"); Settle();
            Assert.False(room.IsTicking);
            Assert.Equal(updates, view.VaultZoneUpdates);
            view.UpdateVaultZones(); view.QueueVaultZones();
            shell.InvalidateArrange(); Settle();
            Assert.Equal(updates, view.VaultZoneUpdates);
            Assert.True(mosaic.IsTicking);
            settings.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            // The registry pause survives another visibility change until navigation resumes it.
            view.IsVisible = true; Settle();
            Assert.False(room.IsTicking);
            view.UpdateVaultZones(); view.QueueVaultZones(); Settle();
            Assert.Equal(updates, view.VaultZoneUpdates); // visible but registry-paused
            AmbientFxCanvas.Env.RaiseMotionGateChanged(); Settle();
            Assert.False(room.IsTicking); // recomposition also preserves the registry pause
            shell.ShowTab("premium"); Settle();
            Assert.Equal(AmbientFxLayers.VaultMotes, room.Layers);
            Assert.True(room.IsTicking);
            Assert.Single(registry["premium"]);

            settings.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(room.IsTicking); // Reduced runs only the few slow vault motes.
            Assert.Equal(AmbientFxLayers.VaultMotes, room.Layers);
            settings.MotionLevel = MotionLevel.Off;
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
