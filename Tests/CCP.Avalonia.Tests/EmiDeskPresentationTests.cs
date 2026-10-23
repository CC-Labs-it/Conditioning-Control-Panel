using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The real welcome-show presenter must own her body until it hands her back.
/// WPF Presentation.cs and Fx.cs:431 guard delayed FX by the presentation epoch.</summary>
public sealed class EmiDeskPresentationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(EmiDeskWindow desk, string name) =>
        (T)typeof(EmiDeskWindow).GetField(name, Private)!.GetValue(desk)!;

    [Fact]
    public Task BorrowingReleasesTheDragAndRestoresPlacementAndControls() => Run(async desk =>
    {
        var body = desk.FindControl<Grid>("BodyRoot")!;
        var point = BodyPoint(desk, body);
        IPointer? pointer = null;
        body.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        desk.MouseDown(point, MouseButton.Left);
        desk.MouseMove(point + new Point(40, 0));
        Assert.Same(body, pointer!.Captured);
        var home = desk.Position;
        bool moved = false;
        desk.Moved += (_, _) => moved = true;
        var saved = (EmiState.Current.WinLeftPx, EmiState.Current.WinTopPx);
        bool stopped = false;
        desk.BeginPresentation(() => stopped = true);
        Assert.Null(pointer.Captured);
        Assert.False(Field<bool>(desk, "_dragging"));
        Assert.False(Field<bool>(desk, "_wobbleLive"));
        Assert.False(desk.FindControl<Button>("BtnGear")!.IsVisible);
        Assert.False(desk.FindControl<Button>("BtnHelp")!.IsVisible);
        Assert.True(desk.FindControl<Button>("BtnClose")!.IsVisible);
        Assert.True(desk.FindControl<Border>("ResizeGrip")!.IsVisible);
        desk.ApplyBodyWidth(280);
        desk.PresentAt(new Point(500, 400), 7, 9, "celebration", "^w^");
        Assert.Equal("^w^", desk.Face);
        Assert.Equal("celebration", Field<string>(desk, "_pose"));
        Assert.Equal(7, Field<TranslateTransform>(desk, "_moveShift").Y);
        Assert.Equal(9, Field<RotateTransform>(desk, "_wobbleRotate").Angle);
        desk.SavePlacement();
        Assert.Equal(saved, (EmiState.Current.WinLeftPx, EmiState.Current.WinTopPx));
        desk.StopPresentation();
        Assert.True(stopped);
        desk.EndPresentation();
        Assert.False(desk.PresentationActive);
        Assert.Equal(home, desk.Position);
        Assert.Equal(280, desk.BodyWidth);
        Assert.True(desk.FindControl<Button>("BtnGear")!.IsVisible);
        Assert.True(desk.FindControl<Button>("BtnHelp")!.IsVisible);
        Assert.Equal(0, Field<TranslateTransform>(desk, "_moveShift").Y);
        Assert.Equal(0, Field<RotateTransform>(desk, "_wobbleRotate").Angle);
        Assert.Equal("^_^", desk.Face);
        // Releasing the old press after the hand-off cannot count as a drop or a pat.
        desk.MouseUp(point, MouseButton.Left);
        Assert.False(moved);
        Assert.Null(Field<ITimer?>(desk, "_landFace"));
        Assert.Null(Field<CancellationTokenSource?>(desk, "_squashAnim"));
        Assert.Equal(saved, (EmiState.Current.WinLeftPx, EmiState.Current.WinTopPx));
        await Task.CompletedTask;
    });

    [Fact]
    public Task PendingDropFaceCannotOverwriteThePresenter() => Run(desk =>
    {
        var clock = new ManualClock();
        EmiDeskWindow.TossClock = clock;
        var point = BodyPoint(desk, desk.FindControl<Grid>("BodyRoot")!);
        desk.MouseDown(point, MouseButton.Left);
        desk.MouseMove(point + new Point(40, 0));
        desk.MouseUp(point + new Point(40, 0), MouseButton.Left);
        Assert.Equal("^_^", desk.Face);
        desk.BeginPresentation(() => { });
        desk.PresentAt(new Point(500, 400), 0, 0, "idle", "^w^");
        clock.Fire();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("^w^", desk.Face);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PendingSummonCannotOverwriteThePresenter() => Run(desk =>
    {
        desk.RunSummon();
        desk.BeginPresentation(() => { });
        desk.PresentAt(new Point(500, 400), 0, 0, "idle", "^w^");
        Clock.Advance(850);   // past both WPF smoke (380) and CRT (220 + 20) steps
        Assert.Equal("^w^", desk.Face);
        Assert.True(desk.FindControl<Grid>("BodyRoot")!.IsVisible);
        AssertFullSize(desk);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PendingDismissCannotHideHerDuringOrAfterTheShow(bool endShow) => Run(desk =>
    {
        desk.RunDismiss();
        desk.BeginPresentation(() => { });
        if (endShow) desk.EndPresentation();
        Clock.Advance(1400);   // WPF CRT-off (230), then particle lifetime (900)
        Assert.True(desk.IsVisible);
        AssertFullSize(desk);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EndingDuringEntranceCancelsItsDelayedSteps() => Run(desk =>
    {
        desk.BeginPresentation(() => { });
        desk.RunPresentationEntrance();
        Assert.True(desk.PresentationArriving);
        desk.EndPresentation();
        Clock.Advance(850);
        Assert.False(desk.PresentationArriving);
        Assert.False(desk.InputLocked);
        Assert.Equal("^_^", desk.Face);
        AssertFullSize(desk);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ThePresentationsOwnEntranceCompletesAndAcceptsFrames() => Run(desk =>
    {
        desk.BeginPresentation(() => { });
        desk.RunPresentationEntrance();
        Assert.True(desk.PresentationArriving);
        desk.PresentAt(new Point(500, 400), 5, 8, "celebration", "^w^");
        Assert.NotEqual("^w^", desk.Face);   // the entrance owns pose/face, but position follows the stage
        Clock.Advance(379);
        Assert.False(desk.FindControl<Grid>("BodyRoot")!.IsVisible);   // still the smoke lead
        Clock.Advance(850);
        Assert.False(desk.PresentationArriving);
        Assert.False(desk.InputLocked);
        Assert.True(desk.FindControl<Grid>("BodyRoot")!.IsVisible);
        // The CRT power-on tween itself runs on real dispatcher ticks, so its end size is not asserted here.
        desk.PresentAt(new Point(500, 400), 5, 8, "celebration", "^w^");
        Assert.Equal("^w^", desk.Face);
        Assert.Equal("celebration", Field<string>(desk, "_pose"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task MotionOffClearsTheArrivalGate() => Run(desk =>
    {
        desk.BeginPresentation(() => { });
        desk.RunPresentationEntrance();
        Assert.True(desk.PresentationArriving);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        desk.RunPresentationEntrance();
        Assert.False(desk.PresentationArriving);
        return Task.CompletedTask;
    });

    [Fact]
    public Task RightClickCannotOpenTheRingDuringPresentation() => Run(desk =>
    {
        desk.BeginPresentation(() => { });
        var point = BodyPoint(desk, desk.FindControl<Grid>("BodyRoot")!);
        desk.MouseDown(point, MouseButton.Right);
        desk.MouseUp(point, MouseButton.Right);
        Assert.False(desk.RingOpen);
        return Task.CompletedTask;
    });

    [Fact]
    public Task HoverCannotArmAPatDuringPresentation() => Run(desk =>
    {
        desk.BeginPresentation(() => { });
        var body = desk.FindControl<Grid>("BodyRoot")!;
        var head = body.TranslatePoint(new Point(body.Bounds.Width / 2, 10), desk)!.Value;
        desk.MouseMove(head);
        Assert.Null(Field<DispatcherTimer?>(desk, "_petTimer"));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("BtnGear")]
    [InlineData("BtnHelp")]
    public Task PresentationIgnoresPanelShortcuts(string button) => Run(desk =>
    {
        desk.BeginPresentation(() => { });
        // WPF guards these handlers as well as hiding their controls.
        desk.FindControl<Button>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(desk.OptionsOpen);
        Assert.Null(Field<EmiBookWindow?>(desk, "_book"));
        return Task.CompletedTask;
    });

    private static Point BodyPoint(EmiDeskWindow desk, Control body) =>
        body.TranslatePoint(new Point(body.Bounds.Width / 2, body.Bounds.Height / 2), desk)!.Value;

    private static void AssertFullSize(EmiDeskWindow desk)
    {
        var scale = Field<ScaleTransform>(desk, "_crtScale");
        Assert.Equal(1, scale.ScaleX);
        Assert.Equal(1, scale.ScaleY);
    }

    private static Task Run(Func<EmiDeskWindow, Task> test) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldMotion = CoreSettings.Current.MotionLevel;
        var oldVolume = CoreSettings.Current.MasterVolume;
        var oldWidth = CoreSettings.Current.EmiDeskWidth;
        var state = EmiState.Current;
        var home = (state.WinLeftPx, state.WinTopPx, state.Monitor);
        Clock = new SteppedClock();
        EmiDeskWindow.FxClock = Clock;
        var desk = new EmiDeskWindow();
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            CoreSettings.Current.MasterVolume = 0;
            desk.Show();
            Dispatcher.UIThread.RunJobs();
            await test(desk);
        }
        finally
        {
            desk.EndPresentation();
            desk.ShutDown();
            EmiDeskWindow.TossClock = TimeProvider.System;
            EmiDeskWindow.FxClock = TimeProvider.System;
            CoreSettings.Current.MotionLevel = oldMotion;
            CoreSettings.Current.MasterVolume = oldVolume;
            CoreSettings.Current.EmiDeskWidth = oldWidth;
            (state.WinLeftPx, state.WinTopPx, state.Monitor) = home;
        }
    });

    private static SteppedClock Clock = new();

    /// <summary>Fires due FX timers in time order and drains each step's dispatcher post before the next.</summary>
    private sealed class SteppedClock : TimeProvider
    {
        private readonly System.Collections.Generic.List<(long Due, TimerCallback Callback, object? State)> _due = new();
        private long _nowMs;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(_nowMs);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _due.Add((_nowMs + (long)dueTime.TotalMilliseconds, callback, state));
            return new ManualClock.NoTimer();
        }
        public void Advance(long ms)
        {
            long end = _nowMs + ms;
            Dispatcher.UIThread.RunJobs();
            while (true)
            {
                int next = -1;
                for (int i = 0; i < _due.Count; i++)
                    if (_due[i].Due <= end && (next < 0 || _due[i].Due < _due[next].Due)) next = i;
                if (next < 0) break;
                var t = _due[next];
                _due.RemoveAt(next);
                _nowMs = Math.Max(_nowMs, t.Due);
                t.Callback(t.State);
                Dispatcher.UIThread.RunJobs();
            }
            _nowMs = end;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            (_callback, _state) = (callback, state);
            return new NoTimer();
        }
        // Deliberately deliver even a disposed timer: an already-posted callback must be harmless too.
        public void Fire() => _callback?.Invoke(_state);
        internal sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
