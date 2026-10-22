using System;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

internal sealed partial class EmiDeskService
{
    private readonly EmiNudgeMachine _nudges = new();
    private ITimer? _pinNudgeTimer;
    private int _pinNudgeGen;
    /// <summary>Clock for the 900 ms pin-nudge delay and, when not the system clock, the desk's summon
    /// FX steps; tests step it instead of sleeping (P08).</summary>
    internal TimeProvider Clock { get; set; } = TimeProvider.System;
    internal EmiLineEngine LineEngine { get; set; } = EmiLineEngine.Instance;
    public event EventHandler<EmiMoment>? MomentFired;

    /// <summary>WPF Fire: emit even while away; draw only onto the live widget. Holds still arm while away.</summary>
    public void Fire(string id, object? context = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        try { MomentFired?.Invoke(this, new EmiMoment(id, context)); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] moment observer failed"); }
        if (!IsOut && !LineEngine.IsHoldMoment(id)) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => DrawRingMoment(id, context)); return; }
        DrawRingMoment(id, context);
    }

    private void DrawRingMoment(string id, object? context)
    {
        try
        {
            if (_window?.PresentationActive == true) return;
            var line = LineEngine.Draw(id, EmiLineEngine.ToCtx(context));
            if (line != null && IsOut) _window?.SpeakRingLine(line, LineEngine);
            // ponytail: asks remain refused by Core's unset EffectFeasibleProbe; no dead offer chip is shown.
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring moment {Moment} failed", id); }
    }

    /// <summary>An accepted pin teaches the gesture even before the widget's first summon.</summary>
    public void NotePinMadeElsewhere(string id, bool pinned)
    {
        _window?.RebuildRing();
        if (!pinned || string.IsNullOrWhiteSpace(id)) return;
        EmiState.NotePinMade();
        Fire("pinAdded", new { target = id });
    }

    internal void NoteRingOpened()
    {
        EmiState.NoteRingOpen();
        var track = _nudges.OnRingOpened(new RingNudgeWorld(this));
        if (track == null) return;
        _pinNudgeTimer?.Dispose();
        int gen = ++_pinNudgeGen;
        _pinNudgeTimer = Clock.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
        {
            if (gen != _pinNudgeGen) return; // cancelled or superseded after the tick was queued
            _pinNudgeTimer?.Dispose();
            _pinNudgeTimer = null;
            SpeakPinNudge(track);
        }, DispatcherPriority.Background), null, TimeSpan.FromMilliseconds(900), Timeout.InfiniteTimeSpan);
    }

    private void SpeakPinNudge(string track)
    {
        bool spoke = false;
        var world = new RingNudgeWorld(this);
        try
        {
            if (!world.Quiet || EmiState.Current.PinGistGot) return;
            LineDraw? line;
            if (LineEngine.MomentIds.Contains(track)) line = LineEngine.Draw(track);
            else line = LineEngine.HoldActive ? null : new LineDraw("nudge-fallback:" + track, track,
                "like one? squeeze it. the other button. it stays.", "^_^", null, 2, false, 0);
            if (line != null && !line.Hold && !string.IsNullOrWhiteSpace(line.Text))
                spoke = _window?.SpeakRingLine(line, LineEngine) == true;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] pin nudge failed"); }
        finally { _nudges.Attempted(world, track, spoke); }
    }

    private void StopRingNudges()
    {
        _pinNudgeGen++;
        _pinNudgeTimer?.Dispose();
        _pinNudgeTimer = null;
        _nudges.NoteDismiss();
    }

    /// <summary>WPF panicPressed cuts the line and cancels onboarding before another frame can land.</summary>
    public void StopRingSpeechForPanic()
    {
        StopRingNudges();
        _window?.CancelChain();
        Fire("panicPressed");
    }

    private sealed class RingNudgeWorld(EmiDeskService owner) : IEmiNudgeWorld
    {
        public int PetsTotal => EmiState.Current.PetsTotal;
        public bool PetGistGot => EmiState.Current.PetGistGot;
        public int RingOpens => EmiState.Current.RingOpens;
        public bool RingGistGot => EmiState.Current.RingGistGot;
        public bool PinGistGot => EmiState.Current.PinGistGot;
        public int FiresOf(string track) => EmiState.Current.NudgeFires.TryGetValue(track, out var n) ? n : 0;
        public void NoteFire(string track) => EmiState.NoteNudgeFired(track);
        public bool Quiet
        {
            get
            {
                var window = owner.Window;
                var main = (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                return owner.IsOut && window?.IsVisible == true && !window.PresentationActive
                    && !window.InputLocked && !window.Transiting && !window.ChainLive
                    && !CoreSession.IsSessionRunning && CoreEngine.Video?.IsPlaying != true
                    && AvatarTube.AvatarTubeWindow.Live?.HasBubbleUp != true
                    && main?.WindowState != WindowState.Minimized && !owner.LineEngine.HoldActive;
            }
        }
    }
}
