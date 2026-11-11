using CoreBannerDrum = ConditioningControlPanel.Services.UI.BannerDrum;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private BeatLoop? _bannerDrumBeat;
    private TextBlock? _drumOutgoing, _drumIncoming;

    private void InitializeBannerDrum()
    {
        AmbientFxCanvas.Env.MotionGateChanged += ResetBannerDrum;
        Closed += (_, _) =>
        {
            AmbientFxCanvas.Env.MotionGateChanged -= ResetBannerDrum;
            ResetBannerDrum();
        };
    }

    private static void ConfigureBannerFade(TextBlock beat)
    {
        if (CoreSettings.Current.MotionLevel == MotionLevel.Off) beat.Transitions = null;
        else EnsureFadeTransition(beat);
    }

    private void ResetBannerDrum()
    {
        _bannerDrumBeat?.Stop();
        _bannerDrumBeat = null;
        foreach (var beat in new[] { _drumOutgoing, _drumIncoming })
            if (beat is not null) beat.RenderTransform = null;
        foreach (var beat in _bannerBeats) ConfigureBannerFade(beat);
        _drumOutgoing = _drumIncoming = null;
    }

    internal void RollBannerDrum(TextBlock outgoing, TextBlock incoming)
    {
        ResetBannerDrum();
        if (AmbientFxCanvas.Env.Level != MotionLevel.Full) return;
        _drumOutgoing = outgoing;
        _drumIncoming = incoming;
        var outScale = new ScaleTransform();
        var inScale = new ScaleTransform();
        var outSlide = new TranslateTransform();
        var inSlide = new TranslateTransform();
        outgoing.RenderTransformOrigin = incoming.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        outgoing.RenderTransform = new TransformGroup { Children = { outScale, outSlide } };
        incoming.RenderTransform = new TransformGroup { Children = { inScale, inSlide } };
        void Step(double t)
        {
            if (t >= CoreBannerDrum.Seconds || AmbientFxCanvas.Env.Level != MotionLevel.Full)
            { ResetBannerDrum(); return; }
            var a = CoreBannerDrum.Face(MotionLevel.Full, t, false);
            var b = CoreBannerDrum.Face(MotionLevel.Full, t, true);
            outSlide.Y = a.Y; outScale.ScaleY = a.Scale;
            inSlide.Y = b.Y; inScale.ScaleY = b.Scale;
        }
        _bannerDrumBeat = new BeatLoop(this, Step);
        _bannerDrumBeat.Start();
        Step(0);
    }

    internal void PaintBannerDepth(Color hue)
    {
        var top = Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.WellTopAlpha));
        var foot = Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.WellLeftAlpha));
        var clear = Color.FromArgb(0, top.R, top.G, top.B);
        if (Named<Border>("BannerDrumShade") is { } shade)
            shade.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new(top, 0), new(clear, 0.34), new(clear, 0.70), new(foot, 1) }
            };
        if (Named<Border>("BannerDrumLip") is { } lip)
            lip.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(10, 0, RelativeUnit.Absolute),
                GradientStops = { new(foot, 0), new(Color.FromArgb(0, foot.R, foot.G, foot.B), 1) }
            };
    }
}
