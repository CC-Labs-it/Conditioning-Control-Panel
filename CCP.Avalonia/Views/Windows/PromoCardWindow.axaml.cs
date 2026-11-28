using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Announcements;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>PORTED from ConditioningControlPanel/Windows/PromoCardWindow.xaml.cs and .Parts.cs (a59e74ee7e): a server
    /// announcement with a valid <c>promo</c> as the Terms &amp; Conditioning title screen. Not now, the x, Escape, the button
    /// and the offer's end answer it (WPF AnnouncementDismissal.Record); panic closes it without answering. WPF animations
    /// are transitions here. Contract: handoff-1123/BF-PROMO-BRIEF.md.</summary>
    public partial class PromoCardWindow : Window, Services.Startup.IPassiveStartupSurface
    {
        private static readonly List<PromoCardWindow> OpenCards = new();
        private static readonly Color Ink = Color.Parse("#1B1A17"), Pink = Color.Parse("#FF4FA8"), Violet = Color.Parse("#7B5CFF"),
            Gold = Color.Parse("#FFD23F"), Paper = Color.Parse("#F7F5EF"), Plum = Color.Parse("#2A0A1C"), Stamp = Color.Parse("#A3232B"),
            Slate = Color.Parse("#5C5A52");
        private static readonly IBrush HardShadow = new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0));
        private static readonly FontFamily Round = new(Platform.AppFonts.PromoRound), Mono = new(Platform.AppFonts.PromoMono);

        private readonly string _id;
        private readonly PromoCard _promo;
        private readonly Func<DateTimeOffset> _now;
        private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
        private TextBlock? _copyMeta;
        private string _shown = "";
        private bool _leaving;

        /// <summary>The previewer's card; never shown by the app.</summary>
        public PromoCardWindow() : this("preview", "Title", "Message",
            new PromoCard(PromoCardRules.Layout, null, "Hook", "CODE", new[] { new PromoRow("Row", null) }, Array.Empty<Uri>(), null, null)) { }

        /// <param name="stickers">Pictures already in hand (tests); null fetches the promo's URLs.</param>
        public PromoCardWindow(string id, string title, string message, PromoCard promo,
            Func<DateTimeOffset>? now = null, IReadOnlyList<IImage?>? stickers = null)
        {
            InitializeComponent();
            _id = id;
            _promo = promo;
            _now = now ?? (() => ServerClock.UtcNow);
            Title = promo.Hook ?? title;
            Build(title, message);
            if (promo.EndsAt is { } end && end - _now() is var left && left > TimeSpan.Zero)
            {
                _shown = PromoCountdown.Format(left);
                DrawCountdown(left);
            }
            BuildArt(stickers);

            BtnClose.Click += (_, _) => Leave(record: true);
            AutomationProperties.SetName(BtnClose, Loc.Get("btn_close"));
            KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Leave(record: true); } };
            _countdownTimer.Tick += (_, _) => TickCountdown();
            _copiedTimer.Tick += (_, _) => { _copiedTimer.Stop(); if (_copyMeta != null) _copyMeta.Text = Loc.Get("promo_copy_code"); };
            Opened += (_, _) => OnOpened();
            Closed += (_, _) => { OpenCards.Remove(this); _countdownTimer.Stop(); _copiedTimer.Stop(); StopArt(); };
        }

        /// <summary>The art: stickers, spiral, sparkles and the ambient loop. Partial, so the card stands without them.</summary>
        partial void BuildArt(IReadOnlyList<IImage?>? stickers);
        partial void StartArt();
        partial void StopArt();

        /// <summary>Opens a card (one already up for the id goes first); throws only if it cannot be built (old popup then).</summary>
        public static void ShowFor(string id, string title, string message, PromoCard promo)
        {
            foreach (var open in OpenCards.Where(c => c._id == id).ToList()) open.Leave(record: false, quick: true);
            new PromoCardWindow(id, title, message, promo).Show();
        }

        /// <summary>Panic: every open card goes at once, no motion, nothing recorded.</summary>
        internal static void CloseForPanic() => OpenCards.ToList().ForEach(c => c.Leave(record: false, quick: true));

        internal static IReadOnlyList<PromoCardWindow> OpenForTests => OpenCards;

        private static bool Moves => AmbientFxCanvas.Env.AllowTransitions && !GentleVisuals.On;

        private void OnOpened()
        {
            OpenCards.Add(this);
            // Shrink to fit a short screen (a 768 px laptop at 125%), then centre (WPF FitAndCentre).
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                var wa = screen.WorkingArea.ToRect(screen.Scaling);
                if (Bounds.Height > wa.Height - 16)
                {
                    var k = Math.Max(0.6, (wa.Height - 16) / Bounds.Height);
                    Fit.LayoutTransform = new ScaleTransform(k, k);
                }
            }
            if (_promo.EndsAt != null) { TickCountdown(); _countdownTimer.Start(); }
            StartArt();
            PlayIn();
        }

        private static Transitions Fade(int ms, Easing? ease = null) => new()
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms) },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = ease ?? new BackEaseOut() },
        };

        private void PlayIn()
        {
            Opacity = 0;
            Transitions = Fade(300);
            Opacity = 1;
            if (!Moves) return;
            Root.RenderTransform = TransformOperations.Parse("scale(0.92) translateY(18px)");
            Root.Transitions = Fade(460);
            Root.RenderTransform = TransformOperations.Parse("none");
            var i = 0;
            foreach (var row in MenuStack.Children) PopIn(row, 140 + 55 * i++);
        }

        /// <summary>Answers the announcement when <paramref name="record"/>, then fades and sinks out (quick: closes at once).</summary>
        private void Leave(bool record, bool quick = false)
        {
            if (_leaving) return;
            _leaving = true;
            _countdownTimer.Stop();
            StopArt();
            if (record)
            {
                CoreSettings.Current.DismissedAnnouncementId = _id;
                CoreSettings.Save();
                MainShellWindow.RecordAnnouncementDismissal(_id);
            }
            if (quick || !AmbientFxCanvas.Env.AllowTransitions) { Close(); return; }
            var ms = GentleVisuals.On ? 300 : 220;
            Transitions = Fade(ms);
            Opacity = 0;
            if (!GentleVisuals.On)
            {
                Root.Transitions = Fade(ms, new QuadraticEaseIn());
                Root.RenderTransform = TransformOperations.Parse("scale(0.95) translateY(14px)");
            }
            DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(ms));
        }

        private void TickCountdown()
        {
            if (_promo.EndsAt is not { } end) return;
            var left = end - _now();
            // The offer is over: the card goes, and the announcement counts as seen.
            if (left <= TimeSpan.Zero) { Leave(record: true); return; }
            var text = PromoCountdown.Format(left);
            if (text == _shown) return;
            _shown = text;
            DrawCountdown(left);
        }

        private void DrawCountdown(TimeSpan left)
        {
            CountdownPanel.Children.Clear();
            var stamp = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12), RenderTransform = TransformOperations.Parse("rotate(-2deg)") };
            stamp.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)), CornerRadius = new CornerRadius(4), RenderTransform = TransformOperations.Parse("translate(1px, 2px)") });
            stamp.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 2),
                Child = Text(Loc.Get("promo_ends_in").ToUpperInvariant(), Mono, 12, FontWeight.Bold, Stamp, default) });
            CountdownPanel.Children.Add(stamp);

            var cells = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var (value, unit) in PromoCountdown.Parts(left))
            {
                var word = Loc.Get(unit switch { PromoCountdown.Unit.Days => "promo_days", PromoCountdown.Unit.Hours => "promo_hours", _ => "promo_minutes" });
                var face = new StackPanel();
                face.Children.Add(Text(value.ToString("00", CultureInfo.InvariantCulture), Round, 30, FontWeight.SemiBold, Colors.White, new Thickness(0, -2, 0, 0)));
                face.Children.Add(Text(word.ToUpperInvariant(), Mono, 10.5, FontWeight.Bold, Gold, new Thickness(0, 2, 0, 0)));
                var i = cells.Children.Count;
                var cell = new Grid { Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0), MinWidth = 74, RenderTransform = TransformOperations.Parse(i % 2 == 0 ? "rotate(-1.4deg)" : "rotate(1.2deg)") };
                cell.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = HardShadow, RenderTransform = TransformOperations.Parse("translate(2px, 4px)") });
                cell.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Ink), BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(3), Padding = new Thickness(8), Child = face });
                cells.Children.Add(cell);
            }
            CountdownPanel.Children.Add(cells);
            AutomationProperties.SetName(CountdownPanel, $"{Loc.Get("promo_ends_in")} {PromoCountdown.Format(left)}");
        }

        private void OnCta()
        {
            // Checked again by PromoCardRules: absolute https on an allowed host, no quotes, no spaces.
            if (_promo.Cta != null) Platform.ExternalOpener.Open(_promo.Cta.Url.AbsoluteUri);
            Leave(record: true);
        }

        private async void OnCopy()
        {
            try
            {
                if (_promo.Code == null || Clipboard is not { } clip) return;
                await clip.SetTextAsync(_promo.Code);
                if (_copyMeta == null) return;
                _copyMeta.Text = Loc.Get("promo_copied");
                if (Moves && _copyMeta.Parent is Control chip) PopIn(chip, 0, from: 1.25);
                _copiedTimer.Stop();
                _copiedTimer.Start();
            }
            catch (Exception ex) { Log.Debug("Promo code copy: {E}", ex.Message); }
        }

        // ---- parts (WPF PromoCardWindow.Parts.cs; numbers are the concept's, bf50-2026 index.html) ----

        private void Build(string title, string message)
        {
            var hook = _promo.Hook ?? "";
            HookText.FontFamily = TitleText.FontFamily = MessageText.FontFamily = Round;
            TagText.FontFamily = Mono;
            HookText.IsVisible = hook.Length > 0; // no hook: the close coin gets the top row to itself
            if (hook.Length == 0) Body.Margin = new Thickness(22, 54, 22, 22);
            // The hook, its last word under the pink highlighter: a band across the lower half.
            var cut = hook.TrimEnd().LastIndexOf(' ');
            var band = Color.FromArgb(0xA8, 0xFF, 0x69, 0xB4);
            HookText.Inlines = new InlineCollection
            {
                new Run(cut > 0 ? hook[..(cut + 1)] : ""),
                new Run(cut > 0 ? hook[(cut + 1)..] : hook)
                {
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops = { new(Colors.Transparent, 0.5), new(band, 0.5), new(band, 0.88), new(Colors.Transparent, 0.88) },
                    },
                },
            };

            TitleText.Text = title;
            MessageText.Text = message;
            MessageScroll.IsVisible = !string.IsNullOrWhiteSpace(message);
            CountdownPanel.IsVisible = _promo.EndsAt != null;
            HeroBox.IsVisible = false;
            HeroLayout();
            if (!string.IsNullOrWhiteSpace(_promo.Tag)) { TagText.Text = _promo.Tag; TagStamp.IsVisible = true; }

            // The title screen's menu: black Continue row = the code, the offers, the pink button, the dashed "?" row = Not now.
            if (_promo.Code != null)
            {
                var code = Pill(_promo.Code, Loc.Get("promo_copy_code"), Ink, Colors.White, 48, 33, 12.5, new Thickness(20, 0, 13, 0), peel: true);
                _copyMeta = code.Meta;
                MenuStack.Children.Add(Clickable(code.Visual, -1.6, -18, OnCopy, $"{_promo.Code}, {Loc.Get("promo_copy_code")}", 0));
            }
            var fills = _promo.Rows.Count == 1 ? new[] { Violet } : new[] { Pink, Violet, Paper };
            double[] tilts = { 1.4, -1.2, 1.0 }, shifts = { 12, -8, 6 };
            for (var i = 0; i < _promo.Rows.Count; i++)
            {
                var on = fills[i] == Pink ? Plum : fills[i] == Paper ? Ink : Colors.White;
                var pill = Pill(_promo.Rows[i].Label, _promo.Rows[i].Sub, fills[i], on, 32, 18.5, 11.5, new Thickness(15, 0, 9, 0));
                MenuStack.Children.Add(Tilted(pill.Visual, tilts[i], shifts[i], MenuStack.Children.Count == 0 ? 0 : 12));
            }
            if (_promo.Cta != null)
            {
                var cta = Pill(_promo.Cta.Label, Loc.Get("promo_opens_browser"), Pink, Plum, 50, 23, 11.5, new Thickness(24, 0, 14, 0));
                MenuStack.Children.Add(Clickable(cta.Visual, 1.3, 16, OnCta, _promo.Cta.Label, 16));
            }
            MenuStack.Children.Add(Clickable(NotNowPill(), -0.6, 0, () => Leave(record: true), Loc.Get("promo_not_now"), 16));
        }

        /// <summary>Stickers and countdown share a row; either can be absent, and the row goes when both are.</summary>
        private void HeroLayout()
        {
            CountdownPanel.Margin = new Thickness(HeroBox.IsVisible ? 22 : 0, 0, 0, 0);
            HeroRow.IsVisible = HeroBox.IsVisible || CountdownPanel.IsVisible;
        }

        /// <summary>A title-screen row: a pill in a 3 px white ring with a hard 2, 4 shadow, the label and a small tag pill.</summary>
        private static (Control Visual, TextBlock? Meta) Pill(string label, string? meta, Color fill, Color on,
            double height, double size, double metaSize, Thickness pad, bool peel = false)
        {
            var r = new CornerRadius((height + 6) / 2);
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(Text(label, Round, size, FontWeight.SemiBold, on, new Thickness(0, 0, 0, size * 0.06)));
            TextBlock? metaText = null;
            if (!string.IsNullOrWhiteSpace(meta))
            {
                metaText = Text(meta, Round, metaSize, FontWeight.SemiBold, on, default);
                line.Children.Add(new Border
                {
                    Margin = new Thickness(9, 0, 0, 0), Padding = new Thickness(metaSize * 0.75, 2, metaSize * 0.75, 3),
                    CornerRadius = new CornerRadius(99), VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromArgb(0x2E, on.R, on.G, on.B)), Child = metaText,
                });
            }
            var g = new Grid { Height = height + 6 };
            g.Children.Add(new Border { CornerRadius = r, Background = HardShadow, RenderTransform = TransformOperations.Parse("translate(2px, 4px)") });
            g.Children.Add(new Border { CornerRadius = r, Background = new SolidColorBrush(fill), BorderBrush = Brushes.White, BorderThickness = new Thickness(3), Padding = pad, Child = line });
            if (peel)
            {
                // The peeled sticker corner of the code row.
                const string corner = "M 0,0 L 0,13 Q 0,19 6,19 L 19,19 Z";
                g.Children.Add(new Path { Data = Geometry.Parse(corner), Fill = HardShadow, HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -1, 4, 0) });
                g.Children.Add(new Path { Data = Geometry.Parse(corner), Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, 2, 0) });
            }
            return (g, metaText);
        }

        /// <summary>The title screen's dashed "?" row: paper, a dashed slate outline, no shadow.</summary>
        private static Control NotNowPill()
        {
            var g = new Grid { Height = 38 };
            g.Children.Add(new Rectangle
            {
                RadiusX = 19, RadiusY = 19, Fill = new SolidColorBrush(Color.FromArgb(0xD1, Paper.R, Paper.G, Paper.B)),
                Stroke = new SolidColorBrush(Slate), StrokeThickness = 2.5, StrokeDashArray = new AvaloniaList<double> { 2.4, 1.6 },
            });
            g.Children.Add(Text(Loc.Get("promo_not_now"), Round, 16, FontWeight.SemiBold, Slate, new Thickness(24, 0, 24, 1)));
            return g;
        }

        /// <summary>Tilts a row and shifts it sideways, the menu's hand-placed look.</summary>
        private static Control Tilted(Control el, double tilt, double dx, double gapAbove)
        {
            el.Margin = new Thickness(0, gapAbove, 0, 0);
            el.HorizontalAlignment = HorizontalAlignment.Center;
            el.Tag = (tilt, dx);
            el.RenderTransform = Pose(el, 1, false);
            return el;
        }

        private static ITransform Pose(Control el, double scale, bool straight)
        {
            var (tilt, dx) = el.Tag is (double t, double x) ? (t, x) : (0d, 0d);
            return TransformOperations.Parse(string.Format(CultureInfo.InvariantCulture,
                "translateX({0}px) rotate({1}deg) scale({2})", dx, straight ? 0 : tilt, scale));
        }

        /// <summary>A tilted row that is a button: hover or focus lifts it straight and a little bigger, a press squashes it.</summary>
        private Button Clickable(Control visual, double tilt, double dx, Action click, string name, double gapAbove)
        {
            var b = new Button { Theme = (global::Avalonia.Styling.ControlTheme)Resources["PromoBareButton"]!, Content = visual };
            Tilted(b, tilt, dx, gapAbove);
            AutomationProperties.SetName(b, name);
            b.Click += (_, _) => click();
            b.PropertyChanged += (_, e) =>
            {
                if (e.Property != IsPointerOverProperty && e.Property != Button.IsPressedProperty && e.Property != IsFocusedProperty) return;
                var lifted = b.IsPointerOver || b.IsFocused;
                b.Transitions = AmbientFxCanvas.Env.AllowTransitions ? Fade(200, new BackEaseOut()) : null;
                b.RenderTransform = Pose(b, b.IsPressed ? 0.96 : lifted ? 1.07 : 1, lifted || b.IsPressed);
            };
            return b;
        }

        /// <summary>Pops an element in from <paramref name="from"/> scale (and from clear, for an entrance).</summary>
        private static void PopIn(Control el, int delayMs, double from = 0.6)
        {
            el.Transitions = null;
            el.RenderTransform = Pose(el, from, false);
            if (from < 1) el.Opacity = 0;
            DispatcherTimer.RunOnce(() =>
            {
                el.Transitions = Fade(360);
                el.RenderTransform = Pose(el, 1, false);
                el.Opacity = 1;
            }, TimeSpan.FromMilliseconds(Math.Max(1, delayMs)));
        }

        private static TextBlock Text(string text, FontFamily family, double size, FontWeight weight, Color color, Thickness margin) => new()
        {
            Text = text, FontFamily = family, FontSize = size, FontWeight = weight, Margin = margin, Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
    }
}
