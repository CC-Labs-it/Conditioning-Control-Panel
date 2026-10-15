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

    // ponytail: wordless pat faces are live here; the full chain player, particle bursts,
    // body art/moves and its speech priority/summon-cut are wire-win-emi-desk-b.
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
        if (face is null) StopPatFeedback();
        else if (face != _patFaceDrawn) { _patFaceDrawn = face; DrawFace(face); }   // P07
    }

    private void StopPatFeedback()
    {
        if (_patFeedbackTimer is not null)
        {
            _patFeedbackTimer.Stop();
            _patFeedbackTimer.Tick -= OnPatFeedbackTick;
            _patFeedbackTimer = null;
        }
        if (_patFeedback is not null) DrawFace(RestFace);
        _patFeedback = null;
    }
}
