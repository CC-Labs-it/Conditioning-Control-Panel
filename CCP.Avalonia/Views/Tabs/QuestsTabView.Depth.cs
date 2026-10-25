using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

// WPF QuestsTabView.InitDepth/PaintDepthQuests/WirePlank. Rules already live in Core DepthLaw.
public partial class QuestsTabView
{
    private readonly List<Action> _stopDepth = new();

    private void InitDepth()
    {
        PaintDepthQuests(NavStripRules.Accent(NavSections.You));
        WirePlank(BtnRerollWeekly, WeeklyRerollFace, WeeklyRerollDrop, WeeklyRerollBevel);
        WirePlank(BtnFixStreak, FixStreakFace, FixStreakDrop, FixStreakBevel);
        WatchDone(WeeklyCompletedOverlay, () => ApplyDone(WeeklySeat, WeeklyFloatBand, WeeklyCompletedOverlay, WeeklyQuestCard));
        for (int i = 0; i < _dailyCards.Length; i++)
        {
            var card = _dailyCards[i];
            var seat = this.FindControl<Grid>("DailySeat" + i)!;
            var band = this.FindControl<Border>("DailySeatBand" + i)!;
            var overlay = card.FindControl<Border>("CompletedOverlay")!;
            WatchDone(overlay, () => ApplyDone(seat, band, overlay));
        }
        WeeklyProgressFill.SizeChanged += (_, e) => WeeklyTubeBead.IsVisible = e.NewSize.Width >= 10;
        WeeklyTubeBead.IsVisible = WeeklyProgressFill.Bounds.Width >= 10;
        DetachedFromVisualTree += (_, _) => StopDepthTravel();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) StopDepthTravel(); };
    }

    // The subscriptions belong to controls in this view's own tree; they cannot root a detached view.
    private static void WatchDone(Border overlay, Action paint)
    {
        overlay.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) paint(); };
        paint();
    }

    private void ApplyDone(Grid seat, Border band, Border overlay, Border? card = null)
    {
        bool done = overlay.IsVisible;
        band.IsVisible = !done;
        seat.RenderTransform = new TranslateTransform(0, done ? DepthLaw.ActiveSinkPx : 0);
        if (card != null) card.BorderBrush = DepthBrush(done ? "DepthPressedBevel" : "DepthFloatRim");
    }

    internal void PaintDepthQuests(Color hue)
    {
        foreach (var band in new[] { DailySeatBand0, DailySeatBand1, DailySeatBand2, WeeklyFloatBand })
            band.Background = ShadowBand(Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.FloatAlpha)));
        foreach (var drop in new[] { DailyCounterDrop, WeeklyXpDrop, WeeklyRerollDrop, FixStreakDrop })
            drop.Background = ShadowBand(Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32())));
    }

    private static LinearGradientBrush ShadowBand(Color contact) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops { new(contact, 0), new(Color.FromArgb(0, contact.R, contact.G, contact.B), 1) },
    };

    private IBrush? DepthBrush(string key) => this.TryFindResource(key, out var value) ? value as IBrush : null;
    private void StopDepthTravel() { foreach (var stop in _stopDepth) stop(); }

    private void WirePlank(Button button, Grid face, Border drop, Border bevel)
    {
        var travel = new TranslateTransform();
        face.RenderTransform = travel;
        bool wasPressed = false, releasing = false;
        double from = 0, to = 0, duration = 0;
        BeatLoop? beat = null;
        // A finite travel on the existing shared beat: no Transform Animation.RunAsync, new timer or resting loop.
        beat = new BeatLoop(face, elapsed =>
        {
            double p = Math.Clamp(duration > 0 ? elapsed / duration : 1, 0, 1);
            if (releasing && p < 0.6)
            {
                double u = p / 0.6;
                travel.Y = from + (to - DepthLaw.ReleaseOvershootPx - from) * (1 - (1 - u) * (1 - u));
            }
            else if (releasing)
            {
                double u = (p - 0.6) / 0.4;
                double eased = u < 0.5 ? 2 * u * u : 1 - Math.Pow(-2 * u + 2, 2) / 2;
                travel.Y = to - DepthLaw.ReleaseOvershootPx + DepthLaw.ReleaseOvershootPx * eased;
            }
            else travel.Y = from + (to - from) * (1 - (1 - p) * (1 - p));
            if (p >= 1) beat!.Stop();
        });
        void Stop() { beat.Stop(); travel.Y = to; }
        _stopDepth.Add(Stop);
        face.DetachedFromVisualTree += (_, _) => Stop();

        void Update()
        {
            bool enabled = button.IsEffectivelyEnabled, pressed = button.IsPressed, hovered = button.IsPointerOver;
            from = travel.Y;
            to = DepthLaw.TravelFor(enabled, pressed, false, hovered);
            double shadow = DepthLaw.ShadowFor(enabled, pressed, false, hovered);
            drop.Margin = new Thickness(1, shadow, 1, -shadow);
            drop.Opacity = shadow > 0 ? 1 : 0;
            drop.IsVisible = bevel.IsVisible = button.IsVisible;
            bevel.BorderBrush = DepthBrush(pressed ? "DepthPressedBevel" : "DepthRaisedBevel");
            bevel.Background = DepthBrush(pressed ? "DepthPressedShade" : "DepthRaisedSheen");
            bevel.Opacity = enabled ? 1 : 0.5;
            releasing = wasPressed && !pressed;
            duration = DepthLaw.Ms(pressed ? DepthLaw.PressMs : releasing ? DepthLaw.ReleaseMs : DepthLaw.HoverMs,
                CoreSettings.Current.MotionLevel) / 1000.0;
            wasPressed = pressed;
            beat.Stop();
            if (duration <= 0 || !face.IsEffectivelyVisible || TopLevel.GetTopLevel(face) == null || from == to && !releasing) travel.Y = to;
            else beat.Start();
        }
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == Button.IsPressedProperty || e.Property == IsEnabledProperty || e.Property == IsVisibleProperty) Update();
        };
        button.PointerEntered += (_, _) => Update();
        button.PointerExited += (_, _) => Update();
        Update();
    }
}
