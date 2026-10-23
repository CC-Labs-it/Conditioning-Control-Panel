using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Chaos/ChaosToyButtonWindow.cs: the big hero button for
    /// an equipped active skill, parked bottom-right for the whole descent. Glyph, keybind badge and
    /// live status (ready / cooldown seconds / charges left); ready = bright with a slow flashing
    /// glow, effect running = steady hot glow, cooling/spent = greyed out. One small topmost
    /// no-activate window per toy; a click is the same path as the keybind (ChaosRunHost.UseToyById).
    /// WPF's WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE is ShowInTaskbar=false + ShowActivated=false; the
    /// DropShadowEffect glow is a BoxShadow on a layer behind the disc (P82: no Effect), its flash a
    /// FrameClock (P82: no infinite Animation) that runs only while ready or mid-press.
    /// <para>ponytail: RaiseToTopmost (WPF :178) is not here: the only WPF callers re-raise over a
    /// mid-run mandatory video, which this head's run does not play.</para>
    /// </summary>
    internal sealed class ChaosToyButtonWindow : Window
    {
        private const double DISC = 138;
        private const double PAD = 14;   // breathing room so the glow doesn't clip at the window edge

        private static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static readonly Color Hot = Color.FromRgb(0xFF, 0xC8, 0xE8);
        private static readonly IBrush ReadyBorder = new SolidColorBrush(Pink);
        private static readonly IBrush ActiveBorder = new SolidColorBrush(Hot);
        private static readonly IBrush CoolBorder = new SolidColorBrush(Color.FromArgb(0x55, 0x9A, 0x9A, 0xA8));

        private enum VisualState { Unset, Cooling, Ready, Active }

        private readonly ChaosToyState _toy;
        private readonly Border _disc, _glow;
        private readonly TextBlock _glyph;
        private readonly ScaleTransform _press = new(1, 1);
        private readonly FrameClock _clock;
        private readonly Stopwatch _since = Stopwatch.StartNew();
        private double _pressAt = double.NaN;
        private VisualState _vstate;

        internal TextBlock Status { get; }
        internal ChaosToyState Toy => _toy;

        /// <summary>Render proof (--render-all): a ready VibePopping on Q in slot 0.</summary>
        internal ChaosToyButtonWindow() : this(SampleToy(), 0) { }

        private static ChaosToyState SampleToy()
        {
            var b = ChaosLifetimeBoons.ById("vibe_popping")!;
            return new ChaosToyState { Id = b.Id, Name = b.Name, Glyph = b.Glyph, Desc = b.Desc, KeyLabel = "Q", CooldownSec = b.UseCooldownSec };
        }

        public ChaosToyButtonWindow(ChaosToyState toy, int slotIndex)
        {
            _toy = toy;
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = CoreSettings.HasProvider ? CoreSettings.Current.ChaosPinOnTop : true;   // WPF ChaosWindowZ.BornTopmost
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = DISC + PAD * 2;
            Height = DISC + PAD * 2;
            Cursor = new Cursor(StandardCursorType.Hand);
            Title = toy.Name;

            _glyph = new TextBlock
            {
                Text = toy.Glyph, FontSize = 50, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
            };
            Status = new TextBlock
            {
                FontSize = 15, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA0, 0xD8)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 18),
            };
            var keyBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x12, 0x10, 0x26)),
                BorderBrush = ReadyBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 3, 9, 3),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 4, 0),
                Child = new TextBlock { Text = toy.KeyLabel, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = ReadyBorder },
            };
            _glow = new Border
            {
                Width = DISC, Height = DISC, CornerRadius = new CornerRadius(DISC / 2), Opacity = 0,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 26, Spread = 2, Color = Pink }),
            };
            _disc = new Border
            {
                Width = DISC, Height = DISC,
                CornerRadius = new CornerRadius(DISC / 2),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x15, 0x12, 0x26)),
                BorderBrush = ReadyBorder, BorderThickness = new Thickness(3.5),
                RenderTransformOrigin = RelativePoint.Center,   // WPF RenderTransformOrigin (0.5,0.5)
                RenderTransform = _press,
                Child = new Grid { Children = { _glyph, Status } },
            };
            Content = new Grid { Children = { _glow, _disc, keyBadge } };

            _clock = new FrameClock(this) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
            _clock.Tick += (_, _) => Animate();
            PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                PressPulse();
                ChaosRunHost.UseToyById(_toy.Id);
            };
            _toy.PropertyChanged += OnToyChanged;
            Place(slotIndex);                       // before Show: no frame at (0,0)
            Opened += (_, _) => Place(slotIndex);   // again once the screen is known for sure
            Closed += (_, _) => { _toy.PropertyChanged -= OnToyChanged; _clock.Stop(); };

            ChaosOverlayWindow.ChaosTips.Attach(_disc, $"{toy.Glyph} {toy.Name} · {toy.KeyLabel}", toy.Desc,
                string.IsNullOrEmpty(toy.CapstoneDesc) ? null : "max: " + toy.CapstoneDesc, flavor: toy.Flavor);
            UpdateVisual();
        }

        private void OnToyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Dispatcher.UIThread.CheckAccess()) UpdateVisual();   // the run tick and a click are on the UI thread
            else Dispatcher.UIThread.Post(UpdateVisual);
        }

        /// <summary>WPF Left/Top from SystemParameters.WorkArea (DIPs); Avalonia positions in pixels.</summary>
        private void Place(int slotIndex)
        {
            try
            {
                var screen = Screens?.Primary ?? Screens?.All?.FirstOrDefault();
                if (screen is null) return;
                var wa = screen.WorkingArea;
                double s = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
                double left = wa.Right / s - 16 - Width - slotIndex * (DISC + 20);
                double top = wa.Bottom / s - DISC - PAD * 2 - 14;
                Position = new PixelPoint((int)(left * s), (int)(top * s));
            }
            catch (Exception ex) { Log.Debug("Toy button position: {E}", ex.Message); }
        }

        private void UpdateVisual()
        {
            Status.Text = _toy.StatusText;
            var vs = _toy.IsEffectActive ? VisualState.Active : _toy.IsReady ? VisualState.Ready : VisualState.Cooling;
            if (vs == _vstate) return;   // the glow only changes on a real state change
            _vstate = vs;
            switch (vs)
            {
                case VisualState.Active:   // steady hot glow for the whole effect window
                    _glow.BoxShadow = new BoxShadows(new BoxShadow { Blur = 34, Spread = 2, Color = Hot });
                    _glow.Opacity = 1.0;
                    _disc.Opacity = 1.0;
                    _disc.BorderBrush = ActiveBorder;
                    _glyph.Opacity = 1.0;
                    break;
                case VisualState.Ready:    // bright + a slow flashing glow (Animate)
                    _glow.BoxShadow = new BoxShadows(new BoxShadow { Blur = 26, Spread = 2, Color = Pink });
                    _disc.Opacity = 1.0;
                    _disc.BorderBrush = ReadyBorder;
                    _glyph.Opacity = 1.0;
                    break;
                default:                   // cooling / spent: greyed out behind the countdown
                    _glow.Opacity = 0;
                    _disc.Opacity = 0.45;
                    _disc.BorderBrush = CoolBorder;
                    _glyph.Opacity = 0.55;
                    break;
            }
            SyncClock();
        }

        private void SyncClock()
        {
            if (_vstate == VisualState.Ready || !double.IsNaN(_pressAt)) _clock.Start();
            else _clock.Stop();
        }

        private void PressPulse()
        {
            _pressAt = _since.Elapsed.TotalSeconds;
            _press.ScaleX = _press.ScaleY = 0.86;
            SyncClock();
        }

        /// <summary>WPF: the ready flash is DoubleAnimation 0.25 -> 0.95, 950 ms, AutoReverse, SineEase;
        /// the press is 0.86 -> 1.0 over 180 ms with BackEase(EaseOut, 0.6).</summary>
        private void Animate()
        {
            double now = _since.Elapsed.TotalSeconds;
            if (_vstate == VisualState.Ready)
            {
                double phase = now / 0.95 % 2.0;
                double t = phase <= 1 ? phase : 2 - phase;
                _glow.Opacity = 0.25 + 0.70 * (1 - Math.Cos(Math.PI * t)) / 2;
            }
            if (!double.IsNaN(_pressAt))
            {
                double t = Math.Min(1, (now - _pressAt) / 0.18);
                double u = 1 - t, a = 0.6;   // BackEase out: 1 - (u^3 - a*u*sin(pi*u))
                double eased = 1 - (u * u * u - a * u * Math.Sin(Math.PI * u));
                _press.ScaleX = _press.ScaleY = 0.86 + 0.14 * eased;
                if (t >= 1) { _pressAt = double.NaN; SyncClock(); }
            }
        }
    }
}
