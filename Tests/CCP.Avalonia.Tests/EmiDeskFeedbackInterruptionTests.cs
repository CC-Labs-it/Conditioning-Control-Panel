using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskFeedbackInterruptionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // WPF EmiDeskWindow.xaml.cs:1619-1633: only the entrance is cut by the ring gesture.
    [Fact]
    public Task RightClickLetsAPatFinishWhileOpeningTheRing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var clock = new ManualClock();
        var desk = new EmiDeskWindow { PatClock = clock };
        var state = EmiState.Current;
        var (pets, petGist, opens, ringGist, streak) = (state.PetsTotal, state.PetGistGot, state.RingOpens, state.RingGistGot, state.RingIgnoreStreak);
        try
        {
            desk.Show(); Dispatcher.UIThread.RunJobs();
            Pat(desk);
            Assert.Equal("pet", Pose(desk));
            RightClick(desk);
            Assert.True(desk.RingOpen);
            Assert.True(desk.ChainLive);
            Assert.Equal("pet", Pose(desk));
            clock.Advance(899); desk.AdvancePatFeedback();
            Assert.True(desk.ChainLive); Assert.Equal("pet", Pose(desk));
            clock.Advance(1); desk.AdvancePatFeedback();
            Assert.False(desk.ChainLive); Assert.Equal("idle", Pose(desk));
        }
        finally
        {
            desk.ShutDown();
            (state.PetsTotal, state.PetGistGot, state.RingOpens, state.RingGistGot, state.RingIgnoreStreak) = (pets, petGist, opens, ringGist, streak);
        }
        return Task.CompletedTask;
    });

    // WPF xaml.cs:1627-1629 and Fx.cs:46-56: right-click completes the cut entrance once.
    [Fact]
    public Task RightClickCutsWakeAndSettlesItsPose() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var clock = new ManualClock();
        var desk = new EmiDeskWindow { PatClock = clock };
        var state = EmiState.Current;
        var (opens, gist, streak) = (state.RingOpens, state.RingGistGot, state.RingIgnoreStreak);
        int done = 0;
        try
        {
            desk.RunSummon(() => done++);
            for (int t = 0; t < 3000 && (desk.InputLocked || !desk.ChainLive); t += 20)
            {
                await Task.Delay(20);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            }
            Assert.True(desk.ChainLive); Assert.Equal("shock", Pose(desk));
            RightClick(desk);
            Assert.True(desk.RingOpen); Assert.False(desk.ChainLive);
            Assert.Equal("idle", Pose(desk)); Assert.Equal(1, done);
            clock.Advance(5000); desk.AdvancePatFeedback(); desk.FinishSummon();
            Assert.Equal(1, done);
        }
        finally
        {
            desk.ShutDown();
            (state.RingOpens, state.RingGistGot, state.RingIgnoreStreak) = (opens, gist, streak);
        }
    });

    // WPF Fx.cs:46-56 and xaml.cs:1627-1629: an ended entrance cannot cut a later pat.
    [Fact]
    public Task PanicDuringWakeDoesNotMakeALaterRightClickCancelAPat() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var desk = new EmiDeskWindow { PatClock = new ManualClock() };
        var state = EmiState.Current;
        var (pets, petGist, opens, ringGist, streak) = (state.PetsTotal, state.PetGistGot, state.RingOpens, state.RingGistGot, state.RingIgnoreStreak);
        var service = EmiDeskService.Instance;
        var field = typeof(EmiDeskService).GetField("_window", Private)!;
        var previous = field.GetValue(service);
        int done = 0;
        try
        {
            field.SetValue(service, desk);
            desk.RunSummon(() => done++);
            for (int t = 0; t < 3000 && (desk.InputLocked || !desk.ChainLive); t += 20)
            {
                await Task.Delay(20);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            }
            Assert.True(desk.ChainLive); Assert.Equal("shock", Pose(desk));
            PanicSurfaces.All.Single(surface => surface.Id == "emi-desk-feedback").Stop(null);
            Assert.False(desk.ChainLive); Assert.Equal("idle", Pose(desk));
            Pat(desk); RightClick(desk);
            Assert.True(desk.RingOpen);
            Assert.True(desk.ChainLive);
            Assert.Equal("pet", Pose(desk)); Assert.Equal(1, done);
        }
        finally
        {
            field.SetValue(service, previous); desk.ShutDown();
            (state.PetsTotal, state.PetGistGot, state.RingOpens, state.RingGistGot, state.RingIgnoreStreak) = (pets, petGist, opens, ringGist, streak);
        }
    });

    // The Avalonia panic surface cancels feedback. WPF settles a completed chain at xaml.cs:1027-1035.
    [Fact]
    public Task PanicSettlesLiveFeedbackAndLeavesAnUnrelatedPoseAlone() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var clock = new ManualClock();
        var desk = new EmiDeskWindow { PatClock = clock };
        var state = EmiState.Current;
        var (pets, gist) = (state.PetsTotal, state.PetGistGot);
        var service = EmiDeskService.Instance;
        var field = typeof(EmiDeskService).GetField("_window", Private)!;
        var previous = field.GetValue(service);
        try
        {
            field.SetValue(service, desk);
            desk.Show(); Pat(desk);
            Assert.Equal("pet", Pose(desk));
            var stop = PanicSurfaces.All.Single(surface => surface.Id == "emi-desk-feedback");
            stop.Stop(null);
            Assert.False(desk.ChainLive); Assert.Equal("idle", Pose(desk));
            clock.Advance(5000); desk.AdvancePatFeedback(); Assert.Equal("idle", Pose(desk));
            int done = 0;
            desk.PlayChain("wake", () => done++);
            Assert.Equal("shock", Pose(desk));
            stop.Stop(null);
            Assert.False(desk.ChainLive); Assert.Equal("idle", Pose(desk)); Assert.Equal(0, done);
            desk.BeginPresentation(() => { });
            desk.PresentAt(new Point(500, 400), 0, 0, "smug", "^_^");
            stop.Stop(null);
            Assert.Equal("smug", Pose(desk));
            desk.PresentAt(new Point(500, 400), 0, 0, "smug", "^_^");
            Assert.Equal("smug", Pose(desk));
        }
        finally
        {
            field.SetValue(service, previous); desk.ShutDown();
            (state.PetsTotal, state.PetGistGot) = (pets, gist);
        }
        return Task.CompletedTask;
    });

    // WPF chain cancellation and setup: EmiChainPlayer.cs:50-65.
    [Fact]
    public Task ReplacementFeedbackChangesPoseWithoutAnIdleFrame() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        var state = EmiState.Current;
        var (pets, gist) = (state.PetsTotal, state.PetGistGot);
        try
        {
            desk.Show();
            var body = desk.FindControl<Image>("BodyImage")!;
            var idle = body.Source;
            Pat(desk);
            var changes = new List<object?>();
            body.PropertyChanged += (_, e) => { if (e.Property == Image.SourceProperty) changes.Add(body.Source); };
            desk.PlayChain("wake");
            Assert.Equal("shock", Pose(desk));
            Assert.Single(changes); Assert.NotSame(idle, changes[0]);
        }
        finally { desk.ShutDown(); (state.PetsTotal, state.PetGistGot) = (pets, gist); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HidingDuringFeedbackReturnsToIdle() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        var state = EmiState.Current;
        var (pets, gist) = (state.PetsTotal, state.PetGistGot);
        try
        {
            desk.Show(); Pat(desk);
            Assert.Equal("pet", Pose(desk));
            desk.Hide();
            Assert.False(desk.ChainLive); Assert.Equal("idle", Pose(desk));
        }
        finally { desk.ShutDown(); (state.PetsTotal, state.PetGistGot) = (pets, gist); }
        return Task.CompletedTask;
    });

    // WPF xaml.cs:1515-1517,1045-1048 and Presentation.cs:45-54 reserve the show pose.
    [Fact]
    public Task PresentationIgnoresHoverAndQueuedPetTicks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        var state = EmiState.Current;
        var (pets, gist) = (state.PetsTotal, state.PetGistGot);
        try
        {
            desk.BeginPresentation(() => { });
            desk.PresentAt(new Point(500, 400), 0, 0, "smug", "^_~");
            var body = desk.FindControl<Control>("BodyRoot")!;
            var head = body.TranslatePoint(new Point(body.Bounds.Width / 2, body.Bounds.Height * .1), desk)!.Value;
            desk.MouseMove(head);
            Assert.Null(typeof(EmiDeskWindow).GetField("_petTimer", Private)!.GetValue(desk));
            typeof(EmiDeskWindow).GetMethod("OnPetTick", Private)!.Invoke(desk, new object?[] { null, EventArgs.Empty });
            Assert.False(desk.ChainLive); Assert.Equal("smug", Pose(desk)); Assert.Equal("^_~", desk.Face);
            Assert.Equal(pets, state.PetsTotal);
            desk.PresentAt(new Point(500, 400), 0, 0, "smug", "^_~");
            Assert.Equal("smug", Pose(desk)); Assert.Equal("^_~", desk.Face);
        }
        finally { desk.ShutDown(); (state.PetsTotal, state.PetGistGot) = (pets, gist); }
        return Task.CompletedTask;
    });

    // WPF xaml.cs:1619-1621: the presentation owns right-click as well as movement.
    [Fact]
    public Task PresentationIgnoresRightClick() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        var state = EmiState.Current;
        var (opens, gist, streak) = (state.RingOpens, state.RingGistGot, state.RingIgnoreStreak);
        try
        {
            desk.BeginPresentation(() => { });
            desk.PresentAt(new Point(500, 400), 0, 0, "smug", "^_~");
            RightClick(desk);
            Assert.False(desk.RingOpen); Assert.Equal("smug", Pose(desk)); Assert.Equal("^_~", desk.Face);
        }
        finally { desk.ShutDown(); (state.RingOpens, state.RingGistGot, state.RingIgnoreStreak) = (opens, gist, streak); }
        return Task.CompletedTask;
    });

    private static void Pat(EmiDeskWindow desk) => typeof(EmiDeskWindow).GetMethod("PetFromClick", Private)!.Invoke(desk, null);
    private static string Pose(EmiDeskWindow desk) => (string)typeof(EmiDeskWindow).GetField("_pose", Private)!.GetValue(desk)!;
    private static void RightClick(EmiDeskWindow desk)
    {
        Dispatcher.UIThread.RunJobs();
        var body = desk.FindControl<Control>("BodyRoot")!;
        var point = body.TranslatePoint(new Point(body.Bounds.Width / 2, body.Bounds.Height / 2), desk)!.Value;
        desk.MouseDown(point, MouseButton.Right); desk.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int ms) => _now = _now.AddMilliseconds(ms);
    }
}
