using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private DispatcherTimer? _speechTimer;
    private int _speechStage;
    private string? _speechLine;
    private string _speechFace = "^_^";
    private Action? _speechDone;

    private void BeginSpeech(string? line, string face, Action? done)
    {
        CancelSpeech();
        _speechLine = line;
        _speechFace = string.IsNullOrEmpty(face) ? "^_^" : face;
        _speechDone = done;
        _speechStage = 0;
        DrawFace("0_0");
        OnBubbleTextCore(".");
        _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(EmiSpeechCadence.Dot1Ms) };
        _speechTimer.Tick += (_, _) => AdvanceSpeech();
        _speechTimer.Start();
    }

    // The timer and the headless tests drive the same frames; no wall-clock waits in the tests.
    internal void AdvanceSpeech()
    {
        if (_speechTimer == null) return;
        switch (++_speechStage)
        {
            case 1:
                OnBubbleTextCore("..");
                _speechTimer.Interval = TimeSpan.FromMilliseconds(EmiSpeechCadence.Dot2Ms);
                break;
            case 2:
                OnBubbleTextCore("...");
                _speechTimer.Interval = TimeSpan.FromMilliseconds(EmiSpeechCadence.Dot3Ms);
                break;
            case 3:
                DrawFace(_speechFace);
                OnBubbleTextCore(_speechLine);
                _speechTimer.Interval = TimeSpan.FromMilliseconds(EmiSpeechCadence.HoldMs(_speechLine));
                break;
            case 4:
                DrawFace("0_0");
                OnBubbleTextCore(null);
                _speechTimer.Interval = TimeSpan.FromMilliseconds(EmiSpeechCadence.RestMs);
                break;
            default:
                var done = _speechDone;
                CancelSpeech();
                done?.Invoke();
                break;
        }
    }

    private void CancelSpeech()
    {
        _speechTimer?.Stop();
        _speechTimer = null;
        _speechLine = null;
        _speechDone = null;
        OnBubbleTextCore(null);
    }

    internal void SpeakLine(LineDraw line, EmiLineEngine engine)
    {
        if (PresentationActive || !IsVisible) return;
        CancelChain();
        CancelHold();
        engine.Ack(line.Id, spoke: !line.Hold);
        if (line.Hold || string.IsNullOrWhiteSpace(line.Text))
        {
            HoldFace(line);
            return;
        }
        Say(line.Text, line.Face);
    }

    // WPF EmiDeskWindow.Bubble.cs:545-565 HoldFace: a face, held, no bubble, then back to rest.
    private const int IgnoreFaceMs = 1400;
    private DispatcherTimer? _holdTimer;

    private void HoldFace(LineDraw line)
    {
        CancelHold();
        StopIdleBeats();
        DrawFace(string.IsNullOrEmpty(line.Face) ? "-_-" : line.Face);
        _holdTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(line.HoldMs > 0 ? line.HoldMs : IgnoreFaceMs)
        };
        _holdTimer.Tick += (_, _) => ExpireHold();
        _holdTimer.Start();
    }

    // The timer and the headless tests drive the same step.
    internal void ExpireHold()
    {
        if (_holdTimer == null) return;
        CancelHold();
        DrawFace(RestFace);
        RestartIdleBeats();
    }

    private void CancelHold()
    {
        _holdTimer?.Stop();
        _holdTimer = null;
    }
}
