using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.FirstShow;
using Serilog;
using S = ConditioningControlPanel.Services.FirstShow.FirstShowScript;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowDesktopWindow.Guide.cs: after the verdict,
/// "Show me around" walks the real app through Core <see cref="S.WelcomeSteps"/>, ringing each named control with
/// <see cref="FirstShowHighlight"/> while EMI stands beside it (<see cref="FirstShowLayout.GuideBody"/>). Head
/// difference: WPF stretched the stage over the virtual desktop; an Avalonia window across monitors of different
/// scale does not lay out, so the stage moves to the monitor the app window is on.</summary>
internal sealed partial class FirstShowStageWindow
{
    private readonly FirstShowHighlight _highlight = new();
    private bool _guiding, _waitForHover;
    private int _guideIndex = -1;
    private double _hoverTime, _targetWait, _highlightWait;
    private Control? _guideTarget;
    private FeatureCard? _previewCard;
    private string? _targetName;
    internal int GuideIndex => _guideIndex;
    internal Control? GuideTarget => _guideTarget;
    internal FeatureCard? PreviewCard => _previewCard;
    internal Rect? HighlightTarget => _highlight.Target;
    private RectD TargetRect => _highlight.Target is { } t ? new RectD(t.X, t.Y, t.Width, t.Height) : FirstShowLayout.Empty;

    // WPF Guide.cs :25-37.
    private void AskVerdict() { Ask(Text("verdict")); AddButton(Text("liked"), () => Reply("replyLiked")); AddButton(Text("showoff"), () => Reply("replyShowoff")); }
    private void Reply(string key) { Ask(Text(key)); AddButton(Text("showAround"), BeginWelcome); AddButton(Text("explore"), Close); }

    // WPF :39-56.
    private void BeginWelcome()
    {
        if (_main == null) { Close(); return; }
        _guiding = true; _ending = false; _logo.Opacity = 0;
        if (_effects != null) { _stage.Children.Remove(_effects); _effects.Dispose(); _effects = null; }
        try
        {
            if (Screens?.ScreenFromWindow(_main) is { } screen && screen.Bounds != _screenPx)
            {
                _screenPx = screen.Bounds; _scale = screen.Scaling > 0 ? screen.Scaling : 1;
                Position = _screenPx.Position; Width = _screenPx.Width / _scale; Height = _screenPx.Height / _scale;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "First show guide: could not follow the app window"); }
        if (!_stage.Children.Contains(_highlight)) _stage.Children.Insert(0, _highlight);
        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        NextGuideStep();
    }

    private void NextGuideStep()
    {
        ClearGuideTarget();
        if (_closed || _main == null) return;
        if (++_guideIndex >= S.WelcomeSteps.Length) { FinishWelcome(); return; }
        var step = S.WelcomeSteps[_guideIndex];
        _main.ShowTab(step.Tab);
        _targetName = step.Target; _waitForHover = step.Hover; _targetWait = 0; _highlightWait = 0;
        Ask(Text(_bundled && step.Line == "guideMedia" ? "guideMediaBundled" : step.Line));
        if (!step.Hover) AddButton(Text("next"), NextGuideStep);
        AddButton(Text("finishTour"), FinishWelcome);
    }

    private void FinishWelcome()
    {
        ClearGuideTarget(); _guideIndex = S.WelcomeSteps.Length;
        _main?.ShowTab("settings");
        Ask(Text("whereNext"));
        AddButton(Text("sessions"), () => LeaveFor("presets"));
        AddButton(Text("studio"), () => LeaveFor("studio"));
        AddButton(Text("explore"), Close);
    }

    private void LeaveFor(string tab)
    {
        var main = _main;
        Close();
        main?.ShowTab(tab);
        main?.Activate();
    }

    private void ClearGuideTarget()
    {
        _previewCard?.SetTutorialPreview(false); _previewCard = null;
        if (_guideTarget != null) global::ConditioningControlPanel.Avalonia.Controls.HelpPopover.CloseActive();
        _guideTarget = null; _targetName = null; _waitForHover = false; _hoverTime = 0;
        _highlight.Target = null; _highlight.InvalidateVisual();
    }

    // WPF GuideFrame :103-161.
    private void GuideFrame(double dt)
    {
        if (_main == null || !_main.IsVisible || _main.WindowState == WindowState.Minimized) { _highlight.IsVisible = false; return; }
        if (_guideTarget == null && _targetName != null)
        {
            var all = _main.GetVisualDescendants().OfType<Control>().Where(x => x.IsEffectivelyVisible).ToArray();
            if (_targetName == "feature-help")
            {
                _previewCard = all.OfType<FeatureCard>().FirstOrDefault(x => !x.IsLocked && !string.IsNullOrEmpty(x.HelpSectionId)
                    && x.FindControl<Button>("BtnHelp") is { IsEffectivelyVisible: true });
                _guideTarget = _previewCard?.FindControl<Button>("BtnHelp");
                _previewCard?.SetTutorialPreview(true);
            }
            else _guideTarget = all.FirstOrDefault(x => x.Name == _targetName);
            _targetWait += dt;
            // A customized dashboard may have no feature cards. Never strand that player.
            if (_guideTarget == null && _targetWait > S.TargetGiveUpSeconds)
            {
                _targetName = null; _waitForHover = false;
                if (_guideIndex == 0) AddButton(Text("next"), NextGuideStep);
            }
        }
        var safe = SafeBounds();
        _highlight.IsVisible = true; _highlight.Target = null;
        _highlight.Width = Width; _highlight.Height = Height;
        _highlight.Viewport = new Rect(safe.Left, safe.Top, safe.Width, safe.Height);
        if (_guideTarget is { IsEffectivelyVisible: true } target && target.Bounds.Width > 0)
        {
            var a = target.PointToScreen(new Point(0, 0));
            var b = target.PointToScreen(new Point(target.Bounds.Width, target.Bounds.Height));
            var rect = new Rect(new Point((a.X - Position.X) / _scale, (a.Y - Position.Y) / _scale),
                new Point((b.X - Position.X) / _scale, (b.Y - Position.Y) / _scale)).Intersect(_highlight.Viewport);
            if (rect.Width > 0 && rect.Height > 0) _highlight.Target = rect;
            _highlightWait += dt;
            _highlight.Waiting = _highlightWait; _highlight.Time = Clock();
            if (_waitForHover)
            {
                _hoverTime = target.IsPointerOver ? _hoverTime + dt : 0;
                if (_hoverTime >= S.HoverSeconds) FoundHelp();
            }
        }
        _highlight.InvalidateVisual();
        var placement = FirstShowLayout.GuideBody(safe, BodySize(), SpeechSize(), TargetRect);
        double ease = (CoreSettings.Current?.MotionLevel ?? MotionLevel.Full) == MotionLevel.Off ? 1 : 1 - Math.Exp(-dt * 7);
        _x += (placement.Left + placement.Width / 2 - _x) * ease; _y += (placement.Top + placement.Height / 2 - _y) * ease;
    }

    /// <summary>WPF :147-153: the pointer rested on the help button long enough.</summary>
    internal void FoundHelp()
    {
        _waitForHover = false;
        _audio.Cue("pop"); Ask(Text("foundHelp"));
        AddButton(Text("next"), NextGuideStep); AddButton(Text("finishTour"), FinishWelcome);
    }
}
