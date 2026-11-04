// WPF MainWindow.Presets.cs:1182: repaint the daily gift on the dashboard funnel.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal void RefreshMysteryTile()
        => RefreshMysteryTile(App.DailyFree?.TodayKey, CoreEntitlement.HasPremium);

    internal void RefreshMysteryTile(string? key, bool premium)
    {
        if (SettingsPage is not { } dash) return;
        dash.CardMystery.Title = Loc.Get("mosaic_free_today");
        dash.CardMystery.TierBadge = null;
        dash.MysteryRevealName.Text = MysteryName(key) ?? Loc.Get("mosaic_daily_gift");
        var badge = dash.MysteryRevealBadge;
        badge.Tier = !premium && MysteryName(key) != null ? 1 : 0;
        badge.FreeToday = badge.Tier > 0;
        EnsureMysteryFlip();
    }

    // WPF DashboardFx.cs:384-666. The host keeps an untransformed hit area so
    // the edge-on plate cannot manufacture pointer leaves during a turn.
    private bool _mysteryFlipHooked, _mysteryShowing, _mysteryWanted;
    private BeatLoop? _mysteryFlip;

    private void EnsureMysteryFlip()
    {
        if (SettingsPage is not { } dash || dash.FindControl<Grid>("MysteryFlipHost") is not { } host) return;
        dash.MysteryBadge.IsVisible = CoreSettings.Service != null &&
            CoreSettings.Current.DailyGiftLastRevealDate != DateTime.Now.ToString("yyyy-MM-dd");
        if (_mysteryFlipHooked) return;
        _mysteryFlipHooked = true;
        host.PointerEntered += (_, _) => RequestMysteryFace(true);
        host.PointerExited += (_, _) => RequestMysteryFace(false);
        dash.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && !dash.IsVisible) SettleMysteryFace(false);
        };
        Closed += (_, _) => SettleMysteryFace(false);
    }

    private void RequestMysteryFace(bool reveal)
    {
        if (SettingsPage is not { } dash || dash.FindControl<Grid>("MysteryFlipPlate") is not { } plate ||
            plate.RenderTransform is not TransformGroup { Children: [ScaleTransform scale, SkewTransform skew] }) return;
        _mysteryWanted = reveal;
        if (reveal && CoreSettings.Service is { } settings)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (settings.Current.DailyGiftLastRevealDate != today)
            {
                settings.Current.DailyGiftLastRevealDate = today;
                settings.Save();
            }
            dash.MysteryBadge.IsVisible = false;
        }
        _mysteryFlip?.Stop();
        if (!AmbientFxCanvas.Env.AllowTransitions) { SettleMysteryFace(reveal); return; }
        var startScale = Math.Clamp(scale.ScaleX, 0, 1);
        var startSkew = skew.AngleY;
        var closing = _mysteryShowing != reveal;
        var closeTime = closing ? 0.15 * startScale : 0;
        var openTime = 0.15 * (closing ? 1 : 1 - startScale);
        if (closeTime + openTime < 0.001) { SettleMysteryFace(reveal); return; }
        _mysteryFlip = new BeatLoop(plate, seconds =>
        {
            if (closing && seconds < closeTime)
            {
                var t = seconds / closeTime;
                t = t * t * t;
                scale.ScaleX = startScale * (1 - t);
                skew.AngleY = startSkew + (6 - startSkew) * t;
                return;
            }
            if (_mysteryShowing != _mysteryWanted) SetMysteryFace(_mysteryWanted);
            var p = Math.Clamp((seconds - closeTime) / openTime, 0, 1);
            p = 1 - Math.Pow(1 - p, 3);
            var from = closing ? 0 : startScale;
            scale.ScaleX = from + (1 - from) * p;
            skew.AngleY = (closing ? -6 : startSkew) * (1 - p);
            if (p >= 1) SettleMysteryFace(_mysteryWanted);
        });
        _mysteryFlip.OnParked = () => SettleMysteryFace(false);
        _mysteryFlip.Start();
    }


    private void SetMysteryFace(bool reveal)
    {
        if (SettingsPage is not { } dash) return;
        dash.MysteryRevealFace.IsVisible = reveal;
        dash.CardMystery.IsVisible = !reveal;
        _mysteryShowing = reveal;
    }

    private void SettleMysteryFace(bool reveal)
    {
        _mysteryFlip?.Stop();
        _mysteryWanted = reveal;
        if (SettingsPage?.FindControl<Grid>("MysteryFlipPlate")?.RenderTransform is
            TransformGroup { Children: [ScaleTransform scale, SkewTransform skew] })
        {
            scale.ScaleX = 1;
            skew.AngleY = 0;
        }
        SetMysteryFace(reveal);
    }

    // The service signals both day rollovers and server overrides off the UI thread.
    internal void InitializeDailyFreeRefresh(DailyFreeService dailyFree)
    {
        void Changed() => Dispatcher.UIThread.Post(() =>
        {
            RefreshEntitlementVeils(persist: true);
            RefreshExclusivesTab();
            RefreshNavPremiumTags();
            RefreshProfileBubble();
        });
        dailyFree.TodayChanged += Changed;
        Closed += (_, _) => dailyFree.TodayChanged -= Changed;
    }

    internal static string? MysteryName(string? key) => key switch
    {
        "takeover" => Loc.Get("tab_takeover"),
        "awareness" => Loc.Get("tab_awareness"),
        "haptics" => Loc.Get("tab_haptics"),
        "voice" => Loc.Get("tab_shelistening"),
        "fyp" => Loc.Get("tab_fyp"),
        "remote" => Loc.Get("tab_remote_control"),
        "dtrh" => "Down the Rabbit Hole",
        _ => null,
    };
}
