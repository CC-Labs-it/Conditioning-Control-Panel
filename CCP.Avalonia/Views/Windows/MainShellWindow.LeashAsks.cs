// WPF MainWindow/MainWindow.Leash.cs:80-88, :442-450 (tug wobble) and Controls/Leash/LeashSurfaces.cs:48-76,
// 79-100 (held notices), 104-125 (asks), 127-182 (events). The one leash binding of this head: offers pop up
// as the one ask, events sound, toast, report seen and wobble the window.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private ILeashService? _askService;
    private readonly HashSet<string> _seenLeashOffers = new();
    private readonly List<LeashOffer> _waitingLeashAsks = new();
    private readonly List<LeashEvent> _heldLeashNotices = new();

    private void InitializeLeashAsks()
    {
        LeashAskWindow.CurrentClosed += OnLeashAskClosed;
        LeashAskWindow.OfferShown += OnLeashOfferShown;
        Closed += (_, _) =>
        {
            LeashAskWindow.CurrentClosed -= OnLeashAskClosed;
            LeashAskWindow.OfferShown -= OnLeashOfferShown;
            if (_askService != null) { _askService.SnapshotChanged -= OnLeashAskSnapshot; _askService.EventArrived -= OnLeashEvent; }
            _askService = null;
            _waitingLeashAsks.Clear();
            _heldLeashNotices.Clear();
            StopLeashTug();
        };
        RebindLeashAsks();
    }

    // WPF rebinds every two seconds and retries asks when a panel or launcher returns.
    internal void RebindLeashAsks()
    {
        var next = LeashHead.Service;
        if (!ReferenceEquals(next, _askService))
        {
            if (_askService != null) { _askService.SnapshotChanged -= OnLeashAskSnapshot; _askService.EventArrived -= OnLeashEvent; }
            _askService = next;
            _seenLeashOffers.Clear();
            _heldLeashNotices.Clear();
            if (next != null)
            {
                next.SnapshotChanged += OnLeashAskSnapshot;
                next.EventArrived += OnLeashEvent;
                OnLeashAskSnapshot(next.Snapshot);
            }
        }
        TryShowLeashAsk();
        FlushHeldLeashNotices();
    }

    private void OnLeashAskSnapshot(LeashSnapshot snap)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var service = _askService;
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(service, _askService)) OnLeashAskSnapshot(snap); });
            return;
        }
        foreach (var offer in snap.Offers)
        {
            if (!_seenLeashOffers.Add(offer.From.Id + "@" + offer.At.ToUnixTimeSeconds())) continue;
            _waitingLeashAsks.RemoveAll(w => w.From.Id == offer.From.Id);
            _waitingLeashAsks.Add(offer);
        }
        _waitingLeashAsks.RemoveAll(w => !snap.Offers.Any(o => o.From.Id == w.From.Id));
        TryShowLeashAsk();
    }

    // WPF LeashSurfaces.cs:117-125, :230-255 drains waiting asks when an overlay closes.
    // A manual Look has already shown this offer, but other queued offers still wait.
    private void OnLeashOfferShown(LeashOffer offer) =>
        _waitingLeashAsks.RemoveAll(w => w.From.Id == offer.From.Id && w.At.ToUnixTimeSeconds() == offer.At.ToUnixTimeSeconds());

    private void OnLeashAskClosed() => Dispatcher.UIThread.Post(TryShowLeashAsk);

    private Window? LeashOwner() => IsVisible && WindowState != WindowState.Minimized ? this : LauncherWindow.Instance;

    // The ask and the snap share one overlay slot: a waiting ask holds while either is up.
    private void TryShowLeashAsk()
    {
        if (_waitingLeashAsks.Count == 0 || LeashAskWindow.Current != null) return;
        var owner = LeashOwner();
        if (owner is not { IsVisible: true } || owner.WindowState == WindowState.Minimized) return;
        var offer = _waitingLeashAsks[0];
        _waitingLeashAsks.RemoveAt(0);
        LeashAskWindow.Open(offer, () => _askService, owner);
    }

    private bool LeashPanelShowing() => IsVisible && WindowState != WindowState.Minimized;

    /// <summary>WPF LeashSurfaces.OnEvent: the cue, then a toast (panel up) or a held notice; receipt items are
    /// reported seen only when they really were on screen. An accepted answer opens the holder's snap instead.</summary>
    private void OnLeashEvent(LeashEvent e)
    {
        var source = _askService;
        void Apply()
        {
            if (!ReferenceEquals(source, _askService)) return;
            try
            {
                if (e.Kind == LeashEventKind.Answered && e.Accepted == true)
                {
                    LeashAskWindow.OpenSnap(LeashAskWindow.Me(), e.From, LeashOwner());
                    return;
                }
                if (e.Kind == LeashEventKind.Ended && LeashHead.SelfCut) { LeashHead.SelfCut = false; return; }
                LeashSound.Event(e.Kind, DateTime.UtcNow);
                // A tug is on screen wherever its wobble plays (the panel or the launcher), never with motion off.
                var wobbles = e.Kind == LeashEventKind.Tug && StartLeashTug(LeashOwner());
                var (toast, seen, hold) = LeashUiRules.EventNotice(e.Kind, LeashPanelShowing(), wobbles);
                if (toast) LeashToast(e);
                if (seen) source?.NoteShown(e.Id);
                if (hold)
                {
                    _heldLeashNotices.Add(e);
                    while (_heldLeashNotices.Count > LeashUiRules.HeldNotices) _heldLeashNotices.RemoveAt(0);
                }
            }
            catch (Exception ex) { Serilog.Log.Debug("[Leash] event {K} failed: {E}", e.Kind, ex.Message); }
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }

    /// <summary>WPF FlushHeldNotices: the panel is back, say what landed while it was away (the sounds already
    /// played) and only now report those items seen.</summary>
    private void FlushHeldLeashNotices()
    {
        if (_heldLeashNotices.Count == 0 || !LeashPanelShowing()) return;
        var held = _heldLeashNotices.ToList();
        _heldLeashNotices.Clear();
        foreach (var e in held)
        {
            try { LeashToast(e); _askService?.NoteShown(e.Id); }
            catch (Exception ex) { Serilog.Log.Debug("[Leash] held notice failed: {E}", ex.Message); }
        }
    }

    private static void LeashToast(LeashEvent e)
    {
        if (LeashUiRules.EventKey(e) is { } key)
            App.Notifications?.Show(Loc.GetF(key, e.From.Name), e.Kind is LeashEventKind.Punish or LeashEventKind.AssignMissed
                ? NotificationType.Warning : NotificationType.Info);
    }

    // ---- the tug wobble (WPF MainWindow.Leash.cs:442-450, LeashFx.Tug :89-115) ----------------------------
    private Control? _tugTarget;
    private ITransform? _tugPrior;
    private RelativePoint _tugOrigin;
    private TransformGroup? _tugGroup;
    private RotateTransform? _tugTurn;
    private TranslateTransform? _tugMove;
    private BeatLoop? _tugLoop;
    private double _tugAmount;
    internal bool LeashTugRunning => _tugLoop?.IsRunning == true;

    /// <summary>Yank left, swing back, settle in 520 ms on the owner window's content, on its shared beat.
    /// 0.8 x the feed amount (WPF Tug strength), half again at Reduced.</summary>
    private bool StartLeashTug(Window? owner)
    {
        var amount = FriendsDrawer.FeedMotionAmount * 0.8;
        if (amount <= 0 || owner is not { IsVisible: true, WindowState: not WindowState.Minimized, Content: Control target }) return false;
        StopLeashTug();
        _tugTarget = target; _tugPrior = target.RenderTransform; _tugOrigin = target.RenderTransformOrigin;
        _tugTurn = new RotateTransform(); _tugMove = new TranslateTransform();
        _tugGroup = new TransformGroup();
        _tugGroup.Children.Add(_tugPrior as Transform ?? new MatrixTransform { Matrix = _tugPrior?.Value ?? Matrix.Identity });
        _tugGroup.Children.Add(_tugTurn); _tugGroup.Children.Add(_tugMove);
        target.RenderTransformOrigin = RelativePoint.Center;
        target.RenderTransform = _tugGroup;
        _tugAmount = amount;
        _tugLoop = new BeatLoop(target, seconds => StepLeashTug(seconds * 1000));
        _tugLoop.Start();
        return true;
    }

    internal void StepLeashTug(double ms)
    {
        if (_tugTarget == null || _tugTurn == null || _tugMove == null) return;
        if (ms >= 520 || FriendsDrawer.FeedMotionAmount <= 0) { StopLeashTug(); return; }
        var t = Math.Clamp(ms / 520, 0, 1);
        double x, angle;
        if (t <= 0.3)
        {
            var q = t / 0.3;
            x = -12 * (1 - (1 - q) * (1 - q)); angle = -2.2 * q;
        }
        else if (t <= 0.6)
        {
            var q = (t - 0.3) / 0.3;
            var ease = q < 0.5 ? 2 * q * q : 1 - 2 * (1 - q) * (1 - q);
            x = -12 + 17 * ease; angle = -2.2 + 3.3 * q;
        }
        else
        {
            var q = (t - 0.6) / 0.4;
            x = 5 * (1 - BillboardTiming.Thud(q)); angle = 1.1 * (1 - q);
        }
        _tugMove.X = x * _tugAmount; _tugTurn.Angle = angle * _tugAmount;
    }

    private void StopLeashTug()
    {
        var loop = _tugLoop;
        _tugLoop = null;
        loop?.Stop();
        if (_tugTarget is { } target && ReferenceEquals(target.RenderTransform, _tugGroup))
        {
            target.RenderTransform = _tugPrior;
            target.RenderTransformOrigin = _tugOrigin;
        }
        _tugTarget = null; _tugGroup = null; _tugMove = null; _tugTurn = null; _tugPrior = null;
    }
}
