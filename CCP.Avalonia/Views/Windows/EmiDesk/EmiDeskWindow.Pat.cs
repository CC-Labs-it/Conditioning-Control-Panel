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

    // Wordless pat frames retain the shared cooldown and poke clock. Full chains take
    // the face through PlayChain; particles and speech still need hosts.
    // #4064 #4146: each pat wears the body pose and plays the body move of the WPF chain it stands
    // in for (EmiChains pet/petStreak/wink, React.cs PetFlickChain, Alive.cs PokeAnnoy/PokeRage),
    // and the pose returns to idle when the frames end or are cut.
    private void StartPatFeedback(IReadOnlyList<EmiPatFrame> frames, string pose, string? move = null)
    {
        StopPatFeedback(restorePose: false);
        SetPose(pose);
        if (move != null) RunBodyMove(move);
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

    private void StopPatFeedback(bool restorePose = true)
    {
        if (_patFeedbackTimer is not null)
        {
            _patFeedbackTimer.Stop();
            _patFeedbackTimer.Tick -= OnPatFeedbackTick;
            _patFeedbackTimer = null;
        }
        if (_patFeedback is not null)
        {
            DrawFace(EmiChains.RestFace);
            if (restorePose) SetPose("idle");
        }
        _patFeedback = null;
    }
}
