// PORTED from ConditioningControlPanel/MainWindow/MainWindow.MarqueeReads.cs (the Barnum interludes) and
// Services/Banner/MarqueeReadEffects.cs (the five acts). Same as WPF: a loop clock keyed to one scroll period (45 s while
// the strip is parked) reports loops into the Core MarqueeReadCadence (every 4th loop, 2 min after launch, 3 min apart;
// fast mode 1 loop / 20 s); the gates are BannerPoolEnabled, the motion transitions gate and the strip on screen; a line
// comes from the Core BannerPoolService "reads" bucket, is split by MarqueeReadSplit and an act is drawn by
// MarqueeReadActs.PickId over MarqueeReadActs.CanPlay; the ticker pauses and fades out 280 ms, the act plays (glitch heal,
// number first, teletype, neon warm-up, redaction - WPF's timings, fonts and colours), the stage fades out 280 ms, the
// ticker resumes where it stopped and the shell's support-banner sheen sweeps once (MainShellWindow.SweepBannerSheen). Flicker and jitter frames need motion + ambient loops
// and not Photosafe (WPF MarqueeReadStage.AllowFlicker). A rebuild, a hide or a detach cancels the act.
// Differs: every act is a pure function of elapsed time on a BeatLoop (P08/P82), not Storyboards and Task.Delay; the
// strip glow is a baked blur per word (GlowBake), not a DropShadowEffect on the Viewbox (P82: no live Effect), so the
// redaction bars carry no halo of their own.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Banner;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>The interludes on one marquee strip (WPF MainWindow.MarqueeReads.cs).</summary>
    internal sealed class MarqueeInterlude
    {
        /// <summary>WPF ReadFadeMs: ticker fade out / stage fade back.</summary>
        internal const double ReadFadeMs = 280;

        /// <summary>WPF ParkedLoopSeconds: the loop clock while the strip is parked static.</summary>
        internal const double ParkedLoopSeconds = 45;

        // WPF PossAnim.ToAsync waits the fade plus 20 ms of slack before the next step.
        internal const double FadeStepMs = ReadFadeMs + 20;

        private readonly MarqueeScroller _ticker;
        private readonly Control _canvas;
        private readonly Panel _host;
        private readonly TranslateTransform _slide;
        private readonly BeatLoop _act;
        private readonly DispatcherTimer _loopTimer;
        private readonly BannerPoolService _pool = new();
        private readonly Random _rng = new();
        private ReadAct? _play;
        private string? _lastAct;
        private bool _armed;

        public MarqueeInterlude(MarqueeScroller ticker, Control canvas, Panel host)
        {
            _ticker = ticker;
            _canvas = canvas;
            _host = host;
            _slide = host.RenderTransform as TranslateTransform ?? new TranslateTransform();
            host.RenderTransform = _slide;
            bool fast = CoreSettings.Current.MarqueeReadDebugFast
                        || string.Equals(Environment.GetEnvironmentVariable("CCP_MARQUEE_READS_FAST"), "1", StringComparison.Ordinal);
            Cadence = new MarqueeReadCadence { Fast = fast };
            if (fast) Serilog.Log.Information("[MarqueeReads] fast cadence armed (desk mode)");
            _act = new BeatLoop(host, Step) { OnParked = Cancel };
            _loopTimer = new DispatcherTimer(DispatcherPriority.Background);
            _loopTimer.Tick += (_, _) => OnLoop();
            // WPF MarqueeRead_IsVisibleChanged stops the loop clock on hide and restarts it on show. Avalonia's
            // IsVisible is not effective, so watch the strip and its ancestors (the tab hosts toggle IsVisible).
            host.AttachedToVisualTree += (_, _) => Watch();
            host.DetachedFromVisualTree += (_, _) => Unwatch();
            if (host.IsAttachedToVisualTree()) Watch();
        }

        private Action? _unwatch;

        private void Watch()
        {
            Unwatch();
            var chain = _host.GetSelfAndVisualAncestors().ToArray();
            void On(object? s, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Visual.IsVisibleProperty) SyncLoop();
            }
            foreach (var a in chain) a.PropertyChanged += On;
            _unwatch = () => { foreach (var a in chain) a.PropertyChanged -= On; };
        }

        private void Unwatch()
        {
            _unwatch?.Invoke();
            _unwatch = null;
        }

        /// <summary>The loop clock runs only while armed, idle and on screen.</summary>
        private void SyncLoop()
        {
            if (_armed && !IsPlaying && _host.IsEffectivelyVisible) _loopTimer.Start();
            else _loopTimer.Stop();
        }

        /// <summary>The rule (tests swap in one on a stepped clock).</summary>
        internal MarqueeReadCadence Cadence { get; set; }

        /// <summary>The act table (WPF MarqueeReadEffects.Build); tests narrow it to one act.</summary>
        internal IReadOnlyList<string> Acts { get; set; } = MarqueeReadActs.All;

        internal bool IsPlaying { get; private set; }

        /// <summary>The act on stage and its length from the end of the ticker fade (tests).</summary>
        internal string? ActId => _play?.Id;
        internal double ActMs => _play?.Ms ?? 0;

        /// <summary>The armed loop period, or null while the clock is stopped (tests).</summary>
        internal TimeSpan? LoopInterval => _loopTimer.IsEnabled ? _loopTimer.Interval : null;

        /// <summary>The line on stage, body and tag joined (tests).</summary>
        internal string StageText { get; private set; } = "";

        /// <summary>WPF ArmMarqueeReadClock: one tick per completed loop of the message now on the strip.</summary>
        public void Arm(double loopSeconds)
        {
            double seconds = loopSeconds > 0.5 ? loopSeconds : ParkedLoopSeconds;
            _loopTimer.Interval = TimeSpan.FromSeconds(Math.Min(600, seconds));
            _armed = true;
            SyncLoop();
        }

        /// <summary>WPF ShutdownMarqueeReads: no clock left pointing at a detached strip.</summary>
        public void Disarm()
        {
            _armed = false;
            _loopTimer.Stop();
        }

        /// <summary>WPF MarqueeReadLoop_Tick. A hidden strip counts no loops (WPF stops the timer on hide).</summary>
        internal void OnLoop()
        {
            try
            {
                if (IsPlaying || !_host.IsEffectivelyVisible) return;
                if (!Cadence.NoteLoop()) return;
                if (!CoreSettings.Current.BannerPoolEnabled || !AmbientFxCanvas.Env.AllowTransitions)
                {
                    Cadence.NoteSkipped();
                    return;
                }
                Play();
            }
            catch (Exception ex) { Serilog.Log.Debug("[MarqueeReads] loop tick: {E}", ex.Message); }
        }

        /// <summary>WPF RunMarqueeInterludeAsync: take the stage, play one act, give the ticker back.</summary>
        private void Play()
        {
            var line = _pool.NextLine(BannerPoolService.ReadsBucket);
            if (string.IsNullOrWhiteSpace(line)) { Cadence.NoteSkipped(); return; }

            var beats = MarqueeReadSplit.Split(line);
            var chosen = MarqueeReadActs.PickId(Acts, _lastAct, id => MarqueeReadActs.CanPlay(id, beats.Body), _rng.Next);
            if (chosen == null) { Cadence.NoteSkipped(); return; }

            IsPlaying = true;
            Cadence.NotePlayed();
            _lastAct = chosen;
            _loopTimer.Stop();
            Serilog.Log.Debug("[MarqueeReads] act {Act} (tag={HasTag})", chosen, beats.HasTag);

            ClearStage();
            var env = AmbientFxCanvas.Env.AllowAmbientLoops;
            var stage = new ReadStage(_host, _slide, _rng, ambient: env,
                flicker: AmbientFxCanvas.Env.AllowTransitions && env && !CoreSettings.Current.LockdownPhotosafe);
            _play = ReadAct.Create(chosen, stage, beats.Body, beats.Tag);
            StageText = (beats.Body + " " + beats.Tag).Trim();
            _host.Opacity = 0;
            _ticker.Pause();
            _act.Start();
        }

        /// <summary>WPF PossAnim.EaseOut: CubicEase EaseOut.</summary>
        internal static double Ease(double x) => 1 - Math.Pow(1 - Math.Clamp(x, 0, 1), 3);

        /// <summary>The whole interlude as a function of elapsed time: ticker fade, the act, stage fade.</summary>
        private void Step(double t)
        {
            if (_play == null) return;
            double ms = t * 1000;
            if (ms < FadeStepMs)
            {
                _canvas.Opacity = 1 - Ease(ms / ReadFadeMs);
                return;
            }
            _canvas.Opacity = 0;
            double a = ms - FadeStepMs;
            _play.At(Math.Min(a, _play.Ms));
            if (a < _play.Ms) { _host.Opacity = 1; return; }
            _host.Opacity = 1 - Ease((a - _play.Ms) / ReadFadeMs);
            if (a >= _play.Ms + FadeStepMs) Restore();
        }

        /// <summary>WPF CancelMarqueeInterlude: stop whatever is on stage and put the ticker back.</summary>
        public void Cancel()
        {
            if (IsPlaying) Restore();
        }

        /// <summary>WPF RestoreTicker: synchronous and unconditional, the cancel path included.</summary>
        private void Restore()
        {
            try
            {
                IsPlaying = false;
                _act.Stop();
                ClearStage();
                _host.Opacity = 0;
                _canvas.Opacity = 1;
                _ticker.Resume();
                // WPF RestoreTicker: one sheen pass over the support banner as the ticker comes back.
                (TopLevel.GetTopLevel(_host) as Windows.MainShellWindow)?.SweepBannerSheen(force: true);
                SyncLoop();
            }
            catch (Exception ex) { Serilog.Log.Debug("[MarqueeReads] restore: {E}", ex.Message); }
        }

        /// <summary>WPF MarqueeReadStage.Clear: drop the act, its baked glows and the strip nudge.</summary>
        private void ClearStage()
        {
            _play = null;
            _slide.X = 0;
            foreach (var image in _host.GetVisualDescendants().OfType<Image>().ToList())
            {
                (image.Source as IDisposable)?.Dispose();
                image.Source = null;
            }
            _host.Children.Clear();
        }
    }

    // ================================================================================== the stage

    /// <summary>WPF MarqueeReadStage: the host, its nudge transform, the theme ink and the flicker gate.</summary>
    internal sealed class ReadStage
    {
        public ReadStage(Panel host, TranslateTransform slide, Random rng, bool ambient, bool flicker)
        {
            (Host, Slide, Rng, AllowAmbient, AllowFlicker) = (host, slide, rng, ambient, flicker);
            Pink = host.TryFindResource("PinkBrush", out var p) && p is IBrush pink ? pink : new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
            Glow = host.TryFindResource("DarkPinkBrush", out var g) && g is ISolidColorBrush dark ? dark.Color : Color.FromRgb(0xC7, 0x1E, 0x7A);
        }

        public Panel Host { get; }
        public TranslateTransform Slide { get; }
        public Random Rng { get; }
        public bool AllowAmbient { get; }

        /// <summary>WPF AllowFlicker: motion, ambient loops and not Photosafe. Every jitter and flicker hangs off it.</summary>
        public bool AllowFlicker { get; }

        public IBrush Pink { get; }
        public Color Glow { get; }

        public static readonly FontFamily Fredoka = new(AppFonts.Fredoka + ", Segoe UI");

        /// <summary>One glowing word: a cell holding the baked glow under the text.</summary>
        public ReadWord Word(string text, FontFamily font, double size, FontWeight weight, IBrush? ink = null,
                             double blur = 8, double glow = 0.8)
        {
            var block = new TextBlock
            {
                Text = text, FontFamily = font, FontSize = size, FontWeight = weight, Foreground = ink ?? Pink,
                TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center,
            };
            var w = new ReadWord(new Grid { VerticalAlignment = VerticalAlignment.Center }, block, text, blur,
                                 new SolidColorBrush(Glow));
            if (blur > 0)
            {
                w.GlowImage = new Image { Stretch = Stretch.None, Opacity = glow, IsHitTestVisible = false };
                w.Cell.Children.Add(w.GlowImage);
                w.Bake();
            }
            w.Cell.Children.Add(block);
            return w;
        }

        /// <summary>WPF BuildStrip: one cell per word; the tag's words built up front so the strip is already at its
        /// final width when the body lands. <paramref name="blur"/> 0 = no glow (the glitch's ghosts).</summary>
        public ReadStrip Strip(string body, string tag, FontFamily font, double size, FontWeight weight,
                               bool upper = false, IBrush? ink = null, double blur = 8)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var strip = new ReadStrip();
            double gap = Math.Max(3, size * 0.34);
            foreach (var (text, isTag) in Split(body).Select(w => (w, false)).Concat(Split(tag).Select(w => (w, true))))
            {
                var w = Word(upper ? text.ToUpperInvariant() : text, font, size, weight, ink, blur);
                w.IsTag = isTag;
                w.Cell.Margin = new Thickness(0, 0, gap, 0);
                panel.Children.Add(w.Cell);
                strip.Words.Add(w);
            }
            strip.Box = new Viewbox
            {
                Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 2, 14, 2),
            };
            Host.Children.Add(strip.Box);
            return strip;
        }

        private static string[] Split(string text) => (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>WPF ReadWord: one word of a read and the pieces the acts animate.</summary>
    internal sealed class ReadWord(Grid cell, TextBlock block, string word, double blur, IBrush glowInk)
    {
        public Grid Cell { get; } = cell;
        public TextBlock Block { get; } = block;
        public string Text { get; } = word;
        public bool IsTag { get; set; }
        public Image? GlowImage { get; set; }
        public Rectangle? Bar { get; set; }

        /// <summary>Show <paramref name="text"/>, re-baking the glow under it when it changed and <paramref name="bake"/>
        /// (rolling and typing re-bake only now and then: each bake is a RenderTargetBitmap).</summary>
        public void Show(string text, bool bake = true)
        {
            if (Block.Text == text) return;
            Block.Text = text;
            if (bake) Bake();
        }

        public void Bake()
        {
            if (GlowImage == null) return;
            try
            {
                double pad = blur * 2;
                GlowBake.Swap(GlowImage, GlowBake.Text(Block, glowInk, new BlurEffect { Radius = blur }, pad), pad);
            }
            catch (Exception ex) { Serilog.Log.Debug("[MarqueeReads] glow bake: {E}", ex.Message); }
        }
    }

    internal sealed class ReadStrip
    {
        public Viewbox Box { get; set; } = null!;
        public List<ReadWord> Words { get; } = new();
        public IEnumerable<ReadWord> Body => Words.Where(w => !w.IsTag);
        public IEnumerable<ReadWord> Tag => Words.Where(w => w.IsTag);
    }

    // =================================================================================== the acts

    /// <summary>One act as a function of time: <see cref="At"/> gets ms since the ticker faded out, up to <see cref="Ms"/>.</summary>
    internal abstract class ReadAct
    {
        public string Id { get; protected init; } = "";
        public double Ms { get; protected set; }
        public abstract void At(double ms);

        public static ReadAct Create(string id, ReadStage stage, string body, string tag) => id switch
        {
            MarqueeReadActs.GlitchHeal => new GlitchHealAct(stage, body, tag),
            MarqueeReadActs.NumberFirst => new NumberFirstAct(stage, body, tag),
            MarqueeReadActs.Teletype => new TeletypeAct(stage, body, tag),
            MarqueeReadActs.Redaction => new RedactionAct(stage, body, tag),
            _ => new NeonWarmUpAct(stage, body, tag),
        };

        protected static double P(double ms, double at, double len) => Math.Clamp((ms - at) / Math.Max(1, len), 0, 1);
        protected static double EaseOut(double x) => MarqueeInterlude.Ease(x);
        protected static double EaseIn(double x) => Math.Pow(Math.Clamp(x, 0, 1), 3);
        protected static double EaseInOut(double x) => x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
    }

    /// <summary>WPF GlitchHealEffect: the line arrives as rot (Core MarqueeGlyphRot) and heals left to right, a few
    /// characters per 48 ms tick, over cyan/magenta ghosts; three jitter frames while the body heals.</summary>
    internal sealed class GlitchHealAct : ReadAct
    {
        private const double BodyHealMs = 2200, HoldMs = 1200, TagHealMs = 620, TailMs = 900, TickMs = 48;
        private static readonly (double At, double X)[] Jitter =
            { (0, 0), (0.22, -3), (0.26, 0), (0.55, 4), (0.59, 0), (0.81, -2), (0.85, 0) };

        private readonly ReadStage _stage;
        private readonly ReadStrip _a, _b, _strip;
        private readonly (int PerTick, double Ms) _body, _tag;
        private int _bodyDone = -1, _tagDone = -1;

        public GlitchHealAct(ReadStage stage, string body, string tag)
        {
            Id = MarqueeReadActs.GlitchHeal;
            _stage = stage;
            _a = Ghost(Color.FromRgb(0x3B, 0xE8, 0xFF), -2, body, tag);
            _b = Ghost(Color.FromRgb(0xFF, 0x3B, 0xC8), 2, body, tag);
            _strip = stage.Strip(body, tag, ReadStage.Fredoka, 21, FontWeight.SemiBold);
            foreach (var layer in new[] { _a, _b, _strip })
                foreach (var w in layer.Words) { w.Block.Text = MarqueeGlyphRot.Rot(w.Text); if (w.IsTag) w.Cell.Opacity = 0; }
            _body = Plan(_strip.Body, BodyHealMs);
            _tag = Plan(_strip.Tag, TagHealMs);
            Ms = _body.Ms + HoldMs + _tag.Ms + TailMs;
        }

        private ReadStrip Ghost(Color tint, double dx, string body, string tag)
        {
            var g = _stage.Strip(body, tag, ReadStage.Fredoka, 21, FontWeight.SemiBold, ink: new SolidColorBrush(tint), blur: 0);
            g.Box.Opacity = 0.5;
            g.Box.RenderTransform = new TranslateTransform(dx, 0);
            return g;
        }

        /// <summary>WPF Heal's pacing: perTick chars per 48 ms tick, a tick waited after every full batch.</summary>
        private static (int, double) Plan(IEnumerable<ReadWord> words, double totalMs)
        {
            int chars = words.Sum(w => w.Text.Length);
            if (chars == 0) return (1, 0);
            int perTick = Math.Max(1, (int)Math.Ceiling(chars / (double)Math.Max(1, (int)(totalMs / TickMs))));
            return (perTick, chars / perTick * TickMs);
        }

        public override void At(double ms)
        {
            _stage.Slide.X = _stage.AllowFlicker && ms < _body.Ms ? Hold(ms / BodyHealMs) : 0;   // WPF: settled when the body heals
            Heal(false, ms, 0, _body, ref _bodyDone);
            double ghosts = 0.5 * (1 - EaseOut(P(ms, _body.Ms, 420)));
            _a.Box.Opacity = _b.Box.Opacity = ms < _body.Ms ? 0.5 : ghosts;
            double tagAt = _body.Ms + HoldMs;
            if (ms < tagAt) return;
            foreach (var w in _strip.Tag) w.Cell.Opacity = 1;
            Heal(true, ms, tagAt, _tag, ref _tagDone);
        }

        /// <summary>The jitter track is discrete frames: the last key at or before <paramref name="p"/>.</summary>
        private static double Hold(double p) => Jitter.Last(k => k.At <= p).X;

        private void Heal(bool tag, double ms, double at, (int PerTick, double Ms) plan, ref int done)
        {
            var targets = Enumerable.Range(0, _strip.Words.Count).Where(i => _strip.Words[i].IsTag == tag).ToList();
            int total = targets.Sum(i => _strip.Words[i].Text.Length);
            int healed = ms - at >= plan.Ms ? total : Math.Min(total, ((int)((ms - at) / TickMs) + 1) * plan.PerTick);
            if (healed == done) return;
            done = healed;
            int left = healed;
            var layers = tag ? new[] { _strip } : new[] { _a, _b, _strip };
            foreach (var i in targets)
            {
                var word = _strip.Words[i].Text;
                if (left <= 0) break;
                string shown = left >= word.Length ? word : word[..left] + MarqueeGlyphRot.Rot(word[left..]);
                foreach (var layer in layers) layer.Words[i].Show(shown);
                left -= word.Length;
            }
        }
    }

    /// <summary>WPF NumberFirstEffect: the figure rolls up big and centred (odometer, QuadraticEase out), shrinks into
    /// its slot while the sentence fades in around it, then the tag lands.</summary>
    internal sealed class NumberFirstAct : ReadAct
    {
        private const double RollMs = 900, SettleMs = 620, HoldMs = 1200, TagMs = 460, TailMs = 820, BigSize = 46, Size = 21;

        private readonly ReadStage _stage;
        private readonly ReadStrip _strip;
        private readonly ReadWord? _slot, _big;
        private readonly ScaleTransform _scale = new(1, 1);
        private readonly TranslateTransform _drift = new();
        private readonly double _value;
        private (double X, double Y, double Scale)? _target;

        public NumberFirstAct(ReadStage stage, string body, string tag)
        {
            Id = MarqueeReadActs.NumberFirst;
            _stage = stage;
            _strip = stage.Strip(body, tag, ReadStage.Fredoka, Size, FontWeight.SemiBold);
            foreach (var w in _strip.Words) w.Cell.Opacity = 0;
            _slot = _strip.Body.FirstOrDefault(w => MarqueeReadActs.Integer.IsMatch(w.Text));
            if (_slot == null)
            {
                Ms = SettleMs + HoldMs + TagMs + TailMs;   // WPF's plain-fade fallback
                return;
            }
            var match = MarqueeReadActs.Integer.Match(_slot.Text).Value;
            _value = double.TryParse(match, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
            _big = stage.Word("0", ReadStage.Fredoka, BigSize, FontWeight.Bold, blur: 12, glow: 0.9);
            _big.Cell.HorizontalAlignment = HorizontalAlignment.Center;
            _big.Cell.RenderTransformOrigin = RelativePoint.Center;
            _big.Cell.RenderTransform = new TransformGroup { Children = { _scale, _drift } };
            stage.Host.Children.Add(_big.Cell);
            Ms = RollMs + 260 + SettleMs + HoldMs + TagMs + TailMs;
        }

        public override void At(double ms)
        {
            if (_slot == null || _big == null)
            {
                foreach (var w in _strip.Body) w.Cell.Opacity = EaseOut(P(ms, 0, SettleMs));
                foreach (var w in _strip.Tag) w.Cell.Opacity = EaseOut(P(ms, SettleMs + HoldMs, TagMs));
                return;
            }
            double roll = 1 - Math.Pow(1 - P(ms, 0, RollMs), 2);
            var figure = string.Format(CultureInfo.CurrentCulture, "{0:0}", _value * roll);
            _big.Show(figure, bake: roll >= 1 || figure.Length != _big.Block.Text?.Length);   // a new digit, or landed
            double settleAt = RollMs + 260;
            if (ms < settleAt) return;

            var (x, y, scale) = _target ??= Locate();
            double target = Math.Max(0.12, Size * scale / BigSize), p = EaseInOut(P(ms, settleAt, SettleMs));
            _scale.ScaleX = _scale.ScaleY = 1 + (target - 1) * p;
            (_drift.X, _drift.Y) = (x * p, y * p);
            foreach (var w in _strip.Body.Where(w => w != _slot)) w.Cell.Opacity = EaseOut(P(ms, settleAt, SettleMs));

            double handAt = settleAt + SettleMs;
            if (ms < handAt) return;
            _slot.Cell.Opacity = 1;
            _big.Cell.Opacity = 1 - EaseOut(P(ms, handAt, 180));
            foreach (var w in _strip.Tag) w.Cell.Opacity = EaseOut(P(ms, handAt + HoldMs, TagMs));
        }

        /// <summary>WPF Locate: the slot's centre relative to the host's centre, and the Viewbox's scale.</summary>
        private (double, double, double) Locate()
        {
            try
            {
                _stage.Host.UpdateLayout();
                var m = _slot!.Block.TransformToVisual(_stage.Host);
                if (m is not { } t) return (0, 0, 1);
                var c = new Point(_slot.Block.Bounds.Width / 2, _slot.Block.Bounds.Height / 2).Transform(t);
                return (c.X - _stage.Host.Bounds.Width / 2, c.Y - _stage.Host.Bounds.Height / 2, t.M11 > 0 ? t.M11 : 1);
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("[MarqueeReads] number slot not measurable: {E}", ex.Message);
                return (0, 0, 1);
            }
        }
    }

    /// <summary>WPF TeletypeEffect: typed in the pixel face with a blinking block cursor, a dead stop before the tag,
    /// then the tag snaps in whole with a 3 px nudge. Photosafe: steady cursor, no nudge.</summary>
    internal sealed class TeletypeAct : ReadAct
    {
        private const double PerCharMs = 35, TypeCapMs = 2300, StopMs = 1200, TailMs = 1000, Size = 12;
        private static readonly FontFamily Pixel = new("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace");

        private readonly ReadStage _stage;
        private readonly ReadWord _typed, _cursor;
        private readonly string _body, _tag;
        private readonly int _step;
        private readonly double _typeMs;

        public TeletypeAct(ReadStage stage, string body, string tag)
        {
            Id = MarqueeReadActs.Teletype;
            (_stage, _body, _tag) = (stage, body, tag);
            _typed = stage.Word("", Pixel, Size, FontWeight.Normal, blur: 7, glow: 0.75);
            _cursor = stage.Word("█", Pixel, Size, FontWeight.Normal, blur: 7, glow: 0.75);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(_typed.Cell);
            row.Children.Add(_cursor.Cell);
            var line = string.IsNullOrWhiteSpace(tag) ? body : body + " " + tag;
            var probe = new TextBlock { Text = line + "█", FontFamily = Pixel, FontSize = Size };
            probe.Measure(global::Avalonia.Size.Infinity);
            // Pinned to its finished width so the line does not creep sideways as it types (WPF holder).
            var holder = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = Math.Max(40, probe.DesiredSize.Width) };
            holder.Children.Add(row);
            stage.Host.Children.Add(new Viewbox { Child = holder, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(14, 2, 14, 2) });
            _step = (int)Math.Max(1, Math.Ceiling(body.Length * PerCharMs / TypeCapMs));
            _typeMs = Math.Ceiling(body.Length / (double)_step) * PerCharMs * _step;
            Ms = _typeMs + StopMs + TailMs;
        }

        public override void At(double ms)
        {
            // PossAnim.Oscillate(cursor, 1 -> 0.05, 480 ms half period) until the tail ends, then settled at 1.
            _cursor.Cell.Opacity = _stage.AllowFlicker && ms < Ms ? 1 - 0.95 * BeatLoop.Breath(ms / 1000, 0.48) : 1;
            int shown = Math.Min(_body.Length, ((int)(ms / (PerCharMs * _step)) + 1) * _step);
            double tagAt = _typeMs + StopMs;
            bool tag = ms >= tagAt && !string.IsNullOrWhiteSpace(_tag);
            _typed.Show(tag ? _body + " " + _tag : _body[..shown], bake: tag || shown == _body.Length || shown % 8 < _step);
            _stage.Slide.X = tag && _stage.AllowFlicker && ms < tagAt + 180 ? 3 * (1 - EaseOut(P(ms, tagAt, 180))) : 0;
        }
    }

    /// <summary>WPF NeonWarmUpEffect: Impact caps, each word flickering up on its own random schedule (one ease under
    /// Photosafe or motion Off), the tag following, and its last word settling into a slow pink breath.</summary>
    internal sealed class NeonWarmUpAct : ReadAct
    {
        private const double WarmMs = 1800, HoldMs = 1200, TagMs = 700, TailMs = 1200;
        private static readonly FontFamily Impact = new("Impact, Arial Black, Segoe UI");

        private readonly ReadStage _stage;
        private readonly List<(ReadWord Word, double At, double Ms, (double At, double V)[]? Frames)> _words = new();
        private readonly ReadWord? _last;

        public NeonWarmUpAct(ReadStage stage, string body, string tag)
        {
            Id = MarqueeReadActs.NeonWarmUp;
            _stage = stage;
            var strip = stage.Strip(body, tag, Impact, 24, FontWeight.Bold, upper: true);
            var words = strip.Body.ToList();
            for (int i = 0; i < words.Count; i++)
                Add(words[i], words.Count <= 1 ? 0 : WarmMs * 0.6 * i / (words.Count - 1), WarmMs * 0.4);
            foreach (var w in strip.Tag) Add(_last = w, WarmMs + HoldMs, TagMs);
            Ms = WarmMs + HoldMs + TagMs + TailMs;
        }

        /// <summary>WPF WarmUp: discrete 1 / 0.15 frames from 12 % to 82 % of the word's span, then an ease to 1.</summary>
        private void Add(ReadWord w, double at, double ms)
        {
            w.Cell.Opacity = 0;
            if (!_stage.AllowFlicker) { _words.Add((w, at, Math.Max(160, ms), null)); return; }
            var frames = new List<(double, double)> { (0, 0) };
            for (double f = 0.12; f < 0.82; f += 0.07 + _stage.Rng.NextDouble() * 0.1)
                frames.Add((f, _stage.Rng.Next(100) < 55 ? 1 : 0.15));
            _words.Add((w, at, ms, frames.ToArray()));
        }

        internal static double Flicker((double At, double V)[] frames, double p)
        {
            if (p >= 1) return 1;
            var last = frames.Last(k => k.At <= p);
            double end = frames[^1].At;
            return p < end ? last.V : last.V + (1 - last.V) * EaseOut((p - end) / (1 - end));
        }

        public override void At(double ms)
        {
            foreach (var (w, at, len, frames) in _words)
                w.Cell.Opacity = ms < at ? 0 : frames == null ? EaseOut(P(ms, at, len)) : Flicker(frames, P(ms, at, len));
            double breathAt = WarmMs + HoldMs + TagMs;
            // MotionFx.GlowBreath(cell, 0.55, 1.0, 1.7): opacity breathes from 0.55 (steady 1 without ambient loops).
            if (_last != null && ms >= breathAt && ms < Ms)
                _last.Cell.Opacity = _stage.AllowAmbient ? 0.55 + 0.45 * BeatLoop.Breath((ms - breathAt) / 1000, 1.7) : 1;
        }
    }

    /// <summary>WPF RedactionEffect: black bars slide off a word at a time, left to right, the tag's bar last. No
    /// flicker anywhere, so it plays unchanged under Photosafe.</summary>
    internal sealed class RedactionAct : ReadAct
    {
        private const double UncoverMs = 2000, HoldMs = 1200, TagMs = 500, TailMs = 900;

        private readonly List<(ReadWord Word, TranslateTransform Slide, double At, double Ms)> _bars = new();

        public RedactionAct(ReadStage stage, string body, string tag)
        {
            Id = MarqueeReadActs.Redaction;
            var strip = stage.Strip(body, tag, ReadStage.Fredoka, 21, FontWeight.SemiBold);
            var words = strip.Body.ToList();
            double per = words.Count > 0 ? UncoverMs / words.Count : UncoverMs;
            for (int i = 0; i < words.Count; i++) Bar(words[i], i * per, Math.Max(140, per * 1.6));
            foreach (var w in strip.Tag) Bar(w, words.Count * per + HoldMs, TagMs);
            Ms = words.Count * per + HoldMs + TagMs + TailMs;
        }

        private void Bar(ReadWord w, double at, double ms)
        {
            var slide = new TranslateTransform();
            w.Bar = new Rectangle { Fill = Brushes.Black, RadiusX = 2, RadiusY = 2, Margin = new Thickness(-2, -1, -2, -1), RenderTransform = slide };
            w.Cell.Children.Add(w.Bar);
            _bars.Add((w, slide, at, ms));
        }

        public override void At(double ms)
        {
            foreach (var (w, slide, at, len) in _bars)
            {
                double p = EaseIn(P(ms, at, len)), width = w.Cell.Bounds.Width > 0 ? w.Cell.Bounds.Width : w.Text.Length * 14;
                slide.X = (width + 8) * p;
                w.Bar!.Opacity = 1 - p;
            }
        }
    }
}
