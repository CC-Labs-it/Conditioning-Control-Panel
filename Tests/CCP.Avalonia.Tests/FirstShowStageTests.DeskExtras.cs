using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using Xunit;

namespace CCP.Avalonia.Tests;

// Restack b190 (#3780, #4399): the welcome show takes the desk EMI over and hands her back clean.
public sealed partial class FirstShowStageTests
{
    private const BindingFlags DeskPrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    // #3780, WPF Presentation.cs:71 -> Alive.cs:162 resets the poke ladder when the show ends.
    // Use the service's real replay path, with a short show inside the four-second poke window.
    [Fact]
    public Task ReplayReturnsHerWithAFreshPokeLadder() => Run(stage =>
    {
        var emi = EmiDeskService.Instance.Window!;
        var clock = new PresentationPatClock();
        emi.PatClock = clock;
        var pat = typeof(EmiDeskWindow).GetMethod("PetFromClick", DeskPrivate)!;
        void Pat() => pat.Invoke(emi, null);
        void FinishPat(int ms) { clock.Advance(ms); emi.AdvancePatFeedback(); }

        Assert.True(FirstShowService.Stop());
        var state = EmiState.Current;
        int pets = state.PetsTotal;
        bool gist = state.PetGistGot;
        // Start inside the existing cooldown so the ladder is exercised without recording a pet
        // or scheduling an EMI state save. A six-second cooldown matches WPF React.cs:115-118/153-170.
        typeof(EmiDeskWindow).GetField("_petCooldownUntil", DeskPrivate)!
            .SetValue(emi, clock.GetUtcNow().UtcDateTime.AddSeconds(6));
        try
        {
            Pat();
            FinishPat(1000);
            Pat();
            Assert.Equal("^_~", emi.Face);
            FinishPat(1000);

            Assert.True(FirstShowService.Open());
            Assert.True(emi.PresentationActive);
            Assert.True(FirstShowService.Stop());
            Assert.False(emi.PresentationActive);
            Pat();                     // no inherited third-touch glee
            Assert.Equal("^_~", emi.Face);
            FinishPat(300);
            Assert.Equal("^_~", emi.Face);
            Assert.Equal(pets, state.PetsTotal);
            Assert.Equal(gist, state.PetGistGot);
            return Task.CompletedTask;
        }
        finally
        {
            emi.PatClock = TimeProvider.System;
            if (state.PetsTotal != pets || state.PetGistGot != gist)
            {
                state.PetsTotal = pets;
                state.PetGistGot = gist;
                EmiState.SaveNow(); // any queued save will also see the restored state
            }
        }
    });

    // #4399, WPF Presentation.cs:25-27 stops the alive lifecycle and ends a drag.
    [Fact]
    public Task PresentationStopsAliveAndGestureHolds() => Run(stage =>
    {
        var emi = EmiDeskService.Instance.Window!;
        Assert.True(FirstShowService.Stop());
        var type = typeof(EmiDeskWindow);
        var ladder = (EmiPokeLadder)type.GetField("_patPokes", DeskPrivate)!.GetValue(emi)!;
        ladder.Note(DateTime.UtcNow);
        var chrome = (EmiChromeHover)type.GetField("_chrome", DeskPrivate)!.GetValue(emi)!;
        chrome.Hold(EmiChromeHold.Drag, true, DateTime.UtcNow);
        type.GetField("_dragging", DeskPrivate)!.SetValue(emi, true);

        Assert.True(FirstShowService.Open());
        Assert.Equal(0, ladder.Count);
        Assert.Equal(EmiChromeHold.None, chrome.Holds & EmiChromeHold.Drag);
        Assert.False((bool)type.GetField("_dragging", DeskPrivate)!.GetValue(emi)!);
        return Task.CompletedTask;
    });

    // #4399, WPF Presentation.cs:26 releases BodyRoot's capture when the show takes over; a corner
    // drag keeps its grip there (EmiDeskPresentationTests.BorrowingKeepsAResizeGripDragsCapture).
    [Fact]
    public Task PresentationReleasesCapturedDrag() => Run(stage =>
    {
        var emi = EmiDeskService.Instance.Window!;
        Assert.True(FirstShowService.Stop());
        var control = (Control)typeof(EmiDeskWindow).GetField("_bodyRoot", DeskPrivate)!.GetValue(emi)!;
        IPointer? pointer = null;
        emi.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Tunnel);
        Dispatcher.UIThread.RunJobs();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), emi)!.Value;
        emi.MouseDown(point, MouseButton.Left);
        Assert.NotNull(pointer);
        Assert.Same(control, pointer!.Captured);
        Assert.True(FirstShowService.Open());
        Assert.Null(pointer.Captured);
        emi.MouseUp(point, MouseButton.Left);
        Assert.True(FirstShowService.Stop());
        Assert.False(emi.PresentationActive);
        return Task.CompletedTask;
    });

    // #4399, WPF Presentation.cs:25 stops the alive lifecycle before the show draws her face.
    [Fact]
    public Task PresentationStopsPendingPatFeedback() => Run(stage =>
    {
        var emi = EmiDeskService.Instance.Window!;
        Assert.True(FirstShowService.Stop());
        var clock = new PresentationPatClock();
        emi.PatClock = clock;
        try
        {
            var type = typeof(EmiDeskWindow);
            type.GetMethod("StartPatFeedback", DeskPrivate)!.Invoke(emi, new object?[] { EmiPatFeedback.Flick, "pet", null });
            var timer = (DispatcherTimer)type.GetField("_patFeedbackTimer", DeskPrivate)!.GetValue(emi)!;
            Assert.True(timer.IsEnabled);
            Assert.Equal("^_~", emi.Face);
            Assert.True(FirstShowService.Open());
            Assert.False(timer.IsEnabled);
            Assert.Null(type.GetField("_patFeedbackTimer", DeskPrivate)!.GetValue(emi));
            var showFace = emi.Face;
            clock.Advance(350);
            type.GetMethod("OnPatFeedbackTick", DeskPrivate)!.Invoke(emi, new object?[] { timer, EventArgs.Empty });
            Assert.Equal(showFace, emi.Face);
        }
        finally { emi.PatClock = TimeProvider.System; }
        return Task.CompletedTask;
    });

    private sealed class PresentationPatClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int ms) => _now += TimeSpan.FromMilliseconds(ms);
    }
}
