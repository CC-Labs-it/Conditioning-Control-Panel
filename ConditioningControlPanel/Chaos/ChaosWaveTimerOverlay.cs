using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ConditioningControlPanel;

/// <summary>
/// The Pocket Watch charm's wave countdown: a small click-through pill pinned to the
/// top-right corner showing the current wave and the time left in it.
/// Hidden until the charm is worn. Same keep-alive contract as the other chaos overlays:
/// created once at run start (<see cref="EnsureCreated"/>), content updated in place, the
/// window itself only closed at run teardown (<see cref="CloseActive"/>) — closing a layered
/// window mid-run can wedge the shared WPF render thread.
/// </summary>
public sealed class ChaosWaveTimerOverlay : Window
{
    private static ChaosWaveTimerOverlay? _active;

    /// <summary>Create the (empty, invisible) window ahead of time at run start.</summary>
    public static void EnsureCreated()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosWaveTimerOverlay(); ((Window)_active).Show(); }
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosWaveTimer.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Update the readout. Safe from any thread; creates the window if needed.</summary>
    public static void Update(int wave, int waveCount, double secLeftInWave, double score)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosWaveTimerOverlay(); ((Window)_active).Show(); }
                    _active.SetText(wave, waveCount, secLeftInWave, score);
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosWaveTimer.Update: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Blank the readout (drafts/pauses) without closing the window.</summary>
    public static void Clear()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() => { try { if (_active != null) _active._pill.Visibility = Visibility.Collapsed; } catch { } });
        }
        catch { }
    }

    /// <summary>Re-stack the live window above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Instant teardown (run end / shutdown).</summary>
    public static void CloseActive()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) { _active = null; return; }
            disp.BeginInvoke(() => { try { _active?.CloseNow(); } catch { } });
        }
        catch { }
    }

    private readonly Border _pill;
    private readonly TextBlock _wave;
    private readonly TextBlock _clock;
    private readonly TextBlock _score;
    private bool _urgent;
    private bool _finalRush;

    private ChaosWaveTimerOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;

        _wave = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x9B, 0xC8)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        _clock = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_wave);
        row.Children.Add(_clock);

        // Score line under the clock — the Pocket Watch's bonus utility: glance the
        // top-right for time AND score without ever opening the sidebar.
        _score = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(_score);

        _pill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(170, 0x12, 0x0E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 0xE8, 0x43, 0x93)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 5, 14, 5),
            Visibility = Visibility.Collapsed,
            Child = stack,
        };
        Content = _pill;

        var wa = SystemParameters.WorkArea;
        Top = wa.Top + 10;                        // top-right corner, clear of the centered banner/announcer
        Left = wa.Right - 180;                    // re-aligned precisely once content sizes
        SizeChanged += (_, _) => { try { Left = wa.Right - ActualWidth - 14; } catch { } };

        SourceInitialized += (_, _) => ApplyExStyles();
    }

    private void SetText(int wave, int waveCount, double secLeftInWave, double score)
    {
        _pill.Visibility = Visibility.Visible;
        var r = ConditioningControlPanel.Services.Chaos.ChaosWaveTimerReadout.For(wave, waveCount, secLeftInWave, score);
        _wave.Text = r.Wave;
        _clock.Text = r.Clock;
        _score.Text = r.Score;

        // The last ten seconds of a wave run hot (red clock); calm white otherwise.
        bool urgent = r.Urgent;
        if (urgent != _urgent)
        {
            _urgent = urgent;
            _clock.Foreground = urgent
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A))
                : Brushes.White;
        }

        // The RUN's last ten seconds (urgent on the final wave): the red clock also blinks —
        // the descent gets a finale instead of an interruption.
        bool finalRush = r.FinalRush;
        if (finalRush != _finalRush)
        {
            _finalRush = finalRush;
            if (finalRush)
            {
                var blink = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(420))
                {
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                _clock.BeginAnimation(OpacityProperty, blink);
            }
            else
            {
                _clock.BeginAnimation(OpacityProperty, null);
                _clock.Opacity = 1.0;
            }
        }
    }

    private void CloseNow()
    {
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
