using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services;
namespace ConditioningControlPanel.Avalonia.Views.Windows;
public partial class MainShellWindow {
    private BankAccumulator? _bank; private DispatcherTimer? _bankPoll; private AmbientFxCanvas? _bankLayer; private BeatLoop? _bankStep, _bankPop;
    private bool _bankHolding, _bankLive, _bankClosed; private int _bankId, _bankLevel, _bankCount; private double _bankNeeded, _bankFrom, _bankShown, _bankPot, _bankStarted;
    private ScaleTransform? _bankScale; private long _bankEpoch; private TimeProvider _bankTime = TimeProvider.System; internal bool BankHolding => _bankHolding;
    internal bool BankFlying => _bankLive; internal bool BankPolling => _bankPoll?.IsEnabled == true;
    private double BankNow => _bankTime.GetElapsedTime(_bankEpoch).TotalMilliseconds;
    // WPF MainWindow.EventFx.cs:118: particle budget, visibility, state and focus.
    private bool BankAllowed => !_bankClosed && AmbientFxCanvas.Env.AllowParticles
        && IsActive && IsVisible && WindowState != WindowState.Minimized;
    // WPF MainWindow.BankFx.cs:199,229,250. XPAwarded precedes this head's Awarded
    // display notification, so no speculative XPChanged arm is needed here.
    private void HookBankFx() {
        _bankTime = BeatLoop.Time; _bankEpoch = _bankTime.GetTimestamp(); _bank = new BankAccumulator(() => BankNow); ProgressionBank.XPAwarded += QueueBankAward;
        Deactivated += BankWindowChanged;
        PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) RefreshBankGate(); };
        AmbientFxCanvas.Env.MotionGateChanged += RefreshBankGate;
        Closed += (_, _) => {
            _bankClosed = true; ProgressionBank.XPAwarded -= QueueBankAward; AmbientFxCanvas.Env.MotionGateChanged -= RefreshBankGate; Deactivated -= BankWindowChanged;
            StopBankFx(false);
        };
    }
    private void QueueBankAward(ProgressionBank.XpAward award)
        => Dispatcher.UIThread.Post(() => ReceiveBankAward(award));
    // WPF :272-375: weather never enters the pot; weather during a flight rides
    // its release to truth. A level change discards the pot and aborts first.
    private void ReceiveBankAward(ProgressionBank.XpAward award) {
        if (_bankClosed) return;
        try {
            if (award.LeveledUp || !BankAllowed) { StopBankFx(); return; }
            if (!BankAccumulator.IsBankable(award.Source)) { if (_bank?.HasOpenPot != true && !_bankLive) ReleaseBankHold(); return; }
            _bankHolding = true; _bankLevel = CoreSettings.Current.PlayerLevel; _bankNeeded = XpCurve.GetXPForLevel(_bankLevel, XpCurve.EpochOf(CoreSettings.Current));
            _xpOdometer?.Stop(); var flight = _bank?.OnAward(award.Amount, award.Source, false); if (flight != null) LaunchBankFlight(flight);
            if (_bank?.HasOpenPot == true || _bankLive) StartBankPoll(); else ReleaseBankHold();
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Bank award failed"); StopBankFx(); }
    }
    // WPF :394,441: only presentation is held; every exit writes the current ledger.
    private void ReleaseBankHold(bool writeTruth = true) { bool held = _bankHolding; _bankHolding = false; if (held && writeTruth && !_bankClosed) UpdateLevelDisplay(); }
    // WPF :464,488: no idle poll; six second watchdog and orphan-hold self heal.
    private void StartBankPoll() {
        if (_bankPoll == null) { _bankPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; _bankPoll.Tick += (_, _) => PollBank(); }
        _bankPoll.Start();
    }
    internal void PollBank() {
        try {
            if (!BankAllowed) { StopBankFx(); return; }
            if (_bankLive && BankNow - _bankStarted > 6000) AbortBankFlight(true);
            if (!_bankLive && _bank?.Tick() is { } flight) LaunchBankFlight(flight);
            if (_bankLive || _bank?.HasOpenPot == true) return; ReleaseBankHold(); _bankPoll?.Stop();
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Bank poll failed"); StopBankFx(); }
    }
    private void BankWindowChanged(object? sender, EventArgs e) => RefreshBankGate();
    private void RefreshBankGate() { if (!BankAllowed) StopBankFx(); }
    // WPF :250,635, plus panic surface cleanup. Invalidate callbacks BEFORE Stop,
    // whose force-land callbacks run synchronously.
    internal void StopBankFx(bool writeTruth = true) {
        _bank?.Reset(); AbortBankFlight(writeTruth); _bankPoll?.Stop(); _bankPop?.Stop(); if (_bankScale != null) _bankScale.ScaleX = _bankScale.ScaleY = 1;
    }
    private void AbortBankFlight(bool writeTruth) {
        ++_bankId; _bankLive = false; _bankStep?.Stop(); _bankLayer?.Stop(); ReleaseBankHold(writeTruth);
    }
    // WPF :540,781,794,903: mapped source, profile then header fallback; bounded
    // per-flight surface outside the Viewbox. Bad geometry releases immediately.
    private void LaunchBankFlight(BankAccumulator.Flight flight) {
        if (!BankAllowed || !_bankHolding || _bankNeeded <= 0
            || Named<Grid>("EventFxHost") is not { } host
            || !BankAnchor(host, Named<TextBlock>("TxtXP"), out var target))
        { AbortBankFlight(true); return; }
        var key = flight.DominantSource switch { XPSource.Session => "presets", XPSource.Quest => "quests", XPSource.Bubble or XPSource.BubbleCount => "studio", _ => null, };
        if (!BankAnchor(host, key == null ? null : NavAnchorForTab(key), out var origin)
            && !BankAnchor(host, Named<Button>("BtnProfileBubble"), out origin)) {
            if (!BankBounds(host, Named<Border>("XPBarTrack"), out var track))
            { AbortBankFlight(true); return; }
            // WPF MainWindow.BankFx.cs:816-819: the transformed left edge, not local width.
            origin = new Point(track.Left - 180, track.Center.Y);
        }
        double width = Math.Abs(target.X - origin.X) + 200, height = Math.Abs(target.Y - origin.Y) + 200;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width > 2600 || height > 2600)
        { AbortBankFlight(true); return; }
        var corner = new Point(Math.Min(origin.X, target.X) - 100, Math.Min(origin.Y, target.Y) - 100);
        if (_bankLayer == null) {
            _bankLayer = new AmbientFxCanvas { IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            host.Children.Add(_bankLayer);
        }
        _bankLayer.Width = width; _bankLayer.Height = height; _bankLayer.Margin = new Thickness(corner.X, corner.Y, 0, 0); _bankLayer.UpdateLayout();
        int id = ++_bankId; _bankLive = true; _bankStarted = BankNow; _bankPot = flight.XpSum; _bankCount = Math.Max(1, flight.TokenCount);
        _bankFrom = _bankShown = BankCounterScript.StartValue(_lastXpShown, _lastXpLevelShown, _bankLevel); StartBankPoll();
        _bankLayer.BankTokens(new Point(origin.X - corner.X, origin.Y - corner.Y),
            new Point(target.X - corner.X, target.Y - corner.Y), _bankCount, AmbientFxCanvas.Env.GlowColor, // WPF MainWindow.BankFx.cs:578
            (index, last) => BankLanded(id, index, last));
    }
    private static bool BankAnchor(Control host, Control? anchor, out Point center) {
        center = default; if (!BankBounds(host, anchor, out var bounds)) return false; center = bounds.Center; return true;
    }
    // WPF MainWindow.BankFx.cs:842-856: map the whole rectangle through the visual transform.
    private static bool BankBounds(Control host, Control? anchor, out Rect bounds) {
        bounds = default;
        if (anchor == null || !anchor.IsEffectivelyVisible || anchor.Bounds.Width <= 0 || anchor.Bounds.Height <= 0
            || anchor.TransformToVisual(host) is not { } transform) return false;
        bounds = new Rect(anchor.Bounds.Size).TransformToAABB(transform);
        return double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top)
            && double.IsFinite(bounds.Right) && double.IsFinite(bounds.Bottom);
    }
    // WPF :594,657: landing ordinal drives a short counter step; last releases to
    // truth only when no successor pot owns the hold.
    private void BankLanded(int id, int ordinal, bool last) {
        if (id != _bankId || !_bankLive) return;
        try {
            double target = BankCounterScript.Target(_bankFrom, _bankPot, CoreSettings.Current.PlayerXP);
            double value = BankCounterScript.StepValue(_bankFrom, target, ordinal, _bankCount); StepBankCounter(value, last); if (!last) return; _bankLive = false; PlayBankThud();
            PopBankCounter(); _xpFraction = Math.Min(1, value / _bankNeeded); FillXpBar(true);
            FlashLevelUp(); // WPF :734 reuses the level-up overlay.
            if (_bank?.HasOpenPot != true) { ReleaseBankHold(); _bankPoll?.Stop(); }
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Bank landing failed"); StopBankFx(); }
    }
    private void StepBankCounter(double value, bool last) {
        _bankStep?.Stop();
        if (Named<TextBlock>("TxtXP") is not { } txt) return;
        double from = _bankShown;
        string tail = $" / {(int)_bankNeeded} XP";
        _lastXpShown = _bankShown = value; _lastXpLevelShown = _bankLevel; _xpReadoutTarget = value.ToString("F0") + tail;
        _bankStep = new BeatLoop(txt, seconds => {
            double p = Math.Clamp(seconds * 1000 / BankCounterScript.StepMs(last), 0, 1); txt.Text = (from + (value - from) * (1 - (1 - p) * (1 - p))).ToString("F0") + tail;
            if (p >= 1) _bankStep?.Stop();
        });
        _bankStep.Start();
    }
    // WPF :679: 1.28 catch at 90ms, BackEase(0.9) settle over 300ms.
    private void PopBankCounter() {
        if (!AmbientFxCanvas.Env.AllowTransitions || Named<TextBlock>("TxtXP") is not { } txt) return;
        if (_bankScale == null) {
            if (txt.RenderTransform != null && !txt.RenderTransform.Value.IsIdentity) return; txt.RenderTransform = _bankScale = new ScaleTransform(1, 1);
            txt.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }
        _bankPop?.Stop();
        _bankPop = new BeatLoop(txt, seconds => {
            double amount;
            if (seconds < .09) { double t = seconds / .09; amount = 1 - (1 - t) * (1 - t); }
            else { double t = Math.Clamp(1 - (seconds - .09) / .3, 0, 1); amount = t * t * t - t * .9 * Math.Sin(t * Math.PI); }
            _bankScale.ScaleX = _bankScale.ScaleY = 1 + .28 * amount; if (seconds >= .39) _bankPop?.Stop();
        });
        _bankPop.Start();
    }
    // WPF :750: mod override, quiet fallback, master volume, silent on failure.
    private static void PlayBankThud() {
        try {
            float volume = Math.Clamp((float)CoreSettings.Current.MasterVolume / 100 * .22f, 0, 1); if (volume <= 0) return;
            foreach (var candidate in new[] { "ui/bank_thud.mp3", "bubbles/Pop2.mp3" }) {
                var path = CoreModArt.AudioOverridePath(candidate) ?? ContentLocator.Resolve("Resources/sounds/" + candidate); if (!File.Exists(path)) continue;
                CoreAudio.PlayOneShot(path, volume, "ui-bank"); return;
            }
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Bank audio failed"); }
    }
}
