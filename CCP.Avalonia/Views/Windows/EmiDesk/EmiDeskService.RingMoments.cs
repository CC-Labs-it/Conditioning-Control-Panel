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
    private readonly EmiNudgeMachine _nudges;
    private ITimer? _pinNudgeTimer;
    private int _pinNudgeGen;
    private ITimer? _nudgePoll;
    private int _nudgePollGen;

    /// <summary>WPF EmiDeskService NudgePollMs: the ambient pet/ring question is asked every 5 s while she is out.</summary>
    internal const int NudgePollMs = 5_000;

    // WPF's fallback lines for a track the lines file does not know yet (EmiDeskService.cs DrawNudge).
    private const string PetNudgeFallbackText = "pat me. it's allowed.";
    private const string RingNudgeFallbackText = "my other side has stuff in it. the other button.";
    private const string PinNudgeFallbackText = "like one? squeeze it. the other button. it stays.";

    // The machine reads the service clock, so a stepped TimeProvider steps its 25 s / 40 s / 90 s gaps too.
    internal EmiDeskService() => _nudges = new EmiNudgeMachine(() => Clock.GetUtcNow().UtcDateTime);
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

    /// <summary>WPF StartNudges: a summon arms the machine and starts the ambient poll.</summary>
    private void StartNudges(int summonCount)
    {
        _nudges.NoteSummon(summonCount);
        ArmNudgePoll(++_nudgePollGen);
    }

    // One-shot re-armed after each tick (same 5 s cadence as WPF's DispatcherTimer) so a stopped
    // generation can never leave a tick behind.
    private void ArmNudgePoll(int gen)
    {
        _nudgePoll?.Dispose();
        _nudgePoll = Clock.CreateTimer(_ => Dispatcher.UIThread.Post(() => OnNudgePoll(gen), DispatcherPriority.Background),
            null, TimeSpan.FromMilliseconds(NudgePollMs), Timeout.InfiniteTimeSpan);
    }

    /// <summary>WPF OnNudgeTick: ask the machine; a pet or ring track owed now is spoken.</summary>
    private void OnNudgePoll(int gen)
    {
        if (gen != _nudgePollGen) return;
        if (!IsOut) { StopRingNudges(); return; }
        try
        {
            var track = _nudges.Tick(new RingNudgeWorld(this));
            if (track != null) SpeakNudge(track, recheckQuiet: false);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge tick failed"); }
        if (gen == _nudgePollGen) ArmNudgePoll(gen);
    }

    private void SpeakPinNudge(string track) => SpeakNudge(track, recheckQuiet: true);

    /// <summary>WPF SpeakNudge: the attempt is always recorded, a fire is spent only on a line that landed.
    /// The delayed pin nudge re-checks quiet (900 ms passed since the machine said yes).</summary>
    private void SpeakNudge(string track, bool recheckQuiet)
    {
        bool spoke = false;
        var world = new RingNudgeWorld(this);
        try
        {
            if (!IsOut || _window is not { IsVisible: true, PresentationActive: false }) return;
            if (recheckQuiet && (!world.Quiet || EmiState.Current.PinGistGot)) return;
            var line = DrawNudge(track);
            if (line != null) spoke = _window.SpeakRingLine(line, LineEngine);
            if (spoke) Log.Information("[EmiDesk] nudge {Track}: {Line}", track, line!.Id);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge {Track} failed", track); }
        finally { _nudges.Attempted(world, track, spoke); }
    }

    /// <summary>WPF DrawNudge: the engine's answer is final for a known track; an unknown one gets
    /// WPF's fallback line, behind nothing but the hold.</summary>
    private LineDraw? DrawNudge(string track)
    {
        if (LineEngine.MomentIds.Contains(track))
        {
            var drawn = LineEngine.Draw(track);
            return drawn != null && !drawn.Hold && !string.IsNullOrWhiteSpace(drawn.Text) ? drawn : null;
        }
        if (LineEngine.HoldActive) return null;
        var (text, face) = track switch
        {
            EmiNudgeMachine.PetTrack => (PetNudgeFallbackText, "^_^"),
            EmiNudgeMachine.RingTrack => (RingNudgeFallbackText, "^_~"),
            EmiNudgeMachine.PinTrack => (PinNudgeFallbackText, "^_^"),
            _ => ((string?)null, (string?)null)
        };
        return text == null || face == null ? null : new LineDraw(track + ".fallback", track, text, face, null, 2, false, 0);
    }

    private void StopRingNudges()
    {
        _nudgePollGen++;
        _nudgePoll?.Dispose();
        _nudgePoll = null;
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
