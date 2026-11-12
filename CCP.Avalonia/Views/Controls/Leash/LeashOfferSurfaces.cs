using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

// WPF LeashSurfaces.OnSnapshot/TryShowWaitingAsk (:104-124): a newly arrived offer pops up as the one ask
// (LeashAskWindow), held until the panel or launcher is up and until no ask or snap is on screen.
internal sealed class LeashOfferSurfaces : IDisposable
{
    private readonly Func<Window?> _owner;
    private readonly HashSet<string> _seen = new();
    private readonly List<LeashOffer> _waiting = new();
    private readonly List<LeashEvent> _held = new();
    private ILeashService? _service;
    private Control? _tugTarget;
    private ITransform? _tugPrior;
    private RelativePoint _tugOrigin;
    private TransformGroup? _tugGroup;
    private RotateTransform? _tugTurn;
    private TranslateTransform? _tugMove;
    private DispatcherTimer? _tugTimer;
    private Stopwatch? _tugClock;
    private double _tugAmount;
    private string? _account;
    internal LeashOfferSurfaces(Func<Window?> owner)
    {
        _owner = owner;
        LeashAskWindow.Freed += TryShow;
    }

    internal void Rebind()
    {
        var next = LeashHead.Service;
        var account = CoreSettings.Current.UnifiedId;
        var serviceChanged = !ReferenceEquals(next, _service);
        if (serviceChanged || account != _account)
        {
            if (_service != null) { _service.SnapshotChanged -= OnSnapshot; _service.EventArrived -= OnEvent; }
            _service = next; _account = account; _seen.Clear(); _waiting.Clear(); _held.Clear();
            if (_service != null)
            {
                _service.SnapshotChanged += OnSnapshot;
                _service.EventArrived += OnEvent;
                if (serviceChanged && _service.Available) Process(_service.Snapshot);
            }
        }
        TryShow();
        FlushHeld();
    }

    private bool PanelShowing() => _owner() is MainShellWindow;
    // WPF LeashSurfaces._iCut/NoteCut (:30,:145-147,:267-272): a cut pressed here already sounded and was
    // said, so the Ended event it brings back is swallowed once.
    internal static bool SelfCut;
    internal static void NoteCut()
    {
        SelfCut = true;
        LeashSound.Event(LeashEventKind.Ended, DateTime.UtcNow);
    }
    private void OnEvent(LeashEvent e)
    {
        var source = _service;
        void Apply()
        {
            if (!ReferenceEquals(source, _service) || !ReferenceEquals(source, LeashHead.Service) || _account != CoreSettings.Current.UnifiedId) return;
            if (e.Kind == LeashEventKind.Ended && SelfCut) { SelfCut = false; return; }
            if (e.Kind == LeashEventKind.Answered && e.Accepted == true)
            {
                LeashAskWindow.ShowHolderSnap(e.From, _owner());
                return;
            }
            LeashSound.Event(e.Kind, DateTime.UtcNow);
            var owner = _owner();
            var wobbles = e.Kind == LeashEventKind.Tug && StartTug(owner);
            var (toast, seen, hold) = LeashUiRules.EventNotice(e.Kind, owner is MainShellWindow, wobbles);
            if (toast) Toast(e);
            if (seen) source?.NoteShown(e.Id);
            if (hold)
            {
                _held.Add(e);
                while (_held.Count > LeashUiRules.HeldNotices) _held.RemoveAt(0);
            }
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }
    // WPF MainWindow.Leash.cs:442-450 and LeashFx.cs:89-115: preserve the root's
    // transform, yank left, swing back and settle in 520 ms. Reduced motion halves distance.
    private bool StartTug(Window? owner)
    {
        var amount = FriendsDrawer.FeedMotionAmount * 0.8;
        if (amount <= 0 || owner is not { IsVisible: true, WindowState: not WindowState.Minimized, Content: Control target }) return false;
        StopTug();
        _tugTarget = target; _tugPrior = target.RenderTransform; _tugOrigin = target.RenderTransformOrigin;
        _tugTurn = new RotateTransform(); _tugMove = new TranslateTransform();
        _tugGroup = new TransformGroup();
        _tugGroup.Children.Add(_tugPrior as Transform ?? new MatrixTransform { Matrix = _tugPrior?.Value ?? Matrix.Identity });
        _tugGroup.Children.Add(_tugTurn); _tugGroup.Children.Add(_tugMove);
        target.RenderTransformOrigin = RelativePoint.Center;
        target.RenderTransform = _tugGroup;
        _tugAmount = amount;
        _tugClock = Stopwatch.StartNew();
        _tugTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _tugTimer.Tick += (_, _) => StepTug(_tugClock!.Elapsed.TotalMilliseconds);
        _tugTimer.Start();
        return true;
    }

    internal void StepTug(double milliseconds)
    {
        if (_tugTarget == null || _tugTurn == null || _tugMove == null) return;
        if (milliseconds >= 520 || FriendsDrawer.FeedMotionAmount <= 0) { StopTug(); return; }
        var t = Math.Clamp(milliseconds / 520, 0, 1);
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
            x = 5 * (1 - ConditioningControlPanel.Services.Billboard.BillboardTiming.Thud(q)); angle = 1.1 * (1 - q);
        }
        _tugMove.X = x * _tugAmount; _tugTurn.Angle = angle * _tugAmount;
    }

