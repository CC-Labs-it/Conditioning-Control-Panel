using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private EmiChain? _intakeChain;
    private DispatcherTimer? _intakeTimer;
    private DispatcherTimer? _intakeHoldTimer;
    private int _intakeFrame;
    private Border? _intakeBubble;
    private TextBlock? _intakeBubbleText;
    internal string? IntakeBubbleText => _intakeBubble?.IsVisible == true ? _intakeBubbleText?.Text : null;

    // WPF EmiDeskWindow.Bubble.cs SpeakLine: spend only once the line is accepted.
    internal void SpeakIntakeLine(LineDraw line)
    {
        if (PresentationActive) return;
        CancelIntakeReaction();
        CancelChain();
        EmiLineEngine.Instance.Ack(line.Id);
        if (string.IsNullOrWhiteSpace(line.Text))
        {
            DrawFace(line.Face);
            return;
        }
        StopIdleBeats();
        _intakeChain = EmiChains.MakeSay(line.Text, line.Face, EmiChains.SayHoldMs(line.Text));
        _intakeFrame = 0;
        AdvanceIntakeChain();
    }

    private void AdvanceIntakeChain()
    {
        if (_intakeChain is not { } chain) return;
        if (_intakeFrame == chain.Seq.Count)
        {
            CancelIntakeChain();
            DrawFace(EmiChains.RestFace);
            SetPose("idle");
            RestartIdleBeats();
            return;
        }
        var frame = chain.Seq[_intakeFrame++];
        if (frame.SetsBubble) SetIntakeBubble(frame.Bubble);
        DrawFace(frame.Text, frame.Small, chain.Flat);
        SetPose(EmiChains.FrameForFace(frame.Text));
        _intakeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, frame.Ms)) };
        _intakeTimer.Tick += IntakeTick;
        _intakeTimer.Start();
    }

    private void IntakeTick(object? sender, EventArgs e)
    {
        if (_intakeTimer != null) { _intakeTimer.Stop(); _intakeTimer.Tick -= IntakeTick; _intakeTimer = null; }
        AdvanceIntakeChain();
    }

    private void SetIntakeBubble(string? text)
    {
        if (_intakeBubble == null)
        {
            _intakeBubbleText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black, FontSize = 13 };
            _intakeBubble = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(8), MaxWidth = 200, Child = _intakeBubbleText };
            _bubbleCanvas.Children.Add(_intakeBubble);
        }
        _intakeBubbleText!.Text = text;
        _intakeBubble.IsVisible = text != null;
        Canvas.SetLeft(_intakeBubble, Math.Max(0, (Width - 200) / 2));
        Canvas.SetTop(_intakeBubble, 10);
    }

    // WPF HoldFace cancels the current channel and chain, then wears a silent face briefly.
    internal void HoldIntakeFace(LineDraw line)
    {
        if (PresentationActive) return;
        CancelIntakeReaction();
        CancelChain();
        StopIdleBeats();
        EmiLineEngine.Instance.Ack(line.Id, spoke: false);
        DrawFace(string.IsNullOrEmpty(line.Face) ? "-_-" : line.Face);
        _intakeHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(line.HoldMs > 0 ? line.HoldMs : 1400) };
        _intakeHoldTimer.Tick += EndIntakeFace;
        _intakeHoldTimer.Start();
    }

    private void EndIntakeFace(object? sender, EventArgs e)
    {
        CancelIntakeReaction();
        DrawFace(EmiChains.RestFace);
        RestartIdleBeats();
    }

    private void CancelIntakeReaction()
    {
        if (_intakeHoldTimer != null) { _intakeHoldTimer.Stop(); _intakeHoldTimer.Tick -= EndIntakeFace; _intakeHoldTimer = null; }
    }

    private void CancelIntakeChain()
    {
        if (_intakeTimer != null) { _intakeTimer.Stop(); _intakeTimer.Tick -= IntakeTick; _intakeTimer = null; }
        _intakeChain = null;
        _intakeFrame = 0;
        if (_intakeBubble != null) _intakeBubble.IsVisible = false;
    }
}
