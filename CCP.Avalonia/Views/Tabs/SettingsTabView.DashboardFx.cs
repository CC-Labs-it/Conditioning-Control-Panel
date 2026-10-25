// Adapted from WPF MainWindow.DashboardFx.cs: mystery doorbell/flip and still-logo drift/sheen.
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView
{
    private BeatLoop? _mysteryBreath, _mysteryFlip, _logoDecoration;
    private bool _decorationAmbient, _decorationTransitions, _mysteryShown, _mysteryWanted;
    private string? _mysteryArtKey;
    private string TodayStamp => DateTime.Now.ToString("yyyy-MM-dd");
    private ScaleTransform BadgeScale => (ScaleTransform)((TransformGroup)MysteryBadge.RenderTransform!).Children[0];

    private void InitializeDashboardDecoration()
    {
        // Keep the input host untransformed: faces turn inside it, so an edge-on plate cannot
        // manufacture PointerExited under a stationary pointer (the WPF phantom-leave case).
        var plate = new Grid { RenderTransform = MysteryFlipHost.RenderTransform,
            RenderTransformOrigin = RelativePoint.Center };
        // ponytail: a separate transparent input host keeps existing click bubbling and layout.
        // The visual children remain hit-testable; only the transform moves to an inner plate.
        MysteryFlipHost.RenderTransform = null;
        var children = MysteryFlipHost.Children.ToArray();
        MysteryFlipHost.Children.Clear();
        foreach (var child in children) plate.Children.Add(child);
        MysteryFlipHost.Children.Add(plate);
        MysteryFlipHost.Background = Brushes.Transparent;
        _mysteryPlate = plate;
        MysteryFlipHost.PointerEntered += (_, _) => RequestMysteryFace(true);
        MysteryFlipHost.PointerExited += (_, _) => RequestMysteryFace(false);
        DetachedFromVisualTree += (_, _) => StopDashboardDecoration();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
                ApplyDashboardDecoration(_decorationAmbient, _decorationTransitions);
        };
        LogoFaceLogo.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
                ApplyDashboardDecoration(_decorationAmbient, _decorationTransitions);
        };
    }

    private Grid _mysteryPlate = null!;
    private ScaleTransform PlateScale => (ScaleTransform)((TransformGroup)_mysteryPlate.RenderTransform!).Children[0];
    private SkewTransform PlateSkew => (SkewTransform)((TransformGroup)_mysteryPlate.RenderTransform!).Children[1];

    internal void ApplyDashboardDecoration(bool ambient, bool transitions)
    {
        _decorationAmbient = ambient;
        _decorationTransitions = transitions;
        RefreshMysteryFace();
        bool unopened = CoreSettings.Current.DailyGiftLastRevealDate != TodayStamp;
        MysteryBadge.IsVisible = unopened;
        bool visible = IsEffectivelyVisible && TopLevel.GetTopLevel(this) is Window { WindowState: not WindowState.Minimized };
        if (!visible) { _mysteryWanted = false; SettleMysteryFace(false); }
        else if (!transitions) SettleMysteryFace(_mysteryWanted);
        if (!ambient || !visible || !unopened) StopMysteryBreath();
        else if (_mysteryBreath == null)
        {
            _mysteryBreath = new BeatLoop(MysteryBadge, t =>
            {
                double swell = BeatLoop.Breath(t, .9);
                BadgeScale.ScaleX = BadgeScale.ScaleY = 1 + .12 * swell;
                MysteryGlow.Opacity = .55 + .4 * swell;
            });
            _mysteryBreath.Start();
        }
        bool logoWanted = ambient && visible && LogoFaceLogo.IsVisible && !ImgLogo.IsAnimatedArtwork;
        if (!logoWanted) StopLogoDecoration();
        else if (_logoDecoration == null)
        {
            double next = Random.Shared.Next(25, 41), sweepStart = -1;
            _logoDecoration = new BeatLoop(LogoFaceLogo, t =>
            {
                var scale = (ScaleTransform)LogoFaceLogo.RenderTransform!;
                // WPF drift is capped at 10 fps; quantize its 40-second ramp on the shared beat.
                scale.ScaleX = scale.ScaleY = 1 + .04 * BeatLoop.Breath(Math.Floor(t * 10) / 10, 40);
                if (t >= next) { sweepStart = t; next = t + Random.Shared.Next(25, 41); }
                double p = sweepStart < 0 ? 2 : (t - sweepStart) / 1.1;
                var slide = (TranslateTransform)((TransformGroup)LogoSheen.RenderTransform!).Children[1];
                if (p > 1) { LogoSheen.Opacity = 0; return; }
                slide.X = -LogoSheen.Width + (LogoFaceLogo.Bounds.Width + LogoSheen.Width * 1.25) * (1 - Math.Cos(Math.PI * p)) / 2;
                LogoSheen.Opacity = BeatLoop.Keys(p, new[] { (0d, 0d), (.2, .26), (.75, .26), (1d, 0d) });
            });
            _logoDecoration.Start();
        }
    }

    private void RefreshMysteryFace()
    {
        string? key = TodayFreeKey();
        CardMystery.Title = Loc.Get("mosaic_free_today");
        CardMystery.TierBadge = null;
        MysteryRevealName.Text = key switch
        {
            "takeover" => Loc.Get("tab_takeover"), "awareness" => Loc.Get("tab_awareness"),
            "haptics" => Loc.Get("tab_haptics"), "voice" => Loc.Get("tab_shelistening"),
            "fyp" => Loc.Get("tab_fyp"), "remote" => Loc.Get("tab_remote_control"),
            "dtrh" => "Down the Rabbit Hole", _ => Loc.Get("mosaic_daily_gift"),
        };
        int tier = CoreEntitlement.HasPremium ? 0 : key switch
        { "dtrh" => 2, "takeover" or "awareness" or "haptics" or "voice" or "fyp" or "remote" => 1, _ => 0 };
        MysteryRevealBadge.Tier = tier;
        MysteryRevealBadge.FreeToday = tier > 0;
        // Repaint on mod changes as well as day/override changes; no decoding on a beat.
        string artKey = CoreMods.ActiveModId + ":" + key;
        if (_mysteryArtKey == artKey) return;
        _mysteryArtKey = artKey;
        string art = key switch
        {
            "takeover" => "takeover", "awareness" => "awareness", "haptics" => "vibe",
            "fyp" => "fyp", "remote" => "remote_control", "dtrh" => "dtrh", _ => "mysterybox",
        };
        var brush = MysteryRevealFace.GetLogicalDescendants().OfType<Border>()
            .Select(b => b.Background).OfType<ImageBrush>().First();
        brush.Source = ModArt.TryLoad("features/" + art + ".png", 512) ?? ModArt.TryLoad("features/mysterybox.png", 512);
    }

    internal void RequestMysteryFace(bool reveal)
    {
        _mysteryWanted = reveal;
        if (reveal)
        {
            if (CoreSettings.Current.DailyGiftLastRevealDate != TodayStamp)
            {
                CoreSettings.Current.DailyGiftLastRevealDate = TodayStamp;
                CoreSettings.Save();
            }
            StopMysteryBreath();
            MysteryBadge.IsVisible = false;
        }
        _mysteryFlip?.Stop();
        _mysteryFlip = null;
        if (!_decorationTransitions) { SettleMysteryFace(reveal); return; }
        double at = Math.Clamp(PlateScale.ScaleX, 0, 1), skew = PlateSkew.AngleY;
        bool closing = _mysteryShown != reveal;
        double duration = Math.Max(.001, .15 * (closing ? at : 1 - at));
        _mysteryFlip = new BeatLoop(_mysteryPlate, t =>
        {
            double p = Math.Clamp(t / duration, 0, 1);
            double eased = closing ? p * p * p : 1 - Math.Pow(1 - p, 3);
            PlateScale.ScaleX = at + ((closing ? 0 : 1) - at) * eased;
            PlateSkew.AngleY = skew + ((closing ? 6 : 0) - skew) * eased;
            if (p < 1) return;
            _mysteryFlip?.Stop(); _mysteryFlip = null;
            if (closing)
            {
                ShowMysteryFace(reveal);
                PlateSkew.AngleY = -6;
                RequestMysteryFace(reveal);
            }
            else SettleMysteryFace(reveal);
        });
        _mysteryFlip.OnParked = () => { _mysteryWanted = false; SettleMysteryFace(false); };
        _mysteryFlip.Start();
    }

    private void ShowMysteryFace(bool reveal)
    {
        _mysteryShown = reveal;
        CardMystery.IsVisible = !reveal;
        MysteryRevealFace.IsVisible = reveal;
    }

    private void SettleMysteryFace(bool reveal)
    {
        _mysteryFlip?.Stop(); _mysteryFlip = null;
        PlateScale.ScaleX = 1; PlateSkew.AngleY = 0;
        ShowMysteryFace(reveal);
    }

    private void StopMysteryBreath()
    {
        _mysteryBreath?.Stop(); _mysteryBreath = null;
        BadgeScale.ScaleX = BadgeScale.ScaleY = 1;
        MysteryGlow.Opacity = .55;
    }

    private void StopLogoDecoration()
    {
        _logoDecoration?.Stop(); _logoDecoration = null;
        var scale = (ScaleTransform)LogoFaceLogo.RenderTransform!;
        scale.ScaleX = scale.ScaleY = 1;
        LogoSheen.Opacity = 0;
        ((TranslateTransform)((TransformGroup)LogoSheen.RenderTransform!).Children[1]).X = 0;
    }

    internal void StopDashboardDecoration()
    {
        _mysteryWanted = false;
        SettleMysteryFace(false);
        StopMysteryBreath();
        StopLogoDecoration();
    }
}
