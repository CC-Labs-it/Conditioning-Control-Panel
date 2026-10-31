using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private readonly EmiPokeLadder _patPokes = new();
    internal TimeProvider PatClock { get; set; } = TimeProvider.System;
    private DispatcherTimer? _patFeedbackTimer;
    private IReadOnlyList<EmiPatFrame>? _patFeedback;
    private DateTimeOffset _patFeedbackFrom;
    private string? _patFaceDrawn;

    private Action? _wakeFeedbackDone;

    // WPF EmiChainPlayer.cs:77-133: hold every frame, including the last, before Done.
    // Reuse the existing wordless face clock and the shared canon instead of another player.
    private void StartWakeFeedback(Action? done)
    {
        var seq = EmiChains.Chains["wake"].Seq;
        var frames = new EmiPatFrame[seq.Count];
        for (int i = 0; i < seq.Count; i++) frames[i] = new(seq[i].Text, seq[i].Ms);
        StartPatFeedback(frames);
        _wakeFeedbackDone = done;
    }

    // Part B: full chain player, particle bursts, body art/moves and speech priority.
    // The wordless wake and pat faces share this clock; only wake may be cut for a pat.
    private void StartPatFeedback(IReadOnlyList<EmiPatFrame> frames)
    {
        StopPatFeedback();
        _patFeedback = frames;
        _patFaceDrawn = null;
        _patFeedbackFrom = PatClock.GetUtcNow();
        AdvancePatFeedback();
        _patFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _patFeedbackTimer.Tick += OnPatFeedbackTick;
        _patFeedbackTimer.Start();
    }

    private void OnPatFeedbackTick(object? sender, EventArgs e) => AdvancePatFeedback();

    internal void AdvancePatFeedback()
    {
        if (_patFeedback is null) return;
        var face = EmiPatFeedback.FaceAt(_patFeedback, (PatClock.GetUtcNow() - _patFeedbackFrom).TotalMilliseconds);
        if (face is null)
        {
            var done = _wakeFeedbackDone;
            bool wake = _summonChainLive;
            StopPatFeedback(restoreFace: !wake);
            done?.Invoke();
        }
        else if (face != _patFaceDrawn) { _patFaceDrawn = face; DrawFace(face); }   // P07
    }

    private void StopPatFeedback(bool restoreFace = true)
    {
        if (_patFeedbackTimer is not null)
        {
            _patFeedbackTimer.Stop();
            _patFeedbackTimer.Tick -= OnPatFeedbackTick;
            _patFeedbackTimer = null;
        }
        _wakeFeedbackDone = null; // cancellation never completes the entrance
        if (restoreFace && _patFeedback is not null) DrawFace(EmiChains.RestFace);
        _patFeedback = null;
    }
}
