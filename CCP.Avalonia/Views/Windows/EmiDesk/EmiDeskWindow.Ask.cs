using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The offer: a question that WAITS under two chips (WPF EmiDeskWindow.Bubble.cs:577-935).
    /// No timer and no give-up: a chip, Esc, a kill moment, presentation and the tear-down are the
    /// only ways out. A line arriving meanwhile parks in ONE slot and speaks after.
    /// </summary>
    public partial class EmiDeskWindow
    {
        private static readonly IBrush ChipHoverFill = new ImmutableSolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4));
        private const double ChipFontAtDefaultWidth = 9.0;
        private const int IgnoreThinkMs = 700;

        /// <summary>WPF Bubble.cs:74: moments that take the whole screen kill a waiting offer.</summary>
        internal static readonly HashSet<string> AskKillMoments = new(StringComparer.Ordinal)
        {
            "videoRunning", "lockdownArmed", "lockdownCountdown", "intakeOpened", "intakeRunning",
            "panicPressed"
        };

        private static readonly FuncControlTemplate<Button> BareChip = new((b, _) => new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            Background = Brushes.Transparent,
            [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
        });

        private StackPanel? _chips;
        private AskDraw? _ask;
        private LineDraw? _parked;
        private DispatcherTimer? _askTimer;
        private int _askStage;
        private double _bubbleClampLo = EmiBubblePlacement.EdgeGap;
        private double _bubbleClampRight = double.PositiveInfinity;

        /// <summary>True while an offer is on screen and still waiting for an answer.</summary>
        public bool AskLive => _ask != null;

        /// <summary>The chips' labels while they are up (test seam).</summary>
        internal IReadOnlyList<Button> AskChips => _chips is { IsVisible: true } c ? c.Children.OfType<Button>().ToList() : Array.Empty<Button>();

        /// <summary>WPF ShowAsk: the . / .. / ... cadence, then the question and two chips, then wait.</summary>
        internal void ShowAsk(AskDraw? ask, EmiLineEngine engine)
        {
            if (PresentationActive || !IsVisible || ask == null) return;
            try
            {
                EnsureBubble();
                CancelHold();
                CancelChain();
                StopIdleBeats();
                EndAskTimer();
                _ask = ask;
                engine.Ack(ask.Id);
                _askStage = 0;
                DrawFace(RestFace);
                ShowBubble(".");
                _askTimer = AskTimer(EmiSpeechCadence.Dot1Ms, AskCadenceStep);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ShowAsk({Id}) failed", ask.Id);
                _ask = null;
            }
        }

        private void AskCadenceStep()
        {
            var ask = _ask;
            if (ask == null) return;
            switch (++_askStage)
            {
                case 1:
                    ShowBubble("..");
                    _askTimer = AskTimer(EmiSpeechCadence.Dot2Ms, AskCadenceStep);
                    break;
                case 2:
                    ShowBubble("...");
                    _askTimer = AskTimer(EmiSpeechCadence.Dot3Ms, AskCadenceStep);
                    break;
                default:
                    _askTimer = null;
                    DrawFace(string.IsNullOrEmpty(ask.Face) ? "^_^" : ask.Face);
                    ShowBubble(ask.Question);
                    BuildChips(ask);
                    Log.Information("[EmiDesk] offer {Id} is up, waiting", ask.Id);
                    break;
            }
        }

        private void BuildChips(AskDraw ask)
        {
            if (_chips == null)
            {
                _chips = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, IsVisible = false };
                _bubbleCanvas.Children.Add(_chips);
            }
            _chips.Children.Clear();
            IReadOnlyList<string> labels = ask.Chips is { Count: >= 2 } ? ask.Chips : new[] { "yes", "no" };
            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                var b = MakeChip(labels[i]);
                b.Click += (_, e) => { e.Handled = true; AnswerAsk(idx); };
                _chips.Children.Add(b);
            }
            _chips.IsVisible = true;
            // WPF :660: the bubble layer opens to input only while there are chips to click.
            _bubbleCanvas.IsHitTestVisible = true;
            LayoutBubble();
            try { _chips.Children.OfType<Button>().FirstOrDefault()?.Focus(); }
            catch { /* focus is a nicety */ }
        }

        private Button MakeChip(string? label)
        {
            double fs = Math.Max(ChipFontAtDefaultWidth, Math.Round(ChipFontAtDefaultWidth * BodyWidth / BubbleFontRefWidth));
            // A TextBlock, not string Content: Avalonia reads '_' in a Button's string as an access key.
            var shell = new Border
            {
                Background = BubbleFill,
                BorderBrush = BubbleInk,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(label) ? "ok" : label!.Trim(),
                    FontFamily = PixelFont,
                    FontSize = fs,
                    Foreground = BubbleInk,
                    Margin = new Thickness(7, 4, 7, 4)
                }
            };
            var b = new Button
            {
                Content = shell,
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Focusable = true,
                Template = BareChip
            };
            b.PointerEntered += (_, _) => shell.Background = ChipHoverFill;
            b.PointerExited += (_, _) => shell.Background = BubbleFill;
            return b;
        }

        /// <summary>WPF LayoutChips: under the bubble's edge (right-aligned when flipped), clamped like the bubble.</summary>
        private void LayoutChips(double bubbleLeft, double bubbleBottom, double bubbleWidth, bool flip)
        {
            if (_chips is not { IsVisible: true }) return;
            _chips.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = _chips.DesiredSize.Width;
            double x = flip ? bubbleLeft + bubbleWidth - w : bubbleLeft;
            double lo = _bubbleClampLo;
            double hi = double.IsInfinity(_bubbleClampRight) ? x : Math.Max(lo, _bubbleClampRight - w);
            x = hi < lo ? lo : Math.Max(lo, Math.Min(hi, x));
            Canvas.SetLeft(_chips, Math.Round(x));
            Canvas.SetTop(_chips, Math.Round(bubbleBottom + 12));
        }

        private void HideChips()
        {
            if (_chips != null)
            {
                _chips.Children.Clear();
                _chips.IsVisible = false;
            }
            _bubbleCanvas.IsHitTestVisible = false;
        }

        /// <summary>WPF AnswerAsk: a chip was clicked, the only answer that counts. Reply first, then the effect.</summary>
        internal void AnswerAsk(int index)
        {
            var ask = _ask;
            if (ask == null) return;
            try
            {
                bool yes = index == 0;
                var reply = yes ? ask.Yes : ask.No;
                string? effect = yes ? ask.Effect : ask.EffectNo;
                Log.Information("[EmiDesk] offer {Id} answered {Answer} -> {Effect}", ask.Id,
                    yes ? "yes" : "no", string.IsNullOrEmpty(effect) ? "none" : effect);
                _ask = null;
                EndAskTimer();
                HideChips();
                HideBubble();
                var desk = EmiDeskService.Instance;
                desk.LineEngine.NoteAskAnswered();
                desk.Fire("askAnswered", new { answer = yes ? "yes" : "no" });
                if (reply != null) SpeakLine(reply, desk.LineEngine);
                if (!string.IsNullOrEmpty(effect)) EmiOffers.Run(effect, fromAsk: true);
                ReleaseParked();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] answering offer {Id} failed", ask.Id);
                _ask = null;
                EndAskTimer();
                HideChips();
            }
        }

        /// <summary>WPF CancelAsk: gone unanswered. A <c>-_-</c>, a beat of thinking, then she drops it.</summary>
        internal void CancelAsk(string why) => EndAskUnanswered(why, visual: true);

        private void EndAskUnanswered(string why, bool visual)
        {
            var ask = _ask;
            if (ask == null) return;
            try
            {
                Log.Information("[EmiDesk] offer {Id} ignored ({Why})", ask.Id, why);
                _ask = null;
                EndAskTimer();
                HideChips();
                HideBubble();
                var desk = EmiDeskService.Instance;
                desk.LineEngine.NoteAskIgnored();
                desk.Fire("askIgnored", new { });
                if (!visual) return;
                CancelChain();
                DrawFace("-_-");
                _askTimer = AskTimer(IgnoreFaceMs, () =>
                {
                    ShowBubble("...");
                    _askTimer = AskTimer(IgnoreThinkMs, () =>
                    {
                        EndAskTimer();
                        HideBubble();
                        DrawFace(RestFace);
                        RestartIdleBeats();
                        ReleaseParked();
                    });
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] cancelling offer {Id} failed", ask.Id);
                _ask = null;
                EndAskTimer();
                HideChips();
            }
        }

        private void ReleaseParked()
        {
            var line = _parked;
            _parked = null;
            if (line == null) return;
            // WPF :853: a short beat so it does not step on the reply's own first frame.
            _askTimer = AskTimer(900, () => SpeakLine(line, EmiDeskService.Instance.LineEngine));
        }

        private void OnAskKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || !AskLive) return;
            e.Handled = true;
            CancelAsk("escape");
        }

        /// <summary>The pending offer step, run now. The timers and the headless tests drive the same steps.</summary>
        internal void AdvanceAsk()
        {
            if (_askTimer == null) return;
            _askTimer.Stop();
            _askStep?.Invoke();
        }

        private Action? _askStep;

        private DispatcherTimer AskTimer(int ms, Action step)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
            void Run()
            {
                if (!ReferenceEquals(_askTimer, t)) return;
                _askTimer = null;
                try { step(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] offer timer step failed"); }
            }
            _askStep = Run;
            t.Tick += (_, _) => { t.Stop(); Run(); };
            t.Start();
            return t;
        }

        private void EndAskTimer()
        {
            _askTimer?.Stop();
            _askTimer = null;
        }

        /// <summary>WPF TearDownBubble: the bubble, the chips and any waiting question go, without the reaction.</summary>
        private void TearDownAsk()
        {
            try
            {
                EndAskUnanswered("teardown", visual: false);
                _ask = null;
                _parked = null;
                EndAskTimer();
                HideChips();
                HideBubble();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bubble tear-down failed"); }
        }
    }
}
