using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.RemoteHud;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Windows/RemoteHud/RemoteHudWindow.cs: the subject's own pill, top
    /// centre of the monitor the main window is on, up while a controller holds the remote. Who has it, for how
    /// long, what they did last, the Easy strength tag and three buttons back: More, Easy, Stop. Unowned,
    /// topmost, never activated (WPF WS_EX_NOACTIVATE is ShowActivated=false + ShowInTaskbar=false).
    /// ponytail: the press/bump/live-dot animations are not here.
    /// </summary>
    internal sealed class RemoteHudWindow : Window
    {
        private static readonly Color Mint = Color.FromRgb(0x4F, 0xE0, 0xB5), Amber = Color.FromRgb(0xFF, 0xC2, 0x4D),
            Red = Color.FromRgb(0xFF, 0x54, 0x68), Pink = Color.FromRgb(0xFF, 0x69, 0xB4), Ink = Color.FromRgb(0x1A, 0x1A, 0x2E);

        private readonly Func<RemoteRelay?> _relay;
        private readonly Action _onLocalStop;
        private readonly Func<Window?> _anchor;
        private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly Border _pill, _tag;
        private readonly TextBlock _initial, _headline, _subline, _tagText;
        private readonly StackPanel _expanded;
        private bool _collapsed, _wasConnected, _disposed;
        private DateTime _seenSinceUtc = DateTime.UtcNow, _confirmUntilUtc;
        private DateTime? _lastStopUtc;
        private string? _confirm, _lastAction;

        /// <summary>Render proof (--render-all): no relay, the anonymous headline.</summary>
        internal RemoteHudWindow() : this(() => null, () => { }, () => null) { Paint(); }

        public RemoteHudWindow(Func<RemoteRelay?> relay, Action onLocalStop, Func<Window?> anchor)
        {
            (_relay, _onLocalStop, _anchor) = (relay, onLocalStop, anchor);
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title = "CCP remote";

            _initial = new TextBlock { FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Child = new Grid { Children = { _initial, new global::Avalonia.Controls.Shapes.Ellipse { Name = "RemoteLiveDot", Width = 10, Height = 10, Fill = new SolidColorBrush(Mint), Stroke = new SolidColorBrush(Ink), StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -2, -2) } } },
                Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Pink, 0), new GradientStop(Color.FromRgb(0xB0, 0x7C, 0xFF), 1) } } };
            _headline = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
            _subline = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xA8, 0xC8)),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, Margin = new Thickness(0, 1, 0, 0) };
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0), Background = Brushes.Transparent, Children = { _headline, _subline } };
            foreach (var fold in new Control[] { badge, words })
            {
                fold.Cursor = new Cursor(StandardCursorType.Hand);
                ToolTip.SetTip(fold, Loc.Get("remote_hud_fold_tip"));
                fold.PointerReleased += (_, e) => { e.Handled = true; ToggleCollapsed(); };
            }
            _tagText = new TextBlock { FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Amber) };
            _tag = new Border { Background = new SolidColorBrush(Color.FromArgb(0x33, Amber.R, Amber.G, Amber.B)), BorderBrush = new SolidColorBrush(Amber),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Child = _tagText };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children =
            {
                SignalButton("more", Mint, "remote_hud_more", "remote_hud_more_tip"),
                SignalButton("easy", Amber, "remote_hud_easy", "remote_hud_easy_tip"),
                SignalButton("stop", Red, "remote_hud_stop", "remote_hud_stop_tip"),
            } };
            _expanded = new StackPanel { Orientation = Orientation.Horizontal, Children = { words, _tag, buttons } };
            _pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xEE, Ink.R, Ink.G, Ink.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, Pink.R, Pink.G, Pink.B)),
                BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(26), Padding = new Thickness(8, 6), Margin = new Thickness(12),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromArgb(0x99, 0, 0, 0) }),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { badge, _expanded } },
            };
            Content = _pill;
            SizeChanged += (_, _) => Place();
            _tick.Tick += (_, _) => Refresh();
        }

        internal string Headline => _headline.Text ?? "";
        internal string Subline => _subline.Text ?? "";
        internal string? StrengthTag => _tag.IsVisible ? _tagText.Text : null;

        /// <summary>WPF Refresh: re-reads the relay and shows, hides and repaints the pill. UI thread only.</summary>
        public void Refresh()
        {
            if (_disposed) return;
            var r = _relay();
            bool connected = RemoteHudRules.ShouldShow(r?.IsActive == true, r?.ControllerConnected == true);
            if (r?.IsActive != true) _lastAction = null;   // WPF ResetV2SessionState clears LastActionLabel
            if (connected && !_wasConnected) (_seenSinceUtc, _collapsed, _lastStopUtc, _confirm) = (DateTime.UtcNow, false, null, null);
            _wasConnected = connected;
            if (!connected)
            {
                _tick.Stop();
                if (IsVisible) Hide();
                return;
            }
            Paint();
            if (!IsVisible) { Show(); Place(); }
            _tick.Start();
            // WPF KeepOnTop (RemoteHudWindow.cs:368), each 1 s refresh: video and lock cards re-assert their own topmost.
            Topmost = false; Topmost = true;   // re-issues HWND_TOPMOST / _NET_WM_STATE_ABOVE, no activation (ChaosHudWindow)
        }

        /// <summary>WPF LastActionChanged: a command landed (RemoteRelay.CommandReceived).</summary>
        public void NoteAction(string action)
        {
            _lastAction = Services.Remote.RemoteActionLabels.For(action);
            Refresh();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tick.Stop();
            try { Close(); } catch (Exception ex) { Serilog.Log.Debug("RemoteHud close failed: {E}", ex.Message); }
        }

        private void Paint()
        {
            var r = _relay();
            var name = RemoteHudRules.DisplayName(r?.ControllerName);
            _initial.Text = RemoteHudRules.InitialFrom(name);
            _headline.Text = name != null ? Loc.GetF("remote_hud_has_remote", name) : Loc.Get("remote_hud_someone");
            var now = DateTime.UtcNow;
            if (_confirm != null && now < _confirmUntilUtc) _subline.Text = _confirm;
            else
            {
                _confirm = null;
                var sub = RemoteHudRules.FormatElapsed(RemoteHudRules.Elapsed(r?.ControllerConnectedSinceUtc, _seenSinceUtc, now));
                if (!string.IsNullOrWhiteSpace(_lastAction)) sub += "  ·  " + Loc.GetF("remote_hud_last", _lastAction);
                _subline.Text = sub;
            }
            var tag = RemoteHudRules.StrengthFor(RemoteCommands.EasyFactor);
            _tag.IsVisible = tag != RemoteHudRules.StrengthTag.None;
            _tagText.Text = tag == RemoteHudRules.StrengthTag.Quarter ? Loc.Get("remote_hud_quarter") : Loc.Get("remote_hud_half");
            _expanded.IsVisible = !_collapsed;
            _pill.Padding = _collapsed ? new Thickness(5) : new Thickness(8, 6);
        }

        private void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            Paint();
        }

        /// <summary>Top centre of the work area of the monitor the main window is on.</summary>
        private void Place()
        {
            if (!IsVisible) return;
            try
            {
                var screen = (_anchor() is { } a ? Screens.ScreenFromWindow(a) : null) ?? Screens.Primary;
                if (screen == null) return;
                var wa = screen.WorkingArea;
                double w = Bounds.Width > 0 ? Bounds.Width : 360;
                Position = new PixelPoint(wa.X + (int)((wa.Width - w * screen.Scaling) / 2), wa.Y + (int)((10 - _pill.Margin.Top) * screen.Scaling));
            }
            catch (Exception ex) { Serilog.Log.Debug("RemoteHud placement failed: {E}", ex.Message); }
        }

        private Button SignalButton(string kind, Color fill, string labelKey, string tipKey)
        {
            var b = new Button
            {
                Name = "BtnHud_" + kind, Width = 40, Height = 40, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(0), Focusable = false,
                CornerRadius = new CornerRadius(20), Background = new SolidColorBrush(fill), Foreground = new SolidColorBrush(Ink),
                Cursor = new Cursor(StandardCursorType.Hand), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Content = new TextBlock { Text = Loc.Get(labelKey), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Ink) },
            };
            ToolTip.SetTip(b, Loc.Get(tipKey));
            b.Click += async (_, _) => await OnSignalAsync(kind, labelKey);
            return b;
        }

        /// <summary>WPF OnSignalAsync: Stop acts locally first (debounced so a double-click never quits the
        /// app through the double-panic exit), then the signal; the pill confirms for 1.6 s.</summary>
        internal async Task OnSignalAsync(string kind, string labelKey)
        {
            if (_disposed) return;
            bool isStop = kind == "stop";
            if (isStop)
            {
                var now = DateTime.UtcNow;
                if (!RemoteHudRules.StopPressAccepted(_lastStopUtc, now)) return;
                _lastStopUtc = now;
                try { _onLocalStop(); } catch (Exception ex) { Serilog.Log.Warning("RemoteHud: local stop failed: {E}", ex.Message); }
            }
            bool sent = false;
            try { sent = _relay() is { } r && await r.SendSignalAsync(kind); }
            catch (Exception ex) { Serilog.Log.Debug("RemoteHud: signal {Kind} failed: {E}", kind, ex.Message); }
            if (_disposed) return;
            _confirm = isStop
                ? Loc.Get(sent ? "remote_hud_stopped" : "remote_hud_stopped_not_sent")
                : sent ? Loc.GetF("remote_hud_sent", Loc.Get(labelKey)) : Loc.Get("remote_hud_not_sent");
            _confirmUntilUtc = DateTime.UtcNow + RemoteHudRules.ConfirmFor;
            Refresh();
        }
    }
}
