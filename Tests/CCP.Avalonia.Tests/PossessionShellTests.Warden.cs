using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Possession;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class PossessionShellTests
{
    [Fact]
    public Task WardenStopReleasesDirectorBooking() => RunAsync(async rig =>
    {
        Frame();
        var button = rig.Shell.Named<Button>("BtnClose")!;
        var target = ((IPossessionHost)rig.Shell).Targets.Single(t => ReferenceEquals(t.Visual, button));
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        rig.Clock = rig.Clock.AddMinutes(4);
        Tick(rig.Lockdown);
        rig.Director.RequestReactive("dodge", target);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, rig.Director.LiveEffectCount);
        AvApp.Possession = rig.Director;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = true;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(rig.Lockdown);
        try
        {
            tube.Show(); AvatarTubeWindow.TestLive = tube; tube.Position = new PixelPoint(-500, -500);
            var knock = warden.KnockAsync(target, CancellationToken.None);
            await Task.Delay(100);
            Assert.NotEqual(new PixelPoint(-500, -500), tube.Position);
            warden.Stop();
            Assert.Equal(0, rig.Director.LiveEffectCount);
            Assert.False(target.IsLive);
            await knock;
            Assert.Equal(0, rig.Director.LiveEffectCount);
        }
        finally { warden.Stop(); AvatarTubeWindow.TestLive = null; tube.Close(); }
    });
    [Fact]
    public Task LockdownEndReassemblesBeforeWardenReturns() => RunAsync(async rig =>
    {
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = true;
        CoreSettings.Current.LockdownWardenEnabled = true;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(rig.Lockdown);
        // App constructs the warden before the director, so its handlers precede the director's.
        var endMethod = typeof(PossessionDirector).GetMethod("OnLockdownDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var endDirector = (Action)Delegate.CreateDelegate(typeof(Action), rig.Director, endMethod);
        rig.Lockdown.LockdownDeactivated -= endDirector;
        rig.Lockdown.LockdownDeactivated += endDirector;
        try
        {
            tube.Show(); AvatarTubeWindow.TestLive = tube;
            var home = new PixelPoint(200, 250); tube.Position = home;
            rig.Director.Warden = warden; AvApp.Possession = rig.Director;
            rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
            await warden.LeaveAsync(CancellationToken.None);
            Assert.True(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);

            Frame();
            var host = (IAvaloniaPossessionHost)rig.Shell;
            var button = rig.Shell.Named<Button>("BtnClose")!;
            var target = ((IPossessionHost)rig.Shell).Targets.Single(t => ReferenceEquals(t.Visual, button));
            rig.Clock = rig.Clock.AddMinutes(4); Tick(rig.Lockdown);
            rig.Director.RequestReactive("dodge", target);
            Dispatcher.UIThread.RunJobs();
            var bounds = host.BoundsOnGhostLayer(button)!.Value; var now = DateTime.UtcNow;
            host.PossessionPointer.Move(new Point(bounds.X - 110, bounds.Center.Y), null, now);
            host.PossessionPointer.Move(new Point(bounds.X - 70, bounds.Center.Y), null, now.AddMilliseconds(50));
            rig.Frames.Frame(TimeSpan.FromMilliseconds(1000)); rig.Frames.Frame(TimeSpan.FromMilliseconds(1450));
            Dispatcher.UIThread.RunJobs();
            Assert.True(PossessionLease.IsLeased(button));
            rig.Lockdown.Deactivate();
            Assert.True(PossessionLease.IsLeased(button)); // normal end eases; it must not snap
            Assert.True(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            rig.Frames.Frame(TimeSpan.FromMilliseconds(2000)); rig.Frames.Frame(TimeSpan.FromMilliseconds(3000));
            for (int i = 0; i < 200 && (PossessionLease.IsLeased(button) || tube.Position != home); i++) await Task.Delay(25);
            Assert.False(PossessionLease.IsLeased(button));
            Assert.Equal(home, tube.Position);
            Assert.False(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
        }
        finally { warden.Stop(); AvatarTubeWindow.TestLive = null; tube.Close(); }
    });

    [Fact]
    public Task EngineRestartRearmsWardenOnlyDuringLockdown() => RunAsync(async rig =>
    {
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.LockdownWardenEnabled = true;
        CoreSettings.Current.AudioOnlySession = true;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(rig.Lockdown);
        var previous = PossessionAvaloniaHead.Warden;
        var oldStarted = CoreEngine.StartedHook;
        try
        {
            tube.Show(); AvatarTubeWindow.TestLive = tube; PossessionAvaloniaHead.Warden = warden;
            CoreEngine.StartedHook = rig.Shell.OnEngineStarted;
            rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
            Assert.True(warden.IsAvailable);
            MainShellWindow.StopEngine();
            Assert.False(warden.IsAvailable);
            CoreEngine.Start(systemInitiated: true);
            Assert.True(warden.IsAvailable);
            CoreSettings.Current.LockdownWardenEnabled = false;
            Assert.False(warden.IsAvailable);
            CoreSettings.Current.LockdownWardenEnabled = true;
            CoreSettings.Current.AvatarTubeDetached = true;
            await warden.LeaveAsync(CancellationToken.None);
            Assert.True(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            MainShellWindow.StopEngine();
            Assert.False(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            CoreEngine.Start(systemInitiated: true);
            Assert.False(warden.IsAvailable); // the appearance cooldown survives restart
            MainShellWindow.StopEngine();
            rig.Lockdown.Deactivate();
            CoreEngine.Start(systemInitiated: true);
            Assert.False(warden.IsAvailable); // a start outside Lockdown cannot rearm it
            await Task.CompletedTask;
        }
        finally
        {
            CoreEngine.Stop(); CoreEngine.StartedHook = oldStarted; warden.Stop();
            PossessionAvaloniaHead.Warden = previous; AvatarTubeWindow.TestLive = null; tube.Close();
        }
    });

}
