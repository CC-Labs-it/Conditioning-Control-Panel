using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// PORTED from ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.Bubble.cs:580-910 (the offer):
// a question on the . / .. / ... cadence, two chips, and NO timer. The chips, Esc, the hover x
// (dismiss tears it down) and a full-screen feature (AskKillMoments) are the only ways out.
public partial class EmiDeskWindow
{
    private const int AskDot1Ms = 420, AskDot2Ms = 420, AskDot3Ms = 520;   // WPF Bubble.cs:66-68
    private const int IgnoreFaceMs = 1400, IgnoreThinkMs = 700;            // WPF Bubble.cs:69-70
    private const double ChipFontAtDefaultWidth = 9.0;                     // WPF Bubble.cs:64

    /// <summary>WPF Bubble.cs:77: these moments mean a full-screen thing took over; the question goes.</summary>
    internal static readonly HashSet<string> AskKillMoments = new(StringComparer.Ordinal)
    {
        "videoRunning", "lockdownArmed", "lockdownCountdown", "intakeOpened", "intakeRunning", "panicPressed"
    };

    private AskDraw? _ask;
    private LineDraw? _parked;          // ONE slot. A second line while an ask waits is dropped.
    private DispatcherTimer? _askTimer;
    private Action? _askStep;
    private int _askStage;
    private StackPanel? _chips;
    private bool _askKeysHooked;

    /// <summary>True while an offer is on screen and still waiting for an answer.</summary>
    public bool AskLive => _ask != null;

    /// <summary>The live chip buttons (tests).</summary>
    internal IReadOnlyList<Button> AskChips => _chips?.Children.OfType<Button>().ToList() ?? new List<Button>();

    private static EmiLineEngine Engine => EmiDeskService.Instance.LineEngine;

