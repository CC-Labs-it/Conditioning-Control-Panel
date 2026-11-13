using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
public partial class EmiDeskWindow
{
    // Say plays WPF's MakeSay chain through the shared chain player (WPF EmiDeskWindow.xaml.cs:1080-1087);
    // this test seam elapses its current frame hold, as the frame clock would.
    internal void AdvanceSpeech() => _player.ElapsePendingHold();
    internal void SpeakLine(LineDraw line, EmiLineEngine engine)
    {
        if (PresentationActive || !IsVisible) return;
        CancelChain();
        CancelHold();
        engine.Ack(line.Id, spoke: !line.Hold);
        if (line.Hold) { HoldFace(line); return; }
        EmiDeskService.Instance.NoteEmiSpoke();
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
        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(line.HoldMs > 0 ? line.HoldMs : IgnoreFaceMs) };
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
