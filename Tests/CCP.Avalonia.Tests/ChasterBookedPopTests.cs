using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The big "+3:00" at the cause (WPF Controls/ChasterBookedPop.cs + MainWindow.Chaster.cs:84-158):
/// pops where the time was earned, coalesces, ends on its own clock, falls back to the rail where the
/// platform has no click-through window, and a full tab pops the muted tag once per throttle.
/// No network: ChasterBillTests.Run fakes Chaster. No real windowing: Place is a seam.</summary>
public sealed class ChasterBookedPopTests
{
    private static readonly ScreenPoint Bubble = new(600, 400);

    private static Task WithSeams(bool placeable, Func<ChasterService, MainShellWindow, FakeFrames, SteppedTime, Task> body) =>
        AvaloniaTestDispatcher.RunAsync(() => ChasterBillTests.Run(async (chaster, shell) =>
        {
            var (place, cursor, time, frames) = (ChasterBookedPop.Place, ChasterBookedPop.Cursor, ChasterBookedPop.Time, ChasterBookedPop.Frames);
            var (fakeFrames, fakeTime) = (new FakeFrames(), new SteppedTime());
            ChasterBookedPop.Place = (_, _) => placeable;
            ChasterBookedPop.Cursor = () => null;
            ChasterBookedPop.Time = fakeTime;
            ChasterBookedPop.Frames = fakeFrames;
            try
            {
                shell.InitializeChasterFlash(chaster);
                await body(chaster, shell, fakeFrames, fakeTime);
            }
            finally
            {
                ChasterBookedPop.CloseAll();
                (ChasterBookedPop.Place, ChasterBookedPop.Cursor, ChasterBookedPop.Time, ChasterBookedPop.Frames) = (place, cursor, time, frames);
            }
        }));

    private static int RailFlashes(MainShellWindow shell) =>
        AdornerLayer.GetAdornerLayer(shell.Named<ChasterRailChip>("ChasterRail")!)!.Children.OfType<ChasterBookedFlash>().Count();

    [Fact]
    public Task ABookingPopsAtItsCauseCoalescesAndEndsOnItsClock() => WithSeams(true, async (chaster, shell, frames, time) =>
    {
        var first = chaster.NoteAt("attention", Bubble);
        Dispatcher.UIThread.RunJobs();
        var pop = ChasterBookedPop.Live.Single();
        Assert.Equal(Bubble, pop.Origin);
        Assert.Equal(CircesTab.Format(first.AppliedSeconds), pop.Stage.Text);
        Assert.Equal(0, RailFlashes(shell));               // the pop replaced the rail figure, as WPF
        Assert.Equal(1, frames.Count);                     // one clock while it lives

        var second = chaster.NoteAt("typo", Bubble);       // inside the coalescing window: same pop, bigger number
        Dispatcher.UIThread.RunJobs();
        Assert.Same(pop, ChasterBookedPop.Live.Single());
        Assert.Equal(CircesTab.Format(first.AppliedSeconds + second.AppliedSeconds), pop.Stage.Text);

        time.Advance(TimeSpan.FromMilliseconds(pop.Plan.TotalMs + 1));
        frames.Frame(TimeSpan.FromSeconds(5));
        Assert.True(pop.Gone);                             // its life closes it, and the clock parks
        Assert.Empty(ChasterBookedPop.Live);
        Assert.Equal(0, frames.Count);
        await Task.CompletedTask;
    });

    [Fact]
    public Task WithoutAClickThroughWindowTheRailTakesIt() => WithSeams(false, async (chaster, shell, _, _) =>
    {
        chaster.NoteAt("attention", Bubble);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(ChasterBookedPop.Live);
        Assert.Equal(1, RailFlashes(shell));
        await Task.CompletedTask;
    });

    [Fact]
    public Task PopsNearALivePopStackAbove() => WithSeams(true, async (_, shell, _, _) =>
    {
        var look = new BookedFlashPlan.Plan("+5:00", BookedFlashPlan.AddColour, 0, 1500, true);
        var a = ChasterBookedPop.Show(shell, Bubble, look, BookedPopLayout.For(MotionLevel.Full))!;
        var b = ChasterBookedPop.Show(shell, new ScreenPoint(630, 410), look, BookedPopLayout.For(MotionLevel.Full))!;
        var far = ChasterBookedPop.Show(shell, new ScreenPoint(1500, 900), look, BookedPopLayout.For(MotionLevel.Full))!;
        Assert.Equal((0, 1, 0), (a.Slot, b.Slot, far.Slot));
        await Task.CompletedTask;
    });

