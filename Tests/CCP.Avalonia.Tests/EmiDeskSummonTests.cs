using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EmiDock.OnChipClick -> EmiDeskService.Toggle: the dock chip summons her, the chip
/// again sends her away through the outro, and a switched-off feature never summons.</summary>
public sealed class EmiDeskSummonTests
{
    [Fact]
    public Task DockChipTogglesHerThroughTheService() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var svc = EmiDeskService.Instance;
        try
        {
            CoreSettings.Current.EmiDeskMuteAvatar = false;   // no modal on the way out
            var dock = new EmiDock();
            var host = new Window { Width = 200, Height = 200, Content = dock };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var chip = dock.FindControl<Button>("BtnChip")!;
            void Click() { chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
            async Task Pump(int ms)
            {
                for (int t = 0; t < ms; t += 20)
                {
                    await Task.Delay(20);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                }
            }

            CoreSettings.Current.EmiDeskEnabled = false;
            Click();
            Assert.False(svc.IsOut);
            Assert.Null(svc.Window);

            CoreSettings.Current.EmiDeskEnabled = true;
            bool? heard = null;
            EventHandler<bool> onOut = (_, o) => heard = o;
            svc.OutChanged += onOut;
            Click();
            await Pump(100);
            Assert.True(svc.IsOut);
            Assert.True(svc.Window!.IsVisible);
            Assert.True(heard);

            Click();
            Assert.True(svc.Window!.ChainLive); // the wink precedes the CRT collapse
            await Pump(3000); // WPF wink + CRT off + burst sweep
            Assert.False(svc.IsOut);
            Assert.False(svc.Window!.IsVisible);
            Assert.False(heard);
            svc.OutChanged -= onOut;
            host.Close();
        }
        finally
        {
            svc.Window?.ShutDown();
            service.SaveImmediate(); CoreSettings.ServiceProvider = oldSettings;
        }
    });

    /// <summary>Headless app + fresh settings with a talking feature on and the mute setting on,
    /// so the prompt is asked; the prompt itself is <paramref name="ask"/>.</summary>
    private static Task Muted(Func<Task<EmiMuteChoice>> ask, Func<EmiDeskService, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var svc = EmiDeskService.Instance;
        var oldAsk = svc.AskMute;
        try
        {
            var s = CoreSettings.Current;
            s.EmiDeskEnabled = true;
            s.EmiDeskMuteAvatar = true;
            s.EmiDeskMuteDontAsk = false;
            s.AiChatEnabled = true;
            svc.ResetMutePrompt();
            svc.AskMute = ask;
            await body(svc);
        }
        finally
        {
            svc.AskMute = oldAsk;
            svc.ResetMutePrompt();
            svc.Window?.ShutDown();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate(); CoreSettings.ServiceProvider = oldSettings;
        }
    });

    private static async Task Pump(int ms)
    {
        for (int t = 0; t < ms; t += 20)
        {
            await Task.Delay(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>WPF ShowGiggle (Speech.cs:461): with EMI out and the mute accepted, the tube does not speak.</summary>
    [Fact]
    public Task MuteAnswerSilencesTheTube() => Muted(() => Task.FromResult(EmiMuteChoice.Mute), async svc =>
    {
        var main = new Window { Width = 800, Height = 600 };
        main.Show();
        var tube = new AvatarTubeWindow(main);
        try
        {
            tube.GigglePriority("before", playSound: false);
            Assert.True(tube.IsSpeaking);   // control: she speaks while EMI is away

            await svc.Summon();
            Assert.True(svc.AvatarMuted);
            tube.GigglePriority("during", playSound: false);
            Assert.False(tube.IsSpeaking);
        }
        finally { tube.Close(); main.Close(); }
    });

    /// <summary>WPF :295 re-entry trap: a second summon while the prompt is up does not ask twice,
    /// a dismiss during it abandons the summon, and the prompt is asked once per session.</summary>
    [Fact]
    public Task MutePromptReentryIsGuarded() => AskedOnce(async (svc, asks, answer) =>
    {
        var first = svc.Summon();
        Assert.Equal(1, asks());
        await svc.Summon();          // chip again while the prompt is up
        Assert.Equal(1, asks());
        svc.Dismiss();               // the x while the prompt is up
        answer(EmiMuteChoice.Mute);
        await first;
        await Pump(3000);
        Assert.False(svc.Window!.IsVisible);
        Assert.False(svc.IsOut);

        await svc.Summon();          // same session: not asked again
        Assert.Equal(1, asks());
        Assert.True(svc.Window!.IsVisible);
    });

    private static Task AskedOnce(Func<EmiDeskService, Func<int>, Action<EmiMuteChoice>, Task> body)
    {
        int n = 0;
        var tcs = new TaskCompletionSource<EmiMuteChoice>();
        return Muted(() => { n++; return tcs.Task; }, svc => body(svc, () => n, c => tcs.TrySetResult(c)));
    }

    /// <summary>WPF EmiDeskSettings :123: switching the feature off sends her away.</summary>
    [Fact]
    public Task SettingsSwitchOffDismissesHer() => Muted(() => Task.FromResult(EmiMuteChoice.Keep), async svc =>
    {
        var section = new EmiDeskSettingsSection();
        var host = new Window { Width = 600, Height = 600, Content = section };
        host.Show();
        section.SyncFromSettings();
        Dispatcher.UIThread.RunJobs();
        try
        {
            await svc.Summon();
            Assert.True(svc.IsOut);
            section.FindControl<CheckBox>("ChkEnabled")!.IsChecked = false;
            await Pump(3000);
            Assert.False(svc.IsOut);
            Assert.False(svc.Window!.IsVisible);
        }
        finally { host.Close(); }
    });

    [Fact]
    public Task DismissBeforeSmokeEndsCannotRestartWake() => Muted(() => Task.FromResult(EmiMuteChoice.Keep), async _ =>
    {
        var win = new EmiDeskWindow();
        try
        {
            win.Show();
            win.RunSummon();
            win.RunDismiss();
            await Pump(800);
            Assert.False((bool)typeof(EmiDeskWindow).GetField("_summonChainLive",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(win)!);
        }
        finally { win.ShutDown(); }
    });

    // WPF Fx.cs:143-202 completes dismissal; MainWindow.xaml.cs:1934-2013 leaves it running.
    [Theory]
    [InlineData(0)]
    [InlineData(1000)] // mid-wink (the base wink is 1280 ms); 1500 raced the base CRT-off step
    public Task PanicCompletesDismissalExactlyOnce(int delay) => Muted(() => Task.FromResult(EmiMuteChoice.Keep), async svc =>
    {
        await svc.Summon();
        var win = svc.Window!;
        int dismissed = 0;
        EventHandler<bool> changed = (_, isOut) => { if (!isOut) dismissed++; };
        svc.OutChanged += changed;
        try
        {
            svc.Dismiss();
            await Pump(delay);
            Assert.True(win.InputLocked);
            var entry = Assert.Single(ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All, s => s.Id == "emi-desk-feedback");
            entry.Stop(null);
            Assert.False(win.ChainLive);
            Assert.False(win.IsVisible);
            Assert.False(win.InputLocked);
            Assert.False(svc.IsOut);
            entry.Stop(null);
            await Pump(3000); // deferred FX and duplicate panic cannot complete it twice
            Assert.Equal(1, dismissed);
        }
        finally { svc.OutChanged -= changed; }
    });

    // WPF React.cs:132-138 cuts wake, completes summon, then processes this same click as a pat.
    [Fact]
    public Task LeftClickDuringWakeCompletesSummonAndPats() => Muted(() => Task.FromResult(EmiMuteChoice.Keep), async _ =>
    {
        var win = new EmiDeskWindow();
        int completed = 0;
        int pets = ConditioningControlPanel.Services.EmiDesk.EmiState.Current.PetsTotal;
        try
        {
            win.RunSummon(() => completed++);
            await Pump(700);
            Assert.True(win.ChainLive);
            Assert.False(win.InputLocked);
            var body = (Control)typeof(EmiDeskWindow).GetField("_bodyRoot",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(win)!;
            var point = body.TranslatePoint(new Point(body.Bounds.Width / 2, body.Bounds.Height / 2), win)!.Value;
            win.MouseDown(point, global::Avalonia.Input.MouseButton.Left);
            win.MouseUp(point, global::Avalonia.Input.MouseButton.Left);
            Assert.Equal(1, completed);
            Assert.Equal("^_^", win.Face);
            Assert.Equal(pets + 1, ConditioningControlPanel.Services.EmiDesk.EmiState.Current.PetsTotal);
            await Pump(2000);
            Assert.Equal(1, completed);
            Assert.Equal("0_0", win.Face); // cancelled wake cannot replace the pat
        }
        finally { win.ShutDown(); }
    });

    [Fact]
    public Task SayDuringDismissalCannotCancelTheOutro() => Muted(() => Task.FromResult(EmiMuteChoice.Keep), async svc =>
    {
        await svc.Summon();
        var win = svc.Window!;
        svc.Dismiss();
        win.Say("x");
        win.PlayChain("shock");
        await Pump(3000);
        Assert.False(win.IsVisible);
        Assert.False(win.InputLocked);
        Assert.False(svc.IsOut);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task InterruptedWakeFinishesOnceAndLaterLinesEatPats(bool panic) => Muted(() => Task.FromResult(EmiMuteChoice.Keep), async _ =>
    {
        // Step the chain timer so real pointer dispatch cannot race the 160 ms shock frame.
        var clock = new ChainClock();
        var win = new EmiDeskWindow(clock.Start);
        int completed = 0;
        try
        {
            win.RunSummon(() => completed++);
            await Pump(700);
            Assert.True(win.ChainLive);
            Assert.Equal("wake", win.ChainPlayer.Current?.Id);
            var wake = Assert.Single(clock.Scheduled);
            Assert.Equal(TimeSpan.FromMilliseconds(500), wake.Hold);
            if (panic) win.StopChainForPanic(); else win.Say("x");
            Assert.Equal(1, completed);
            Assert.True(wake.Cancelled);
            var replacement = win.ChainPlayer.Current;
            Assert.Equal(panic ? null : "say", replacement?.Id);
            clock.Advance(wake.Hold); // the interrupted wake's original timer is now due
            Assert.False(wake.Fired);
            Assert.Same(replacement, win.ChainPlayer.Current);
            Assert.Equal(1, completed);
            Assert.Equal("0_0", win.Face); // neither interrupted wake nor its next frame resumed
            win.PlayChain("shock");
            await Pump(250); // dispatch may take longer than the first frame's hold
            var body = (Control)typeof(EmiDeskWindow).GetField("_bodyRoot",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(win)!;
            var point = body.TranslatePoint(new Point(body.Bounds.Width / 2, body.Bounds.Height / 2), win)!.Value;
            win.MouseDown(point, global::Avalonia.Input.MouseButton.Left);
            win.MouseUp(point, global::Avalonia.Input.MouseButton.Left);
            Assert.Equal("o_o", win.Face);
            Assert.True(win.ChainLive);
            clock.Advance(TimeSpan.FromMilliseconds(160));
            Assert.Equal("0_0", win.Face);
            clock.Advance(TimeSpan.FromMilliseconds(160));
            Assert.Equal("(◉_◉)", win.Face);
            clock.Advance(TimeSpan.FromMilliseconds(900));
            Assert.False(win.ChainLive);
            clock.Advance(TimeSpan.FromMilliseconds(2000));
            await Pump(2000);
            Assert.False(wake.Fired);
            Assert.Equal(1, completed);
        }
        finally { win.ShutDown(); }
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1500)]
    public Task PresentationAbandonsDismissalAndLeavesHerUsable(int delay) => Muted(() => Task.FromResult(EmiMuteChoice.Keep), async svc =>
    {
        CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
        await svc.Summon();
        var win = svc.Window!;
        svc.Dismiss();
        await Pump(delay);
        win.BeginPresentation(() => { });
        win.RunPresentationEntrance();
        await Pump(3000);
        Assert.True(win.IsVisible);
        Assert.False(win.PresentationArriving);
        win.EndPresentation();
        win.PlayChain("shock");
        Assert.True(win.ChainLive);
        svc.Dismiss();
        await Pump(3000);
        Assert.False(win.IsVisible);
        Assert.False(win.InputLocked);
        Assert.False(svc.IsOut);
    });

    /// <summary>P06: the panic registry's emi-desk-feedback entry kills a running chain.</summary>
    [Fact]
    public Task PanicEntryCancelsHerChain() => Muted(() => Task.FromResult(EmiMuteChoice.Keep), async svc =>
    {
        CoreSettings.Current.EmiDeskMuteAvatar = false;
        await svc.Summon();
        var win = svc.Window!;
        win.PlayChain("shock");
        Assert.True(win.ChainLive);
        var entry = Assert.Single(ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All, s => s.Id == "emi-desk-feedback");
        entry.Stop(null);
        Assert.False(win.ChainLive);
        svc.Dismiss();
        await Pump(3000);
    });

    /// <summary>WPF EmiDeskService.Summon :313/:325: a summon counts in EmiState and puts her back
    /// where SavePlacement left her, not at the default park.</summary>
    [Fact]
    public Task SummonCountsAndRestoresHerSavedPlace() => Muted(() => Task.FromResult(EmiMuteChoice.Keep), async svc =>
    {
        CoreSettings.Current.EmiDeskMuteAvatar = false;
        int before = ConditioningControlPanel.Services.EmiDesk.EmiState.Current.SummonCount;
        await svc.Summon();
        Assert.Equal(before + 1, ConditioningControlPanel.Services.EmiDesk.EmiState.Current.SummonCount);
        var win = svc.Window!;
        var parked = win.Position;
        var moved = new PixelPoint(parked.X - 137, parked.Y - 91);
        win.Position = moved;
        win.SavePlacement();
        win.ParkBottomRightOfMain();
        Assert.Equal(parked, win.Position);
        win.RestorePlacement();
        Assert.Equal(moved, win.Position);
        svc.Dismiss();
        await Pump(3000);
    });

    /// <summary>WPF EmiDock.Refresh :150-163: while she is out the chip's mini face mirrors the
    /// widget's face frame by frame; when she leaves it rests and stops listening.</summary>
    [Fact]
    public Task DockMiniFaceMirrorsHerWhileOut() => Muted(() => Task.FromResult(EmiMuteChoice.Keep), async svc =>
    {
        CoreSettings.Current.EmiDeskMuteAvatar = false;
        var dock = new EmiDock();
        var host = new Window { Width = 200, Height = 200, Content = dock };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        var mini = dock.FindControl<EmiFace>("MiniFace")!;
        try
        {
            dock.FindControl<Button>("BtnChip")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(100);
            Assert.True(svc.IsOut);
            svc.Window!.DrawFace(">_<");
            Assert.Equal(">_<", mini.Face);
            // WPF binds Small/Flat too (EmiDock.xaml.cs:150-156), and the glass is an EmiFace.
            svc.Window!.DrawFace(":)", small: true, flat: true);
            var glass = svc.Window!.FindControl<EmiFace>("FaceView")!;
            Assert.True(glass.Small);
            Assert.True(glass.Flat);
            Assert.Equal(":)", mini.Face);
            Assert.True(mini.Small);
            Assert.True(mini.Flat);
            svc.Window!.DrawFace(":)");
            Assert.False(mini.Small);
            Assert.False(mini.Flat);
            svc.Window!.DrawFace("...", small: true);

            svc.Dismiss();
            await Pump(3000);
            Assert.False(svc.IsOut);
            Assert.Equal("0_0", mini.Face);
            Assert.False(mini.Small); // resting clears the mirrored flags
            svc.Window!.DrawFace("^_^");
            Assert.Equal("0_0", mini.Face);
        }
        finally { host.Close(); }
    });

    /// <summary>WPF App.OnExit closed her; here she goes with the main window.</summary>
    [Fact]
    public Task ClosingTheShellClosesHer() => Muted(() => Task.FromResult(EmiMuteChoice.Keep), svc =>
    {
        var main = new Window { Width = 400, Height = 300 };
        main.Show();
        var win = new EmiDeskWindow();
        bool closed = false;
        win.Closed += (_, _) => closed = true;
        EmiDeskService.HookShell(main, win);
        win.Show();
        main.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
        return Task.CompletedTask;
    });
    private sealed class ChainClock
    {
        internal sealed class Timer(TimeSpan due, TimeSpan hold, Action callback)
        {
            internal TimeSpan Due { get; } = due;
            internal TimeSpan Hold { get; } = hold;
            internal bool Cancelled { get; set; }
            internal bool Fired { get; set; }
            internal Action Callback { get; } = callback;
        }

        private TimeSpan _now;
        internal List<Timer> Scheduled { get; } = new();
        internal Action Start(TimeSpan hold, Action callback)
        {
            var timer = new Timer(_now + hold, hold, callback);
            Scheduled.Add(timer);
            return () => timer.Cancelled = true;
        }

        internal void Advance(TimeSpan elapsed)
        {
            var target = _now + elapsed;
            while (Scheduled.Where(t => !t.Cancelled && !t.Fired && t.Due <= target)
                       .OrderBy(t => t.Due).FirstOrDefault() is { } timer)
            {
                _now = timer.Due;
                timer.Fired = true;
                timer.Callback();
            }
            _now = target;
        }
    }

}
