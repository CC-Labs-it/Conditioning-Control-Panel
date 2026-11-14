using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;
namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
internal sealed partial class EmiDeskService
{
    private DispatcherTimer? _greetingTimer;
    private string _summonVia = "rail";
    private bool _summonTouring;
    private DateTime _outSinceUtc;
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;
    // Tests build isolated desks (new EmiDeskService()); the engine's probes stay on the shared desk.
    internal EmiDeskService()
    {
        EmiLineEngine.AskSituationProbe = static () => Instance.AskSituationOk();
        EmiLineEngine.EffectFeasibleProbe = EffectFeasible;
    }
    private bool AskSituationOk()
    {
        var win = _window;
        var main = (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return IsOut && win is { IsVisible: true, PresentationActive: false, InputLocked: false, Transiting: false, AskLive: false, RainAskLive: false }
            && !CoreSession.IsSessionRunning && App.Sessions?.IsRunning != true && CoreEngine.Video?.IsPlaying != true
            && !TubeBubbleLive() && main?.WindowState != WindowState.Minimized;
    }
    private static bool EffectFeasible(string? effect)
    {
        if (!string.Equals(effect, "pinTop:fyp", StringComparison.OrdinalIgnoreCase)) return false;
        var target = EmiTargets.Find("fyp");
        return target is { Available: true, Locked: false }
            && !EmiState.Current.Pins.Contains("fyp", StringComparer.OrdinalIgnoreCase);
    }
    internal EmiLineEngine LineEngine { get; set; } = EmiLineEngine.Instance;
    internal Func<bool> TubeBubbleLive = () => global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow.Live?.HasBubbleUp == true;
    // WPF Fire/Speak: the Core engine owns spice, cadence, limits and holds. The window owns pixels.
    public void Fire(string moment, object? context = null)
    {
        if (string.IsNullOrWhiteSpace(moment)) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Fire(moment, context));
            return;
        }
        try
        {
            var engine = LineEngine;
            if (!IsOut)
            {
                if (engine.IsHoldMoment(moment)) engine.Draw(moment, EmiLineEngine.ToCtx(context));
                return;
            }
            // WPF :677-689: safety holds arm even while the widget is away.
            if (_window?.PresentationActive == true && !engine.IsHoldMoment(moment)) return;
            if (_window?.AskLive == true)
            {
                if (!engine.IsHoldMoment(moment)) return;
                _window.CancelAsk();
            }
            if (!engine.IsHoldMoment(moment) && (IntakeHostWindow.IsAnyOpen() || TubeBubbleLive())) return;
            var ctx = EmiLineEngine.ToCtx(context);
            var line = engine.Draw(moment, ctx);
            var ask = engine.DrawAsk(moment, ctx);
            if (ask != null && moment is not ("dismissed" or "appClosing"))
            {
                _window?.ShowAsk(ask, engine);
                return;
            }
            // WPF EmiDeskService.cs:649 NeverSpeaks; a hold is a face, so it plays even there (:765).
            bool neverSpeaks = moment is "dismissed" or "appClosing";
            if (line != null && (line.Hold || !neverSpeaks))
                _window?.SpeakLine(line, engine);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] speech moment {Moment} failed", moment); }
    }
    /// <summary>WPF NoteEmiSpoke (EmiDeskService.cs:782): stagger the avatar's next bark after her line,
    /// only while she is not muting the avatar (no second voice to stagger against then).</summary>
    internal void NoteEmiSpoke()
    {
        try
        {
            if (AvatarMuted) return;
            global::ConditioningControlPanel.Avalonia.Platform.BarkHead.Engine?.NotifyExternalLineSpoken();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bark min-gap stamp failed"); }
    }
    // #4071, WPF EmiDeskService.cs:123-195 THE AVATAR OWNS VOICE: the tube is about to talk, so EMI holds to
    // blink-only faces until its bubble and voice end, plus the moment's own tail. Armed even while she is
    // muted or away. A one-second local poll that runs only while a hold is live (no server call).
    private DispatcherTimer? _avatarHoldTimer;
    private readonly System.Collections.Generic.HashSet<string> _voiceHolds = new(StringComparer.Ordinal);
    // The tube that spoke last is the one the hold waits on (WPF has one App.AvatarWindow).
    private global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow? _speakingTube;
    private bool TubeVoiceLive() =>
        (_speakingTube ?? global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow.Live)?.VoiceHoldLive == true;
    /// <summary>The tube's speech chokepoints call this the instant before a bubble goes up, muted or not.</summary>
    internal void NoteAvatarSpeaking(global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow? tube = null)
    {
        if (tube != null) _speakingTube = tube;
        try { ArmVoiceHold("avatarSpeaking"); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] avatar hold arm failed"); }
    }
    private void ArmVoiceHold(string momentId)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ArmVoiceHold(momentId)); return; }
        if (_voiceHolds.Add(momentId)) Fire(momentId);
        if (_avatarHoldTimer != null) return;
        _avatarHoldTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _avatarHoldTimer.Tick += OnAvatarHoldTick;
        _avatarHoldTimer.Start();
    }
    private void OnAvatarHoldTick(object? sender, EventArgs e) => CheckAvatarVoiceHold();
    /// <summary>Release every voice hold once the tube has neither a bubble up nor a clip playing.</summary>
    internal void CheckAvatarVoiceHold()
    {
        try
        {
            if (TubeVoiceLive()) return;
            if (_avatarHoldTimer != null)
            {
                _avatarHoldTimer.Stop();
                _avatarHoldTimer.Tick -= OnAvatarHoldTick;
                _avatarHoldTimer = null;
            }
            foreach (var id in _voiceHolds) LineEngine.ReleaseHold(id);
            _voiceHolds.Clear();
            _speakingTube = null;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] avatar hold tick failed"); }
    }
    private void ScheduleGreeting(string? why)
    {
        CancelGreeting();
        _summonVia = string.Equals(why, "hotkey", StringComparison.OrdinalIgnoreCase) ? "hotkey" : "rail";
        _summonTouring = string.Equals(why, "tour", StringComparison.OrdinalIgnoreCase);
        _outSinceUtc = UtcNow();
        _greetingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
        _greetingTimer.Tick += (_, _) => GreetingDue();
        _greetingTimer.Start();
    }
    // The timer and the headless tests drive the same step: nothing greets unless Summon scheduled it.
    internal void GreetingDue() { if (_greetingTimer != null) GreetNow(); }
    internal void GreetNow()
    {
        CancelGreeting();
        if (!IsOut) return;
        // WPF EmiDeskService.cs:819,847-855: delayed, ordered greetings.
        var moment = !LineEngine.EverSpent("firstContact") ? "firstContact"
            : !LineEngine.EverSpent("desktopFirstBoot") ? "desktopFirstBoot" : "summoned";
        Fire(moment, new { via = _summonVia, minutes = Math.Max(0, (int)(UtcNow() - _outSinceUtc).TotalMinutes), touring = _summonTouring });
    }
    private void CancelGreeting()
    {
        _greetingTimer?.Stop();
        _greetingTimer = null;
    }
    internal void PanicSilence()
    {
        CancelGreeting();
        _window?.CancelAsk();
        _window?.CancelChain();
        Fire("panicPressed");
    }
}
