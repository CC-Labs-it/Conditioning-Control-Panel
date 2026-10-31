using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VaultAmbientOwnershipTests
{
    [Fact]
    public void PremiumVisibilityAndWindowChangesPreserveTheGovernorsPause() => AvaloniaTestDispatcher.Run(() =>
    {
        AmbientBeatLoopsTests.Setup();
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = false;
        var view = new ExclusivesTabView();
        var host = new Window { Width = 1400, Height = 900, Content = view };
        try
        {
            host.Show(); host.Activate(); Settle();
            var room = view.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!;
            Assert.True(room.IsTicking);
            room.Pause(); // Same governor operation as SwitchTabFx when another tab is selected.
            view.IsVisible = false; Settle();
            view.IsVisible = true; Settle();
            Assert.False(room.IsTicking);
            host.Hide(); Settle();
            host.Show(); host.Activate(); Settle();
            Assert.False(room.IsTicking);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(room.IsTicking);
            room.Resume(); Settle();
            Assert.True(room.IsTicking);
        }
        finally { host.Close(); Settle(); }
    });

    [Fact]
    public void PlansSelectionParksAndResumesItsComposedRoom() => AvaloniaTestDispatcher.Run(() =>
    {
        AmbientBeatLoopsTests.Setup();
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = false;
        var view = new ExclusivesTabView { PlansMode = true };
        var host = new Window { Width = 900, Height = 900, Content = view };
        try
        {
            host.Show(); host.Activate(); Settle();
            var room = view.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!;
            Assert.False(room.IsTicking);
            view.SetPlansActive(true); Settle();
            Assert.True(room.IsTicking);
            view.SetPlansActive(false); Settle();
            Assert.False(room.IsTicking);
            view.SetPlansActive(true); Settle();
            Assert.True(room.IsTicking);
            host.Hide(); Settle();
            Assert.False(room.IsTicking);
            host.Show(); host.Activate(); Settle();
            Assert.True(room.IsTicking);
        }
        finally { host.Close(); Settle(); }
    });

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
