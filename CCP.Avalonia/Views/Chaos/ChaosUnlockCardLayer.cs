using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// The hub's unlock announcements (WPF ChaosHubWindow.xaml.cs:867 ShowUnlockCard +
    /// ChaosUnlockCardOverlay.cs BuildCardVisual): a card floats top-center over the hub - slide-in,
    /// 4.5 s dwell, fade - and back-to-back purchases queue. The layer never takes clicks, so
    /// shopping continues underneath. What each card says comes from Core
    /// <see cref="ChaosUnlockCardRules"/>.
    /// One timeline per card, sampled on the window's 30 fps beat, replaces WPF's BeginAnimation set.
    /// ponytail: real item art - this head's ChaosArt has no sprites yet, so the tinted glyph
    /// tile WPF falls back to is always what shows.
    /// </summary>
    internal sealed class ChaosUnlockCardLayer
    {
        // WPF ChaosHubWindow.xaml.cs:869 + ShowNextUnlockCard timings.
        internal const double InMs = 180, SlideMs = 220, HoldMs = 4500, OutMs = 260;
        private const double IconDelayMs = 100, IconMs = 380, FlareMs = 380;

        private readonly Panel _layer;
        private readonly Queue<ChaosUnlockCardInfo> _queue = new();
        private readonly BeatLoop _frame;   // the window's shared beat, not a per-view frame timer
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private Card? _current;

        /// <summary>Milliseconds on the layer's clock; tests replace it with a stepped one.</summary>
        internal Func<double> Now;

        internal ChaosUnlockCardLayer(Panel layer)
        {
            _layer = layer;
            Now = () => _clock.Elapsed.TotalMilliseconds;
            _frame = new BeatLoop(layer, _ => Advance());
        }

        /// <summary>The card on screen, null when the layer is idle.</summary>
        internal Border? Current => _current?.Root;
        internal int Queued => _queue.Count;

        internal void Show(ChaosUnlockCardInfo? info)
        {
            if (info == null) return;
            _queue.Enqueue(info);
            if (_current == null) ShowNext();
        }

        /// <summary>Window closed: drop the queue and stop the beat (P65).</summary>
        internal void Stop()
        {
            _frame.Stop();
            _queue.Clear();
            if (_current != null) _layer.Children.Remove(_current.Root);
            _current = null;
        }

        private void ShowNext()
        {
            if (_queue.Count == 0) { _current = null; _frame.Stop(); return; }
            var info = _queue.Dequeue();
            _current = Build(info, 400);
            _current.Start = Now();
            _current.Root.HorizontalAlignment = HorizontalAlignment.Center;
            _current.Root.VerticalAlignment = VerticalAlignment.Top;
            _current.Root.Margin = new Thickness(0, 96, 0, 0);   // below the title bar + tab strip
            _layer.Children.Add(_current.Root);
            var (cue, vol) = info.Cue;
            ChaosOverlayWindow.ChaosSfx.Play(cue, vol);   // the card carries the purchase's sting
            Apply(_current, 0);
            _frame.Start();
        }

        /// <summary>Sample the timeline at <see cref="Now"/>; retires the card after its fade.</summary>
        internal void Advance()
        {
            if (_current == null) { _frame.Stop(); return; }
            double t = Now() - _current.Start;
            if (t >= HoldMs + OutMs)
            {
                _layer.Children.Remove(_current.Root);
                ShowNext();
                return;
            }
            Apply(_current, t);
        }

        private static void Apply(Card c, double t)
        {
            c.Root.Opacity = t < HoldMs ? Math.Min(1, t / InMs) : Math.Max(0, 1 - (t - HoldMs) / OutMs);
            c.Slide.Y = -14 + 14 * BackOut(Math.Min(1, t / SlideMs), 0.4);
            double pop = 0.4 + 0.6 * BackOut(Math.Clamp((t - IconDelayMs) / IconMs, 0, 1), 0.7);
            c.IconScale.ScaleX = c.IconScale.ScaleY = pop;
            // One accent-glow flare: 24 -> 46 -> 24 blur, 0.5 -> 0.9 -> 0.5 opacity (AutoReverse).
            double u = Math.Min(1, t / (2 * FlareMs));
            double f = SineInOut(u < 0.5 ? u * 2 : (1 - u) * 2);
            c.Root.BoxShadow = Glow(c.Accent, 24 + 22 * f, 0.5 + 0.4 * f);
        }

        /// <summary>WPF BackEase EaseOut: 1 - g(1 - t), g(x) = x^3 - x a sin(pi x).</summary>
        internal static double BackOut(double t, double a)
        {
            double x = 1 - t;
            return 1 - (x * x * x - x * a * Math.Sin(Math.PI * x));
        }

        private static double SineInOut(double t) => (1 - Math.Cos(Math.PI * t)) / 2;

        private static BoxShadows Glow(Color c, double blur, double opacity) =>
            new(new BoxShadow { Blur = blur, Color = Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B) });

        private sealed class Card
        {
            public required Border Root;
            public required TranslateTransform Slide;
            public required ScaleTransform IconScale;
            public Color Accent;
            public double Start;
        }

        /// <summary>WPF ChaosUnlockCards.BuildCardVisual, control for control.</summary>
        private static Card Build(ChaosUnlockCardInfo d, double width)
        {
            var accent = Color.FromRgb((byte)(d.AccentRgb >> 16), (byte)(d.AccentRgb >> 8), (byte)d.AccentRgb);
            var accentBrush = new SolidColorBrush(accent);
            var accentDim = new SolidColorBrush(Color.FromArgb(0xCC, accent.R, accent.G, accent.B));

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = d.Ribbon, Foreground = accentBrush,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontWeight = FontWeight.Bold, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 10),
            });

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("76,*") };
            var iconScale = new ScaleTransform(0.4, 0.4);
            row.Children.Add(new Border
            {
                Width = 64, Height = 64, CornerRadius = new CornerRadius(10),
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(0x28, accent.R, accent.G, accent.B)),
                RenderTransformOrigin = RelativePoint.Center,   // WPF set (0.5, 0.5) explicitly
                RenderTransform = iconScale,
                Child = new TextBlock
                {
                    Text = d.Glyph, FontSize = 30, Foreground = accentBrush,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            });

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = d.Title, Foreground = Brushes.White, FontWeight = FontWeight.Bold,
                FontSize = 17, TextWrapping = TextWrapping.Wrap,
            });
            text.Children.Add(new TextBlock
            {
                Text = d.Desc, Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xEE)),
                FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            });
            if (!string.IsNullOrWhiteSpace(d.Flavor))
                text.Children.Add(new TextBlock
                {
                    Text = d.Flavor, Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xA0, 0xA0, 0xC0)),
                    FontStyle = FontStyle.Italic, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 7, 0, 0),
                });
            if (!string.IsNullOrWhiteSpace(d.Context))
            {
                // WPF "→ " + context: the arrow is a Fluent glyph beside the wrapping line.
                var ctx = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 9, 0, 0) };
                ctx.Children.Add(new IconGlyph
                {
                    Kind = IconKind.ArrowRight, Size = 12, Foreground = accentDim,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 4, 0),
                });
                var line = new TextBlock { Text = d.Context, Foreground = accentDim, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(line, 1);
                ctx.Children.Add(line);
                text.Children.Add(ctx);
            }
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            stack.Children.Add(row);

            var slide = new TranslateTransform(0, -14);
            var root = new Border
            {
                Width = width, Child = stack, Opacity = 0,
                Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x1C, 0x1A, 0x36)),
                BorderBrush = accentDim, BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 14, 18, 16),
                RenderTransform = slide,
                BoxShadow = Glow(accent, 24, 0.5),
            };
            Log.Debug("ChaosHub: unlock card {Ribbon} {Title}", d.Ribbon, d.Title);
            return new Card { Root = root, Slide = slide, IconScale = iconScale, Accent = accent };
        }
    }
}
