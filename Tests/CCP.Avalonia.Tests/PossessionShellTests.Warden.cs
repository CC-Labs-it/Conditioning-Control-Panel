using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Bark;
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
    // doseKeeper: the default Dose keeper starts the engine and stops it again at Lockdown end
    // (WPF LockdownDoseKeeper.cs:385); that engine stop must not cut the reassembly short.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LockdownEndReassemblesBeforeWardenReturns(bool doseKeeper) => RunAsync(async rig =>
    {
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = true;
        CoreSettings.Current.LockdownWardenEnabled = true;
        CoreSettings.Current.LockdownDoseKeeperEnabled = doseKeeper;
        CoreSettings.Current.FlashEnabled = true;
        CoreSettings.Current.BarkChatSuppressionMs = 0;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(rig.Lockdown);
        var previous = PossessionAvaloniaHead.Warden; var oldBark = BarkHead.Engine;
        var spoken = new List<string>();
        var bark = new BarkEngine(() => new BarkRuleSet(new[] { new BarkRule { Id = "warden_test",
            Trigger = PossessionBarkTriggers.Warden, VariantPool = new() { new BarkVariant("{verb}") } } }))
            { Speak = line => spoken.Add(line.Text) };
        // App constructs the warden before the director, so its handlers precede the director's.
        var endMethod = typeof(PossessionDirector).GetMethod("OnLockdownDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var endDirector = (Action)Delegate.CreateDelegate(typeof(Action), rig.Director, endMethod);
        rig.Lockdown.LockdownDeactivated -= endDirector;
        rig.Lockdown.LockdownDeactivated += endDirector;
        try
        {
            bark.Start(); BarkHead.Engine = bark;
            tube.Show(); AvatarTubeWindow.TestLive = tube; PossessionAvaloniaHead.Warden = warden;
            var home = new PixelPoint(200, 250); tube.Position = home;
            rig.Director.Warden = warden; AvApp.Possession = rig.Director;
            rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
            if (doseKeeper) { rig.Shell.EnforceLockdownDoseEngine(atActivation: true); Assert.True(CoreEngine.IsRunning); }
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
            if (doseKeeper) Assert.False(CoreEngine.IsRunning); // the keeper gave its engine back
            Assert.True(PossessionLease.IsLeased(button)); // normal end eases; it must not snap
            Assert.True(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            Assert.NotEqual(home, tube.Position);
            rig.Frames.Frame(TimeSpan.FromMilliseconds(2000)); rig.Frames.Frame(TimeSpan.FromMilliseconds(3000));
            for (int i = 0; i < 200 && (PossessionLease.IsLeased(button) || tube.Position != home || !spoken.Contains(WardenRules.Return)); i++) await Task.Delay(25);
            Assert.False(PossessionLease.IsLeased(button));
            Assert.Equal(home, tube.Position);
            Assert.False(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            Assert.Equal(new[] { WardenRules.Leave, WardenRules.Return }, spoken);
        }
        finally
        {
            warden.Stop(); CoreEngine.Stop(); BarkHead.Engine = oldBark;
            PossessionAvaloniaHead.Warden = previous; AvatarTubeWindow.TestLive = null; tube.Close();
        }
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
        static void PanicStop()
        {
            if (CoreEngine.IsRunning) MainShellWindow.StopEngine();
            PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);
        }
        try
        {
            tube.Show(); AvatarTubeWindow.TestLive = tube; PossessionAvaloniaHead.Warden = warden;
            CoreEngine.StartedHook = rig.Shell.OnEngineStarted;
            rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
            Assert.True(warden.IsAvailable);
            CoreEngine.Start(systemInitiated: true);
            MainShellWindow.StopEngine();
            Assert.True(warden.IsAvailable); // WPF StopEngineCore never touches the warden
            MainShellWindow.StopSessionFromRemote();
            Assert.True(warden.IsAvailable); // nor does WPF StopSessionFromRemote (MainWindow.RemoteControl.cs:1407)
            PanicStop();
            Assert.False(warden.IsAvailable);
            CoreEngine.Start(systemInitiated: true);
            Assert.True(warden.IsAvailable);
            CoreSettings.Current.LockdownWardenEnabled = false;
            Assert.False(warden.IsAvailable);
            CoreSettings.Current.LockdownWardenEnabled = true;
            CoreSettings.Current.AvatarTubeDetached = true;
            await warden.LeaveAsync(CancellationToken.None);
            Assert.True(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            MainShellWindow.StopSessionFromRemote();
            Assert.True(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            PanicStop();
            Assert.False(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            CoreEngine.Start(systemInitiated: true);
            Assert.False(warden.IsAvailable); // the appearance cooldown survives restart
            PanicStop();
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

    private sealed class ReassemblyGate : IPossessionEffect
    {
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Id => "test-reassembly-gate";
        public PossessionRung MinRung => PossessionRung.Settle;
        public PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
        public bool IsBig => false;
        public bool UsesFlicker => false;
        public double Weight => 1;
        public TimeSpan HoldFor => TimeSpan.Zero;
        public IReadOnlyList<PossessionRole> Roles => Array.Empty<PossessionRole>();
        public bool IsLive { get; private set; }
        public bool CanApply(PossessionContext ctx, PossessionTarget? target) => !IsLive;
        public Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
        {
            IsLive = true;
            return Task.CompletedTask;
        }
        public Task UndoAsync(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero) Finish();
            return _finished.Task;
        }
        public void Finish() { IsLive = false; _finished.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NewLockdownPreservesQueuedReassemblyAndPanicStillStops(bool panic) => RunAsync(async rig =>
    {
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarTubeDetached = true;
        CoreSettings.Current.LockdownWardenEnabled = true;
        CoreSettings.Current.LockdownDoseKeeperEnabled = false;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(rig.Lockdown);
        var previous = PossessionAvaloniaHead.Warden;
        var gate = new ReassemblyGate();
        try
        {
            tube.Show(); AvatarTubeWindow.TestLive = tube; PossessionAvaloniaHead.Warden = warden;
            var home = new PixelPoint(200, 250); tube.Position = home;
            rig.Director.Warden = warden; AvApp.Possession = rig.Director;
            rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
            await warden.LeaveAsync(CancellationToken.None);
            var leftAt = tube.Position;
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
            rig.Frames.Frame(TimeSpan.FromMilliseconds(1500)); rig.Frames.Frame(TimeSpan.FromMilliseconds(1900));
            Assert.True(PossessionLease.IsLeased(button));
            var displaced = host.BoundsOnGhostLayer(button)!.Value;
            Assert.NotEqual(bounds.X, displaced.X);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var effects = (List<IPossessionEffect>)typeof(PossessionDirector).GetField("_effects", flags)!.GetValue(rig.Director)!;
            effects.Add(gate);
            typeof(PossessionDirector).GetField("_lastReactiveAt", flags)!.SetValue(rig.Director, DateTime.MinValue);
            rig.Director.RequestReactive(gate.Id, null);
            Assert.Equal(2, rig.Director.LiveEffectCount);

            rig.Lockdown.Deactivate(); // the gate holds the newest return, leaving dodge queued
            Assert.True(gate.IsLive);
            Assert.Equal(1, rig.Director.LiveEffectCount);
            Assert.True(PossessionLease.IsLeased(button));
            rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
            Assert.True(PossessionLease.IsLeased(button));
            Assert.Equal(1, rig.Director.LiveEffectCount);
            Assert.Equal(displaced.X, host.BoundsOnGhostLayer(button)!.Value.X);
            Assert.False(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            Assert.Equal(leftAt, tube.Position); // WPF reset does not snap the tube home
            Assert.True(warden.IsAvailable); // activation clears the previous appearance cooldown

            if (panic)
            {
                PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);
                Assert.False(PossessionLease.IsLeased(button));
                Assert.Equal(0, rig.Director.LiveEffectCount);
                Assert.Equal(home, tube.Position);
            }
            gate.Finish();
            for (int i = 0; i < 100 && rig.Director.LiveEffectCount != 0; i++) await Task.Delay(25);
            Assert.Equal(0, rig.Director.LiveEffectCount);
            if (!panic)
            {
                Assert.True(PossessionLease.IsLeased(button));
                rig.Frames.Frame(TimeSpan.FromMilliseconds(2000)); rig.Frames.Frame(TimeSpan.FromMilliseconds(2400));
                Assert.True(PossessionLease.IsLeased(button));
                rig.Frames.Frame(TimeSpan.FromMilliseconds(3000));
                for (int i = 0; i < 100 && PossessionLease.IsLeased(button); i++) await Task.Delay(25);
                Assert.False(PossessionLease.IsLeased(button));
                Assert.Equal(leftAt, tube.Position); // the old reassembly cannot return a new lockdown's warden
                Assert.False(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            }
            Assert.Equal(bounds.X, host.BoundsOnGhostLayer(button)!.Value.X, 3);
        }
        finally { gate.Finish(); warden.Stop(); PossessionAvaloniaHead.Warden = previous; AvatarTubeWindow.TestLive = null; tube.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NewLockdownCancelsAnInFlightLeaveWithoutLateNote(bool panic) => RunAsync(async rig =>
    {
        CoreSettings.Current.AvatarEnabled = true; CoreSettings.Current.AvatarTubeDetached = true;
        CoreSettings.Current.LockdownWardenEnabled = true;
        CoreSettings.Current.LockdownDoseKeeperEnabled = false;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = true };
        using var warden = new Warden(rig.Lockdown);
        var previous = PossessionAvaloniaHead.Warden; var gate = new ReassemblyGate();
        try
        {
            tube.Show(); AvatarTubeWindow.TestLive = tube; PossessionAvaloniaHead.Warden = warden;
            var home = new PixelPoint(200, 250); tube.Position = home;
            rig.Director.Warden = warden; AvApp.Possession = rig.Director;
            rig.Lockdown.Activate(TimeSpan.FromMinutes(30)); Frame();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var effects = (List<IPossessionEffect>)typeof(PossessionDirector).GetField("_effects", flags)!.GetValue(rig.Director)!;
            effects.Add(gate);
            rig.Director.RequestReactive(gate.Id, null);
            Assert.Equal(1, rig.Director.LiveEffectCount);
            var leave = warden.LeaveAsync(CancellationToken.None);
            await Task.Delay(90);
            var before = tube.Position; Assert.NotEqual(home, before);
            rig.Lockdown.Deactivate(); // gate holds reassembly before it can return the tube
            rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
            await leave;
            Assert.Equal(before, tube.Position);
            Assert.True(warden.IsAvailable);
            Assert.False(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
            if (panic) PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);
            gate.Finish();
            await Task.Delay(500);
            Assert.Equal(panic ? home : before, tube.Position);
            Assert.False(tube.FindControl<Border>("PossessionNoteCard")!.IsVisible);
        }
        finally { gate.Finish(); warden.Stop(); PossessionAvaloniaHead.Warden = previous; AvatarTubeWindow.TestLive = null; tube.Close(); }
    });

}
