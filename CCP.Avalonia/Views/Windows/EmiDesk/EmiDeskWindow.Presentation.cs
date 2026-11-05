using System;
using Avalonia;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// PORTED from ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.Presentation.cs: the welcome show
// borrows the REAL widget, drives her position, bob, turn, pose and face frame by frame, and hands her
// back where she was. WPF re-owned her to the stage (:17); an Avalonia window cannot be re-owned once
// shown, so she is raised above the stage instead (both topmost, last raised wins).
public partial class EmiDeskWindow
{
    internal bool PresentationActive { get; private set; }
    internal bool PresentationArriving { get; private set; }
    private Action? _stopPresentation;
    private PixelPoint _beforePresentation;
    private string? _presentationFace, _presentationPose;
    private int _presentationEpoch;
    internal void BeginPresentation(Action stop)
    {
        _beforePresentation = Position;
        PresentationActive = true; _presentationEpoch++; _presentationFace = _presentationPose = null;
        _stopPresentation = stop;
        try
        {
            FinishSummon(); CancelChain(); CancelHold(); CloseRing(); CloseOptionsPanel(); CloseBook();
            StopIdleBeats(); DisarmPet(); _dragging = false; SweepFx(all: true); TearDownReactions();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] presentation setup step failed"); }
        _bodyRoot.IsVisible = true; SetCrt(1, 1); InputLocked = false; _transiting = false;
        _btnGear.IsVisible = _btnHelp.IsVisible = false; Show(); Opacity = 1;
        try { Topmost = false; Topmost = true; } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] raise above the stage failed"); }
        SetPose("idle"); DrawFace("^_^");
    }
    /// <summary>WPF :37: her summon, as the show's entrance. Motion Off skips it.</summary>
    internal void RunPresentationEntrance()
    {
        if (!PresentationActive) return;
        if (CoreSettings.Current?.MotionLevel == ConditioningControlPanel.Models.MotionLevel.Off) return;
        PresentationArriving = true;
        int epoch = _presentationEpoch;
        RunSummon(() => { if (epoch == _presentationEpoch) { PresentationArriving = false; _presentationFace = _presentationPose = null; } });
    }
    /// <summary>WPF :45: her centre in PHYSICAL screen pixels, plus this frame's bob (DIPs), turn
    /// (degrees), pose and face.</summary>
    internal void PresentAt(Point centerPixels, double bob, double turn, string pose, string face)
    {
        if (!PresentationActive) return;
        double scale = Math.Max(.1, DipScale), bw = _bodyWidth * scale, bh = _bodyWidth * BodyAspect * scale;
        SetBodyPhysical(centerPixels.X - bw / 2, centerPixels.Y - bh / 2);
        if (PresentationArriving) return;
        _moveShift.Y = bob; _wobbleRotate.Angle = turn;
        if (_presentationPose != pose) { _presentationPose = pose; SetPose(pose); }
        if (_presentationFace != face) { _presentationFace = face; DrawFace(face); }
    }
    internal void StopPresentation() => _stopPresentation?.Invoke();
    internal void EndPresentation()
    {
        if (!PresentationActive) return;
        PresentationActive = PresentationArriving = false; _presentationEpoch++; _stopPresentation = null;
        try { FinishSummon(); CancelChain(); SweepFx(all: true); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] presentation teardown step failed"); }
        _bodyRoot.IsVisible = true; SetCrt(1, 1); InputLocked = false; _transiting = false;
        Position = _beforePresentation;
        _moveShift.Y = 0; _wobbleRotate.Angle = 0; _btnGear.IsVisible = _btnHelp.IsVisible = true;
        SetPose("idle"); DrawFace("^_^"); ClampIntoWorkArea(); RestartIdleBeats();
    }
}
