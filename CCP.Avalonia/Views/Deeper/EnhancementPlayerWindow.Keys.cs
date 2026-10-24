using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

// WPF EnhancementPlayerWindow.xaml.cs:2746-2970. Space play/pause, Left/Right seek 5 s (Shift 30 s),
// Up/Down volume 5, M mute, Esc tap closes an idle player, hold Esc 3 s stops playback.
// ponytail: F (HTML5/borderless fullscreen) and the page-side key relay need fullscreen and a
// document-created script, which NativeWebView lacks; named in the parity row as -b-next.
public partial class EnhancementPlayerWindow
{
    private const double EscHoldSeconds = 3.0;
    private ConditioningControlPanel.Avalonia.Helpers.BeatLoop? _escHoldTimer; // WPF: 50 ms DispatcherTimer; here the window's shared beat
    private DateTime _escHoldStart;
    private DateTime _lastEscSignal;
    private bool _escConsumed;
    private int? _volumeBeforeMute;
    private bool _muteChanging;

    /// <summary>The clock the hold-Esc timer reads. Tests step it.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox) return;
        if (HandlePlayerKeyDown(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift), _keysDown.Contains(e.Key)))
            e.Handled = true;
        _keysDown.Add(e.Key);
    }

    // Avalonia's KeyEventArgs has no IsRepeat; a key already down is a repeat.
    private readonly System.Collections.Generic.HashSet<Key> _keysDown = new();

    private void Window_PreviewKeyUp(object? sender, KeyEventArgs e)
    {
        _keysDown.Remove(e.Key);
        if (e.Key != Key.Escape) return;
        if (FocusManager?.GetFocusedElement() is TextBox) return;
        HandleEscapeUp();
        e.Handled = true;
    }

    private bool HandlePlayerKeyDown(Key key, bool shift, bool isRepeat)
    {
        switch (key)
        {
            case Key.Escape:
                _lastEscSignal = UtcNow();
                if (!isRepeat) HandleEscapeDown();
                return true;
            case Key.Space:
                if (!isRepeat) BtnPlayPause_Click();
                return true;
            case Key.Left: SeekRelative(shift ? -30 : -5); return true;
            case Key.Right: SeekRelative(shift ? 30 : 5); return true;
            case Key.Up: AdjustVolume(+5); return true;
            case Key.Down: AdjustVolume(-5); return true;
            case Key.M:
                if (!isRepeat) ToggleMute();
                return true;
            case Key.F:
                return true; // fullscreen: see the ponytail note above
            default:
                return false;
        }
    }

    private void SeekRelative(double deltaSeconds)
    {
        if (_isVideoMode && _videoNavigated)
        {
            var t = Math.Max(0, _currentSec + deltaSeconds);
            if (_durationSec > 0) t = Math.Min(t, _durationSec);
            _currentSec = t;
            _ = _videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke(
                "l.currentTime=" + t.ToString("0.###", CultureInfo.InvariantCulture) + ";"));
        }
        else if (_localAudio != null && _localAudio.DurationSeconds > 0)
            SeekAudio(Math.Clamp(_localAudio.PositionSeconds + deltaSeconds, 0, _localAudio.DurationSeconds));
    }

    private void AdjustVolume(int delta) => _sliderVolume.Value = Math.Clamp(_sliderVolume.Value + delta, 0, 100);

    private void ToggleMute()
    {
        try
        {
            _muteChanging = true;
            if (_volumeBeforeMute is int restore)
            {
                _volumeBeforeMute = null;
                _sliderVolume.Value = restore;
            }
            else
            {
                _volumeBeforeMute = (int)Math.Round(_sliderVolume.Value);
                _sliderVolume.Value = 0;
            }
        }
        finally { _muteChanging = false; }
    }

    // -- Hold-Esc escape hatch: people get stranded in loops. Off under a strict lock.

    private static bool IsUnderStrictLock()
        => MainShellWindow.LockdownActive || CoreSettings.Current.StrictLockEnabled;

    // A paused session is still a session; a loaded video counts whether or not it rolls.
    private bool IsSessionActive() => _isPlaying || _localAudio != null || (_isVideoMode && _videoNavigated);

    private void HandleEscapeDown()
    {
        if (_changePopup.IsOpen)
        {
            _changePopup.IsOpen = false;
            _escConsumed = true;
            return;
        }
        if (_escHoldTimer != null) return;
        _escConsumed = false;
        if (IsUnderStrictLock()) return;
        if (!IsSessionActive()) return;
        _escHoldStart = UtcNow();
        _escHoldTimer = new ConditioningControlPanel.Avalonia.Helpers.BeatLoop(this, _ => EscHoldTimer_Tick(null, EventArgs.Empty));
        _escHoldTimer.Start(); // first step shows the hint at 0
    }

    private void EscHoldTimer_Tick(object? sender, EventArgs e)
    {
        var now = UtcNow();
        // A lost key-up (focus swap) reads as a release after 1.5 s of silence.
        if ((now - _lastEscSignal).TotalSeconds > 1.5) { CancelEscHold(); return; }
        var held = (now - _escHoldStart).TotalSeconds;
        ShowHoldEscHint(Math.Min(1, held / EscHoldSeconds));
        if (held < EscHoldSeconds) return;
        CancelEscHold();
        _escConsumed = true;
        BtnStop_Click();
        _txtStatus.Text = Loc.Get("deeper_player_status_escape_stopped");
        Log.Information("[DeeperPlayer] hold-Esc stop");
    }

    private void HandleEscapeUp()
    {
        CancelEscHold();
        if (_escConsumed) { _escConsumed = false; return; }
        if (IsUnderStrictLock()) return;
        // A stray tap must never end a session: only an idle player closes on Esc.
        if (IsSessionActive()) return;
        Close();
    }

    private void CancelEscHold()
    {
        if (_escHoldTimer != null)
        {
            var loop = _escHoldTimer;
            _escHoldTimer = null;
            loop.Stop();
        }
        this.FindControl<Border>("HoldEscHint")!.IsVisible = false;
    }

    private void ShowHoldEscHint(double fraction)
    {
        this.FindControl<ProgressBar>("HoldEscProgress")!.Value = Math.Clamp(fraction, 0, 1);
        this.FindControl<Border>("HoldEscHint")!.IsVisible = true;
    }
}
