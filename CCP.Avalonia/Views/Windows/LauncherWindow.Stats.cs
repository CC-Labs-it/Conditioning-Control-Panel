// WPF LauncherWindow.xaml.cs:626-645 and Services/MotionFx.cs:151-166,198-214.
// Adapted from fin-win-launcher, using the existing frame clock and motion gate.
using System;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;
namespace ConditioningControlPanel.Avalonia.Views.Windows;
public partial class LauncherWindow
{
    private Tween _levelTween, _sparkleTween, _xpTween;
    private double _shownLevel, _shownSparkles, _shownXpWidth = -1;
    private void TweenStats(int level, int sparkles, double width)
    {
        if (level != _shownLevel)
        {
            _levelTween.Go(_shownLevel, level, Env.AllowTransitions ? (_shownLevel == 0 ? 0.9 : 0.5) : 0);
            _shownLevel = level;
        }
        if (sparkles != _shownSparkles)
        {
            _sparkleTween.Go(_shownSparkles, sparkles, Env.AllowTransitions ? (_shownSparkles == 0 ? 1.1 : 0.5) : 0);
            _shownSparkles = sparkles;
        }
        if (width >= 0 && Math.Abs(width - _shownXpWidth) >= 0.5)
        {
            _xpTween.Go(_shownXpWidth < 0 ? 0 : _shownXpWidth, width, Env.AllowTransitions ? (_shownXpWidth < 0 ? 1 : 0.5) : 0);
            _shownXpWidth = width;
        }
        PaintStats(0);
        EnsureFxClock();
    }

    private void PaintStats(double dt)
    {
        StatLevel.Text = _levelTween.Step(dt).ToString("0");
        StatSparkles.Text = _sparkleTween.Step(dt).ToString("N0");
        XpFill.Width = Math.Max(0, _xpTween.Step(dt));
    }

    private bool StepStats(double dt)
    {
        if (!Env.AllowTransitions) SettleStats();
        bool busy = _levelTween.On || _sparkleTween.On || _xpTween.On;
        if (busy) PaintStats(dt);
        return busy;
    }

    private void SettleStats()
    {
        _levelTween.On = _sparkleTween.On = _xpTween.On = false;
        PaintStats(0);
    }
}
