// WPF MainWindow.Presets.cs:1163-1283; called by RefreshDashboardRail (:107).
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal void RefreshMysteryTile()
    {
        if (SettingsPage is not { } dash) return;
        var key = SettingsTabView.TodayFreeKey();
        var name = key switch
        {
            "takeover" => Loc.Get("tab_takeover"),
            "awareness" => Loc.Get("tab_awareness"),
            "haptics" => Loc.Get("tab_haptics"),
            "voice" => Loc.Get("tab_shelistening"),
            "fyp" => Loc.Get("tab_fyp"),
            "remote" => Loc.Get("tab_remote_control"),
            "dtrh" => "Down the Rabbit Hole",
            _ => Loc.Get("mosaic_daily_gift"),
        };
        // WPF :1177-1189: the unopened face says the offer, only the reveal names the feature.
        dash.CardMystery.Title = Loc.Get("mosaic_free_today");
        dash.CardMystery.TierBadge = null;
        dash.FindControl<TextBlock>("MysteryRevealName")!.Text = name;
        var path = key switch
        {
            "takeover" => "features/takeover.png",
            "awareness" => "features/awareness.png",
            "haptics" => "features/vibe.png",
            "fyp" => "features/fyp.png",
            "remote" => "features/remote_control.png",
            "dtrh" => "features/dtrh.png",
            _ => "features/mysterybox.png",
        };
        // WPF :1192-1196: mod-aware art, decoded at 512, with the box as fallback.
        var face = dash.FindControl<Border>("MysteryRevealFace")!;
        var artHost = ((Grid)face.Child!).Children.OfType<Border>().First();
        if (artHost.Background is ImageBrush brush)
            brush.Source = ModArt.FirstOf(new[] { path }.Concat(Services.UI.ModTileArt.Candidates("mysterybox", CoreMods.ActiveModId)), 512);
        // WPF :1207-1211: premium owns the pool, so its reveal carries no price sign.
        var tier = CoreEntitlement.HasPremium ? 0 : key switch
        {
            "dtrh" => 2,
            "takeover" or "awareness" or "haptics" or "voice" or "fyp" or "remote" => 1,
            _ => 0,
        };
        var badge = dash.FindControl<TierBadge>("MysteryRevealBadge")!;
        badge.Tier = tier;
        badge.FreeToday = tier > 0;
        EnsureMysteryFx();
    }
    private Helpers.BeatLoop? _mysteryTurn, _mysteryBreath;
    private bool _mysteryHooked, _mysteryWantReveal, _mysteryShowingReveal;
    internal Func<DateTime> MysteryToday = () => DateTime.Now;
    internal bool MysteryBreathing => _mysteryBreath != null;
    private string MysteryDate => MysteryToday().ToString("yyyy-MM-dd");
    private Grid MysteryPlate => SettingsPage!.FindControl<Grid>("MysteryPlate")!;
    private ScaleTransform MysteryScale => (ScaleTransform)((TransformGroup)MysteryPlate.RenderTransform!).Children[0];
    private SkewTransform MysterySkew => (SkewTransform)((TransformGroup)MysteryPlate.RenderTransform!).Children[1];

    // WPF DashboardFx.cs:384-452. The untransformed host keeps hover stable at the thin point.
    private void EnsureMysteryFx()
    {
        if (SettingsPage is not { } tab) return;
        if (!_mysteryHooked)
        {
            _mysteryHooked = true;
            var host = tab.FindControl<Grid>("MysteryFlipHost")!;
            host.PointerEntered += (_, _) => RequestMysteryFace(true);
            host.PointerExited += (_, _) => RequestMysteryFace(false);
            tab.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) ApplyMysteryFx(); };
            Closed += (_, _) => { SettleMysteryPlate(false); StopMysteryBreath(); };
        }
        ApplyMysteryFx();
    }

    // WPF DashboardFx.cs:325-377, 459-494. Only user hover spends the local-date latch.
    internal void RequestMysteryFace(bool reveal)
    {
        _mysteryWantReveal = reveal;
        if (reveal && CoreSettings.Current.DailyGiftLastRevealDate != MysteryDate)
        {
            CoreSettings.Current.DailyGiftLastRevealDate = MysteryDate;
            CoreSettings.Save();
        }
        ApplyMysteryBreath();
        _mysteryTurn?.Stop();
        _mysteryTurn = null;
        if (!AmbientFxCanvas.Env.AllowTransitions) { SettleMysteryPlate(reveal); return; }
        if (_mysteryShowingReveal == reveal && MysteryScale.ScaleX >= .999)
        { SettleMysteryPlate(reveal); return; }
        var from = MysteryScale.ScaleX;
        var lean = MysterySkew.AngleY;
        var close = _mysteryShowingReveal != reveal;
        var closeSeconds = close ? .15 * from : 0;
        var openFrom = close ? 0 : from;
        var openSeconds = .15 * (1 - openFrom);
        // WPF DashboardFx.cs:501-601: reverse from the current value, swap only at zero.
        _mysteryTurn = new Helpers.BeatLoop(MysteryPlate, t =>
        {
            if (t < closeSeconds)
            {
                var ease = Math.Pow(t / closeSeconds, 3);
                MysteryScale.ScaleX = from * (1 - ease);
                MysterySkew.AngleY = lean + (6 - lean) * ease;
            }
            else
            {
                SetMysteryFace(reveal);
                var progress = openSeconds == 0 ? 1 : Math.Clamp((t - closeSeconds) / openSeconds, 0, 1);
                var remaining = Math.Pow(1 - progress, 3);
                MysteryScale.ScaleX = 1 - (1 - openFrom) * remaining;
                MysterySkew.AngleY = (close ? -6 : lean) * remaining;
                if (progress >= 1) SettleMysteryPlate(reveal);
            }
        });
        _mysteryTurn.Start();
    }

    private void SetMysteryFace(bool reveal)
    {
        _mysteryShowingReveal = reveal;
        SettingsPage!.CardMystery.IsVisible = !reveal;
        SettingsPage.FindControl<Border>("MysteryRevealFace")!.IsVisible = reveal;
    }

    // WPF DashboardFx.cs:599-610, 731: hiding never acknowledges the gift.
    private void SettleMysteryPlate(bool reveal)
    {
        _mysteryTurn?.Stop();
        _mysteryTurn = null;
        _mysteryWantReveal = reveal;
        MysteryScale.ScaleX = 1;
        MysterySkew.AngleY = 0;
        SetMysteryFace(reveal);
    }

    private void ApplyMysteryFx()
    {
        if (!_mysteryHooked) return;
        if (SettingsPage?.IsVisible != true || WindowState == WindowState.Minimized)
            SettleMysteryPlate(false);
        else if (!AmbientFxCanvas.Env.AllowTransitions) SettleMysteryPlate(_mysteryWantReveal);
        ApplyMysteryBreath();
    }

    // WPF DashboardFx.cs:341-375, 668-730. Badge content is independent of ambient motion.
    private void ApplyMysteryBreath()
    {
        var tab = SettingsPage!;
        var unopened = CoreSettings.Current.DailyGiftLastRevealDate != MysteryDate;
        tab.FindControl<Border>("MysteryBadge")!.IsVisible = unopened;
        var run = unopened && IsVisible && tab.IsVisible && IsActive && WindowState != WindowState.Minimized
            && AmbientFxCanvas.Env.AllowAmbientLoops;
        if (!run) { StopMysteryBreath(); return; }
        if (_mysteryBreath != null) return;
        var glow = tab.FindControl<Border>("MysteryGlow")!;
        var scale = (ScaleTransform)((TransformGroup)tab.FindControl<Border>("MysteryBadge")!.RenderTransform!).Children[0];
        _mysteryBreath = new Helpers.BeatLoop(glow, t =>
        {
            var breath = Helpers.BeatLoop.Breath(t, .9);
            glow.Opacity = .55 + .4 * breath;
            scale.ScaleX = scale.ScaleY = 1 + .12 * breath;
        });
        _mysteryBreath.Start();
    }

    private void StopMysteryBreath()
    {
        _mysteryBreath?.Stop();
        _mysteryBreath = null;
        SettingsPage!.FindControl<Border>("MysteryGlow")!.Opacity = .55;
        var scale = (ScaleTransform)((TransformGroup)SettingsPage.FindControl<Border>("MysteryBadge")!.RenderTransform!).Children[0];
        scale.ScaleX = scale.ScaleY = 1;
    }
}