    /// <summary>WPF ShowAsk: put a question up and WAIT.</summary>
    internal void ShowAsk(AskDraw? ask)
    {
        if (PresentationActive || ask == null) return;
        try
        {
            CancelChain();
            StopIdleBeats();
            EndAskTimer();
            HookAskKeys();

            _ask = ask;
            Engine.Ack(ask.Id);

            _askStage = 0;
            DrawFace(RestFace);
            PaintBubble(".");
            StartAskTimer(AskDot1Ms, AskCadenceStep);
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
        _askStage++;
        switch (_askStage)
        {
            case 1: PaintBubble(".."); StartAskTimer(AskDot2Ms, AskCadenceStep); break;
            case 2: PaintBubble("..."); StartAskTimer(AskDot3Ms, AskCadenceStep); break;
            default:
                DrawFace(string.IsNullOrEmpty(ask.Face) ? "^_^" : ask.Face);
                PaintBubble(ask.Question);
                BuildChips(ask);
                Log.Information("[EmiDesk] offer {Id} is up, waiting", ask.Id);
                break;
        }
    }

    private void BuildChips(AskDraw ask)
    {
        if (_chips == null)
        {
            _chips = new StackPanel { Orientation = Orientation.Horizontal };
            BubbleHost.Children.Add(_chips);
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
        // WPF Bubble.cs:661: the layer is hit-testable only while there are chips to click.
        BubbleHost.IsHitTestVisible = true;
        PlaceSpeechBubble();
        try { _chips.Children.OfType<Button>().FirstOrDefault()?.Focus(); }
        catch { /* focus is a nicety; Esc still works once a chip is clicked */ }
    }

    private Button MakeChip(string? label)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
        var fill = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x32));
        var hover = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4));
        var shell = new Border
        {
            Background = fill, BorderBrush = ink, BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(label) ? "ok" : label.Trim(),
                FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace"),
                FontSize = Math.Max(ChipFontAtDefaultWidth, Math.Round(ChipFontAtDefaultWidth * BodyWidth / 220.0)),
                Foreground = ink, Margin = new Thickness(7, 4, 7, 4)
            }
        };
        var b = new Button
        {
            Content = shell, Margin = new Thickness(0, 0, 5, 0), Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand), Focusable = true,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            // WPF BareButtonTemplate: the chip's whole look is its content.
            Template = new FuncControlTemplate<Button>((btn, _) => new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [!ContentPresenter.ContentProperty] = btn[!ContentControl.ContentProperty]
            })
        };
        b.PointerEntered += (_, _) => shell.Background = hover;
        b.PointerExited += (_, _) => shell.Background = fill;
        return b;
    }

    /// <summary>WPF LayoutChips: under the bubble, following its edge, clamped like the bubble.</summary>
    private void LayoutChips(double bubbleLeft, double bubbleBottom, double bubbleWidth, bool flip,
        double workLeft, double workRight)
    {
        if (_chips?.IsVisible != true) return;
        _chips.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = _chips.DesiredSize.Width;
        double x = flip ? bubbleLeft + bubbleWidth - w : bubbleLeft;
        double lo = EmiBubblePlacement.EdgeGap;
        double right = Width - EmiBubblePlacement.EdgeGap;
        if (!double.IsInfinity(workLeft)) lo = Math.Max(lo, workLeft + EmiBubblePlacement.EdgeGap);
        if (!double.IsInfinity(workRight)) right = Math.Min(right, workRight - EmiBubblePlacement.EdgeGap);
        double hi = Math.Max(lo, right - w);
        x = Math.Max(lo, Math.Min(hi, x));
        Canvas.SetLeft(_chips, Math.Round(x));
        Canvas.SetTop(_chips, Math.Round(bubbleBottom + 12));
    }

    private void HideChips()
    {
        if (_chips != null) { _chips.Children.Clear(); _chips.IsVisible = false; }
        BubbleHost.IsHitTestVisible = false;
    }

    /// <summary>WPF AnswerAsk: a chip was clicked. The only answer that counts as an answer.</summary>
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
            HideSpeechBubble();

            var service = EmiDeskService.Instance;
            service.LineEngine.NoteAskAnswered();
            service.Fire("askAnswered", new { answer = yes ? "yes" : "no" });
            // The reply first, then the effect; fromAsk keeps the effect's moment from a second line.
            if (reply != null) SpeakRingLine(reply, service.LineEngine);
            if (!string.IsNullOrEmpty(effect)) service.RunEffect(effect, fromAsk: true);
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

    /// <summary>WPF CancelAsk: the question went away unanswered; a -_-, a beat, then she drops it.</summary>
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
            HideSpeechBubble();

            EmiDeskService.Instance.LineEngine.NoteAskIgnored();
            EmiDeskService.Instance.Fire("askIgnored", new { });
            if (!visual) return;

            CancelChain();
            DrawFace("-_-");
            StartAskTimer(IgnoreFaceMs, () =>
            {
                PaintBubble("...");
                StartAskTimer(IgnoreThinkMs, () =>
                {
                    EndAskTimer();
                    HideSpeechBubble();
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

    /// <summary>WPF TearDownBubble: she is going away; the question goes with no reaction.</summary>
    private void TearDownAsk()
    {
        try
        {
            EndAskUnanswered("teardown", visual: false);
            _ask = null;
            _parked = null;
            EndAskTimer();
            HideChips();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ask tear-down failed"); }
    }

    /// <summary>WPF ReleaseParked: the one line that arrived behind the question has its turn.</summary>
    private void ReleaseParked()
    {
        var line = _parked;
        _parked = null;
        if (line == null) return;
        StartAskTimer(900, () => { EndAskTimer(); SpeakRingLine(line, Engine); });
    }

    private void HookAskKeys()
    {
        if (_askKeysHooked) return;
        _askKeysHooked = true;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || !AskLive) return;
            e.Handled = true;
            CancelAsk("escape");
        }, RoutingStrategies.Tunnel);
    }

    private void StartAskTimer(int ms, Action step)
    {
        EndAskTimer();
        _askStep = step;
        _askTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        _askTimer.Tick += (_, _) => StepAsk();
        _askTimer.Start();
    }

    /// <summary>Test seam (P08): run the pending ask step now instead of waiting on its timer.</summary>
    internal void StepAsk()
    {
        var step = _askStep;
        _askTimer?.Stop();
        _askTimer = null;
        _askStep = null;
        step?.Invoke();
    }

    private void EndAskTimer()
    {
        _askTimer?.Stop();
        _askTimer = null;
        _askStep = null;
    }
}
