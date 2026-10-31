using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
public partial class EmiDeskWindow
{
    private DispatcherTimer? _speechTimer;
    private int _speechStage;
    private EmiChain? _speechChain;
    private Action? _speechDone;
    private void BeginSpeech(string? line, string face, Action? done)
    {
        if (PresentationActive) return; // WPF EmiDeskWindow.xaml.cs:1048,1077
        CancelChain();
        _speechChain = EmiChains.MakeSay(line, face, EmiChains.SayHoldMs(line));
        _speechDone = done;
        _speechStage = -1;
        _speechTimer = new DispatcherTimer();
        _speechTimer.Tick += (_, _) => AdvanceSpeech();
        AdvanceSpeech();
        _speechTimer.Start();
    }
    internal void AdvanceSpeech()
    {
        if (_speechTimer == null || _speechChain == null) return;
        if (++_speechStage >= _speechChain.Seq.Count)
        {
            var done = _speechDone;
            CancelSpeech();
            done?.Invoke();
            return;
        }
        var frame = _speechChain.Seq[_speechStage];
        DrawFace(frame.Text);
        OnBubbleTextCore(frame.Bubble);
        _speechTimer.Interval = TimeSpan.FromMilliseconds(frame.Ms);
    }
    private void CancelSpeech()
    {
        _speechTimer?.Stop();
        _speechTimer = null;
        _speechChain = null;
        _speechDone = null;
        OnBubbleTextCore(null);
    }
    internal void SpeakLine(LineDraw line, EmiLineEngine engine)
    {
        if (PresentationActive || !IsVisible) return;
        CancelChain();
        CancelHold();
        engine.Ack(line.Id, spoke: !line.Hold);
        if (line.Hold)
        {
            HoldFace(line);
            return;
        }
        // WPF EmiDeskWindow.Bubble.cs:524-530: a wordless row is a chain, or just a face.
        if (string.IsNullOrWhiteSpace(line.Text))
        {
            if (!string.IsNullOrEmpty(line.Chain)) PlayChain(line.Chain!);
            else DrawFace(line.Face);
            return;
        }
        Say(line.Text, line.Face);
    }
    // WPF EmiDeskWindow.Bubble.cs:545-565 HoldFace: a face, held, no bubble, then back to rest.
    private const int IgnoreFaceMs = 1400;
    private DispatcherTimer? _holdTimer;
    internal bool HoldPending => _holdTimer != null;
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
    internal void ExpireHold()
    {
        if (_holdTimer == null) return;
        CancelHold();
        DrawFace(EmiChains.RestFace);
        RestartIdleBeats();
    }
    private void CancelHold()
    {
        _holdTimer?.Stop();
        _holdTimer = null;
    }
}