    private void StopTug()
    {
        _tugTimer?.Stop(); _tugTimer = null; _tugClock = null;
        if (_tugTarget is { } target && ReferenceEquals(target.RenderTransform, _tugGroup))
        {
            target.RenderTransform = _tugPrior;
            target.RenderTransformOrigin = _tugOrigin;
        }
        _tugTarget = null; _tugGroup = null; _tugMove = null; _tugTurn = null; _tugPrior = null;
    }

    private static void Toast(LeashEvent e)
    {
        if (LeashUiRules.EventKey(e) is { } key)
            App.Notifications?.Show(Loc.GetF(key, e.From.Name), e.Kind is LeashEventKind.Punish or LeashEventKind.AssignMissed
                ? Helpers.NotificationType.Warning : Helpers.NotificationType.Info);
    }
    private void FlushHeld()
    {
        if (!PanelShowing()) return;
        foreach (var e in _held) { Toast(e); _service?.NoteShown(e.Id); }
        _held.Clear();
    }

    private static string OfferKey(LeashOffer o) => o.From.Id + "@" + o.At.ToUnixTimeSeconds();
    private void OnSnapshot(LeashSnapshot snapshot)
    {
        var source = _service;
        var account = CoreSettings.Current.UnifiedId; // the account this snapshot was published for
        void Apply()
        {
            if (!ReferenceEquals(source, _service)) return;
            if (!ReferenceEquals(source, LeashHead.Service) || _account != CoreSettings.Current.UnifiedId)
            {
                Rebind();
                // Core never republishes an identical snapshot, so a new account's first one is kept, not dropped.
                if (!ReferenceEquals(source, _service) || account != _account) return;
            }
            if (source?.Available == true) Process(snapshot);
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }
    private void Process(LeashSnapshot snapshot)
    {
        foreach (var offer in snapshot.Offers)
        {
            if (!_seen.Add(OfferKey(offer))) continue;
            _waiting.RemoveAll(o => o.From.Id == offer.From.Id);
            _waiting.Add(offer);
        }
        _waiting.RemoveAll(o => !snapshot.Offers.Any(current => current.From.Id == o.From.Id));
        TryShow();
    }
    private void TryShow()
    {
        if (_service?.Available != true || _waiting.Count == 0 || LeashAskWindow.Current != null || LeashAskWindow.Snap != null) return;
        var owner = _owner();
        if (owner == null || !owner.IsVisible || owner.WindowState == WindowState.Minimized) return;
        var offer = _waiting[0];
        _waiting.RemoveAt(0);
        // WPF ShowAsk reads the live service (LeashSurfaces.cs:211,217).
        if (LeashAskWindow.Open(offer, () => LeashHead.Service, owner) == null) _waiting.Insert(0, offer);
    }
    public void Dispose()
    {
        StopTug();
        LeashAskWindow.Freed -= TryShow;
        if (_service != null) { _service.SnapshotChanged -= OnSnapshot; _service.EventArrived -= OnEvent; }
        _service = null; _waiting.Clear(); _seen.Clear();
    }
}
