using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView
{
    private BeatLoop? _giftBreath, _giftTurn;
    private bool _giftReveal, _giftWanted;
    private Point? _giftPointer;
    private ScaleTransform GiftScale => (ScaleTransform)((TransformGroup)MysteryFlipHost.RenderTransform!).Children[0];
    private SkewTransform GiftSkew => (SkewTransform)((TransformGroup)MysteryFlipHost.RenderTransform!).Children[1];
    private ScaleTransform GiftBadgeScale => (ScaleTransform)((TransformGroup)MysteryBadge.RenderTransform!).Children[0];
    private bool GiftUnopened => CoreSettings.Current.DailyGiftLastRevealDate != DateTime.Now.ToString("yyyy-MM-dd");

    private void InitializeMysteryGift()
    {
        void RememberPointer(PointerEventArgs e)
        {
            if (MysteryFlipHost.Parent is Control parent) _giftPointer = e.GetPosition(parent);
        }
        // Tracked on the whole view, not the plate: an exit swallowed mid-turn must still be
        // corrected by the move that follows it (WPF re-reads Mouse after each turn).
        AddHandler(PointerMovedEvent, (_, e) => RememberPointer(e), RoutingStrategies.Bubble, handledEventsToo: true);
        PointerExited += (_, e) => RememberPointer(e);
        MysteryFlipHost.PointerEntered += (_, e) => { RememberPointer(e); RequestGiftFace(true); };
        MysteryFlipHost.PointerExited += (_, e) =>
        {
            RememberPointer(e);
            // A transformed plate can leave the hit-test under a stationary pointer mid-turn.
            if (_giftTurn != null && MysteryFlipHost.Parent is Control parent &&
                MysteryFlipHost.Bounds.Contains(e.GetPosition(parent))) return;
            RequestGiftFace(false);
        };
        Loaded += (_, _) => SyncMysteryGiftFx();
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;
            if (!IsVisible) ResetGift(); else RefreshMysteryGift();
        };
        AttachedToVisualTree += (_, _) =>
        {
            LocalizationManager.Instance.LanguageChanged += GiftLanguageChanged;
            CoreMods.ModChanged += GiftModChanged;
            Env.MotionGateChanged += SyncMysteryGiftFx;
            RefreshMysteryGift();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            LocalizationManager.Instance.LanguageChanged -= GiftLanguageChanged;
            CoreMods.ModChanged -= GiftModChanged;
            Env.MotionGateChanged -= SyncMysteryGiftFx;
            ResetGift();
        };
    }

    private void GiftLanguageChanged(object? sender, EventArgs e) => RefreshMysteryGift();
    private void GiftModChanged(object? sender, ModPackage e) => Dispatcher.UIThread.Post(RefreshMysteryGift);

    // WPF MainWindow/MainWindow.Presets.cs:1164-1288; hover/timing: MainWindow/MainWindow.DashboardFx.cs:277-727. Offer on the box; identity and re-stamped price on the back.
    internal void RefreshMysteryGift()
    {
        var key = TodayFreeKey();
        CardMystery.Title = Loc.Get("mosaic_free_today");
        CardMystery.TierBadge = null;
        MysteryRevealName.Text = key switch
        {
            "takeover" => Loc.Get("tab_takeover"), "awareness" => Loc.Get("tab_awareness"),
            "haptics" => Loc.Get("tab_haptics"), "voice" => Loc.Get("tab_shelistening"),
            "fyp" => Loc.Get("tab_fyp"), "remote" => Loc.Get("tab_remote_control"),
            "dtrh" => "Down the Rabbit Hole", _ => Loc.Get("mosaic_daily_gift"),
        };
        var art = key switch
        {
            "takeover" => "takeover", "awareness" => "awareness", "haptics" => "vibe",
            "fyp" => "fyp", "remote" => "remote_control", "dtrh" => "dtrh", _ => "mysterybox",
        };
        var brush = (ImageBrush)MysteryRevealArt.Background!;
        var old = brush.Source as IDisposable;
        brush.Source = ModArt.TryLoad($"features/{art}.png", 512) ?? ModArt.TryLoad("features/mysterybox.png", 512);
        old?.Dispose();
        MysteryRevealBadge.Tier = CoreEntitlement.HasPremium ? 0 : key switch
        {
            "dtrh" => 2, "takeover" or "awareness" or "haptics" or "voice" or "fyp" or "remote" => 1, _ => 0,
        };
        MysteryRevealBadge.FreeToday = MysteryRevealBadge.Tier > 0;
        SyncMysteryGiftFx();
    }

    internal void SyncMysteryGiftFx()
    {
        MysteryBadge.IsVisible = GiftUnopened && IsVisible;
        if (!Env.AllowTransitions && _giftTurn != null) SetGiftFace(_giftWanted);
        if (!GiftUnopened || !IsLoaded || !IsEffectivelyVisible || !Env.AllowAmbientLoops ||
            TopLevel.GetTopLevel(this) is Window { IsActive: false } or Window { WindowState: WindowState.Minimized })
        {
            _giftBreath?.Stop();
            _giftBreath = null;
            MysteryGlow.Opacity = 0.55;
            GiftBadgeScale.ScaleX = GiftBadgeScale.ScaleY = 1;
            return;
        }
        if (_giftBreath != null) return; // WPF leaves a running breath in phase
        _giftBreath = new BeatLoop(MysteryBadge, t =>
        {
            var k = BeatLoop.Breath(t, 0.9);
            MysteryGlow.Opacity = 0.55 + 0.40 * k;
            GiftBadgeScale.ScaleX = GiftBadgeScale.ScaleY = 1 + 0.12 * k;
        });
        _giftBreath.Start();
    }

    private void RequestGiftFace(bool reveal)
    {
        _giftWanted = reveal;
        if (reveal && GiftUnopened)
        {
            CoreSettings.Current.DailyGiftLastRevealDate = DateTime.Now.ToString("yyyy-MM-dd");
            CoreSettings.Save();
            SyncMysteryGiftFx();
        }
        _giftTurn?.Stop();
        _giftTurn = null;
        if (!Env.AllowTransitions || (_giftReveal == reveal && GiftScale.ScaleX >= 0.999))
        { SetGiftFace(reveal); return; }
        // Close to the thin point, swap, then reopen. Reversals use the remaining distance,
        // so a flick costs a flick of motion (WPF 150ms per half, 6-degree fake perspective).
        double start = GiftScale.ScaleX;
        double startSkew = GiftSkew.AngleY;
        bool closing = _giftReveal != reveal;
        double closeSeconds = closing ? start * 0.15 : 0;
        double openFrom = closing ? 0 : start;
        _giftTurn = new BeatLoop(MysteryFlipHost, t =>
        {
            double scale;
            if (closing && t < closeSeconds)
            {
                var k = Math.Pow(t / closeSeconds, 3);
                scale = start * (1 - k);
                GiftSkew.AngleY = startSkew + (6 - startSkew) * k;
            }
            else
            {
                PaintGiftFace(reveal);
                var duration = Math.Max(0.001, (1 - openFrom) * 0.15);
                var k = 1 - Math.Pow(1 - Math.Clamp((t - closeSeconds) / duration, 0, 1), 3);
                scale = openFrom + (1 - openFrom) * k;
                GiftSkew.AngleY = (closing ? -6 : startSkew) * (1 - k);
            }
            GiftScale.ScaleX = scale;
            if (scale >= 1 && (!closing || t >= closeSeconds))
            {
                SetGiftFace(reveal);
                // WPF MainWindow/MainWindow.DashboardFx.cs:635: settle over the inset face, not the layout slot.
                if (_giftPointer is { } point && MysteryFlipHost.Bounds.Deflate(new Thickness(6)).Contains(point) != _giftWanted)
                    RequestGiftFace(!_giftWanted);
            }
        });
        _giftTurn.Start();
    }

    private void PaintGiftFace(bool reveal)
    {
        _giftReveal = reveal;
        CardMystery.IsVisible = !reveal;
        MysteryRevealFace.IsVisible = reveal;
    }

    private void SetGiftFace(bool reveal)
    {
        _giftTurn?.Stop();
        _giftTurn = null;
        PaintGiftFace(reveal);
        GiftScale.ScaleX = 1;
        GiftSkew.AngleY = 0;
    }

    internal void ResetGift()
    {
        _giftBreath?.Stop();
        _giftBreath = null;
        _giftWanted = false;
        _giftPointer = null;
        SetGiftFace(false);
        MysteryGlow.Opacity = 0.55;
        GiftBadgeScale.ScaleX = GiftBadgeScale.ScaleY = 1;
    }
}