    [Fact]
    public Task AFullTabPopsTheMutedTagOncePerThrottle() => WithSeams(false, async (chaster, shell, _, _) =>
    {
        CoreSettings.Current.ChasterPrices = new() { NatashasFavourite.EventId };
        TabBooking last = default;
        for (var i = 0; i < 200 && (last = chaster.NoteAt(NatashasFavourite.EventId, Bubble)).Booked; i++) { }
        Assert.Equal(TabRefusal.DailyCap, last.Refusal);
        Dispatcher.UIThread.RunJobs();                     // the fill went to the rail (no window); the refusal had nowhere either
        ChasterBookedPop.CloseAll();

        ChasterBookedPop.Place = (_, _) => true;
        chaster.NoteAt(NatashasFavourite.EventId, Bubble);
        chaster.NoteAt(NatashasFavourite.EventId, Bubble); // inside the 2 s throttle: no second tag
        Dispatcher.UIThread.RunJobs();
        var tag = ChasterBookedPop.Live.Single();
        Assert.Equal(Loc.Get(CapNotice.TextKey(TabRefusal.DailyCap)), tag.Stage.Text);
        Assert.Null(tag.Stage.Source);
        Assert.Equal(CapNotice.Pop(AmbientFxCanvas.Env.Level), tag.Plan);
        await Task.CompletedTask;
    });

    [Fact]
    public Task InvalidCauseUsesTheCursorAndPanicClosesEveryPop() => WithSeams(true, async (chaster, shell, frames, _) =>
    {
        ChasterBookedPop.Cursor = () => Bubble;
        chaster.NoteAt("attention", new ScreenPoint(double.NaN, 400));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Bubble, ChasterBookedPop.Live.Single().Origin);
        var stop = Assert.Single(PanicSurfaces.All, s => s.Id == "chaster-booked-pop");
        stop.Stop(shell);
        Assert.Empty(ChasterBookedPop.Live);
        Assert.Equal(0, frames.Count);
        stop.Stop(shell); // idle cleanup is safe
        await Task.CompletedTask;
    });

    [Fact]
    public Task ClosingTheShellClosesPopsAndUnsubscribes() => WithSeams(true, async (chaster, shell, frames, _) =>
    {
        chaster.NoteAt("attention", Bubble);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(ChasterBookedPop.Live);
        shell.Close();
        Assert.Empty(ChasterBookedPop.Live);
        Assert.Equal(0, frames.Count);
        chaster.NoteAt("typo", Bubble);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(ChasterBookedPop.Live);
        await Task.CompletedTask;
    });

    [Fact]
    public Task ShowingReassertsTopmostAfterPlacement() => WithSeams(true, async (_, shell, _, _) =>
    {
        var previous = ChasterBookedPop.Raise;
        var raised = 0;
        ChasterBookedPop.Raise = w => { Assert.True(w.IsVisible); raised++; return true; };
        try
        {
            var look = new BookedFlashPlan.Plan("+5:00", BookedFlashPlan.AddColour, 0, 1500, true);
            Assert.NotNull(ChasterBookedPop.Show(shell, Bubble, look, BookedPopLayout.For(MotionLevel.Full)));
            Assert.Equal(1, raised);
        }
        finally { ChasterBookedPop.Raise = previous; }
        await Task.CompletedTask;
    });

    [Fact]
    public void MotionFollowsWpfsTimings()
    {
        var full = BookedPopLayout.For(MotionLevel.Full);
        Assert.Equal(0.2, ChasterBookedPop.At(full, 0, null).Scale, 3);          // BackEase scale-in from 0.2
        Assert.True(ChasterBookedPop.At(full, full.ScaleInMs * 0.6, null).Scale > 1);   // overshoot
        Assert.Equal(1, ChasterBookedPop.At(full, full.ScaleInMs, null).Scale, 3);
        Assert.Equal(1.18, ChasterBookedPop.At(full, 800, 0).Scale, 3);          // Retitle re-punch
        Assert.Equal(1, ChasterBookedPop.At(full, 50, 200).Scale, 3);            // a punch inside the scale-in holds 1.0 after it ends
        Assert.Equal(-full.RiseDip, ChasterBookedPop.At(full, full.TotalMs, null).Rise, 3);
        Assert.Equal(1, ChasterBookedPop.At(full, full.TotalMs - full.FadeMs, null).Opacity, 3);
        Assert.Equal(0.5, ChasterBookedPop.At(full, full.TotalMs - full.FadeMs / 2.0, null).Opacity, 3);

        var reduced = BookedPopLayout.For(MotionLevel.Reduced);                  // no motion, the number holds and fades
        Assert.Equal((1d, 0d, 1d), (ChasterBookedPop.At(reduced, 0, 0).Scale, ChasterBookedPop.At(reduced, 900, null).Rise, ChasterBookedPop.At(reduced, 0, null).Opacity));
        Assert.Equal(0.5, ChasterBookedPop.At(reduced, reduced.TotalMs - reduced.FadeMs / 2.0, null).Opacity, 3);

        var off = BookedPopLayout.For(MotionLevel.Off);                          // there, then gone
        Assert.Equal(1, ChasterBookedPop.At(off, off.TotalMs - 1, null).Opacity, 3);
    }
}
