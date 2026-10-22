// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.HeroFx.cs (789 lines), plus
// the header writer it decorates: UpdateLevelDisplay (MainWindow.UiUpdates.cs:52), the bar half of
// AnimateXpDisplay/FillXpBarTo (MainWindow.ChromeFx.cs:801-855), OnXPChanged/OnLevelUp
// (MainWindow.xaml.cs:712,774) and the level-up flash (CelebrateLevelUp, MainWindow.EventFx.cs:249;
// FlashOverlay, MainWindow.Companion.cs:115).
//
// LIVE: the LVL chip (LevelChip/TxtLevelLabel, the one level readout since eac44ef9f), the "xp / needed XP" readout and the XP bar
// read CoreSettings.PlayerLevel/PlayerXP against Core's XpCurve and repaint on ProgressionBank's
// Awarded and LevelUp. The bar tweens to its new width (0.6s QuadraticEaseOut, MotionFx.BarFill)
// and a level-up flashes XPBarFlashOverlay (250ms up, auto-reversed), both off at MotionLevel Off.
// The profile menu's Level/XP rail is painted from the same numbers.
//
// STILL MISSING vs WPF, so the row stays a stub:
//   the XP odometer (MotionFx.Odometer - the readout snaps), the meniscus (AnimateXpMeniscus,
//   ApplyXpMeniscusPulse), PopLevelChip, the level-up burst (FireBurstAt) and THE BANK hold
//   (MainWindow.BankFx.cs); the rank title (TxtPlayerTitle, a {loc:Str} a code write would lose);
//   the Start button's charge/exhale/ignition/heartbeat set (BtnStart_Click is a stub); level-up
//   sound and toast. FlashSaveAbsorb is live (BtnSave_Click, MainShellWindow.Settings.cs).
// Sign-in, startup profile restore and logout repaint through UpdateQuickLoginUI
// (MainShellWindow.Login.cs), and the login dialog's ProfileLoad is awaited before a repaint.

using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private double _xpFraction;
        private Transitions? _xpFillTransition;

        /// <summary>WPF ctor subscriptions to App.Progression.XPChanged/LevelUp; the events are static,
        /// so they come off again when the window closes.</summary>
        private void HookLevelDisplay()
        {
            Action<double, string> awarded = (_, _) => Dispatcher.UIThread.Post(UpdateLevelDisplay);
            Action<int> levelUp = _ => Dispatcher.UIThread.Post(() => { FlashLevelUp(); UpdateLevelDisplay(); });
            ProgressionBank.Awarded += awarded;
            ProgressionBank.LevelUp += levelUp;
            Closed += (_, _) => { ProgressionBank.Awarded -= awarded; ProgressionBank.LevelUp -= levelUp; };
            if (Named<Border>("XPBar")?.Parent is Control track)
                track.SizeChanged += (_, _) => FillXpBar(animate: false);
            // WPF XPBarTrack_ToolTipOpening (MainWindow.UiUpdates.cs:2897): the ambient-bubble daily
            // budget, computed on open so it is never stale; read-only.
            if (Named<Border>("XPBarTrack") is { } xpTrack)
                xpTrack.AddHandler(ToolTip.ToolTipOpeningEvent, (_, _) => ToolTip.SetTip(xpTrack,
                    ConditioningControlPanel.Localization.Loc.GetF("label_ambient_bubble_xp_budget",
                        Services.AmbientBubbleXp.PaidToday(CoreSettings.Current), Services.AmbientBubbleXp.DailyXpCap)));
            UpdateLevelDisplay();
        }

        /// <summary>WPF MainWindow.UpdateLevelDisplay's header half.</summary>
        internal void UpdateLevelDisplay()
        {
            try
            {
                var s = CoreSettings.Current;
                var level = s.PlayerLevel;
                var xp = s.PlayerXP;
                var needed = XpCurve.GetXPForLevel(level, XpCurve.EpochOf(s));
                if (Named<TextBlock>("TxtLevelLabel") is { } label) label.Text = $"LVL {level}";
                if (Named<TextBlock>("TxtXP") is { } txt) txt.Text = $"{(int)xp} / {(int)needed} XP";
                _xpFraction = Math.Min(1.0, needed > 0 ? xp / needed : 0);
                FillXpBar(animate: true);

                // WPF RefreshProfileMenu (MainWindow.ProfileBubble.cs:315-326): same numbers as the bar.
                if (Named<TextBlock>("ProfileMenuLevel") is { } menuLevel) menuLevel.Text = $"{Loc.Get("label_level")} {level}";
                if (Named<TextBlock>("ProfileMenuXp") is { } menuXp) menuXp.Text = $"{xp:F0} / {needed:F0} {Loc.Get("label_xp")}";
                if (Named<Grid>("ProfileMenuXpRail")?.ColumnDefinitions is { Count: 2 } cols)
                {
                    var pct = Math.Clamp(_xpFraction, 0.0, 1.0);
                    cols[0].Width = new GridLength(pct, GridUnitType.Star);
                    cols[1].Width = new GridLength(1.0 - pct, GridUnitType.Star);
                }
            }
            catch (Exception ex) { Log.Debug("UpdateLevelDisplay: {E}", ex.Message); }
        }

        /// <summary>FillXpBarTo: the fill is a fraction of the bar's parent grid (not the rounded track).</summary>
        private void FillXpBar(bool animate)
        {
            if (Named<Border>("XPBar") is not { Parent: Control track } bar) return;
            var available = track.Bounds.Width;
            if (available <= 0) return;
            bar.Transitions = animate && AmbientFxCanvas.Env.AllowTransitions
                ? _xpFillTransition ??= new Transitions
                {
                    new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromSeconds(0.6), Easing = new QuadraticEaseOut() },
                }
                : null;
            bar.Width = _xpFraction * available;
        }

        // WPF MainWindow.HeroFx.cs:104-109.
        private const int SaveTickDrawMs = 400;
        private const int SaveRippleMs = 520;
        private const double SaveRippleGrowPx = 14.0;
        private const double SaveAbsorbHoldMs = 1600;
        private const double SaveAbsorbFadeMs = 260;
        private const double SaveButtonHeight = 50;

        /// <summary>
        /// WPF FlashSaveAbsorb (MainWindow.HeroFx.cs:702): the Save button's tick draws itself
        /// (StrokeDashOffset 9 -> 0, 400ms ease-out), holds 1.6s and fades 260ms; one ripple ring
        /// grows 14px past the button and fades over 520ms. Decoration only, off at MotionLevel Off;
        /// every value ends at its XAML rest, so nothing needs reverting.
        /// </summary>
        internal void FlashSaveAbsorb()
        {
            try
            {
                if (!AmbientFxCanvas.Env.AllowTransitions) return;

                if (Named<global::Avalonia.Controls.Shapes.Path>("SaveTick") is { } tick)
                {
                    // The draw ends on 0 as a local value, so the tick stays drawn after the
                    // animation lets go, as WPF's held end value does; the next press restarts at 9.
                    tick.StrokeDashOffset = 0;
                    _ = new Animation
                    {
                        Duration = TimeSpan.FromMilliseconds(SaveTickDrawMs),
                        Easing = new QuadraticEaseOut(),
                        Children =
                        {
                            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(global::Avalonia.Controls.Shapes.Shape.StrokeDashOffsetProperty, 9.0) } },
                            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(global::Avalonia.Controls.Shapes.Shape.StrokeDashOffsetProperty, 0.0) } },
                        },
                    }.RunAsync(tick);

                    // One clock for show + hold + fade.
                    double life = SaveAbsorbHoldMs + SaveAbsorbFadeMs;
                    _ = new Animation
                    {
                        Duration = TimeSpan.FromMilliseconds(life),
                        Children =
                        {
                            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1.0) } },
                            new KeyFrame { Cue = new Cue(SaveAbsorbHoldMs / life), Setters = { new Setter(OpacityProperty, 1.0) } },
                            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0.0) } },
                        },
                    }.RunAsync(tick);
                }

                if (Named<Border>("SaveRipple") is { } ripple)
                {
                    double width = Named<Control>("SaveAbsorbHost")?.Bounds.Width ?? 0;
                    double gx = width <= 1 ? 1.0 : (width + SaveRippleGrowPx) / width;
                    double gy = (SaveButtonHeight + SaveRippleGrowPx) / SaveButtonHeight;
                    // P82: transforms animate through the Visual, never RunAsync on the transform.
                    _ = new Animation
                    {
                        Duration = TimeSpan.FromMilliseconds(SaveRippleMs),
                        Easing = new QuadraticEaseOut(),
                        Children =
                        {
                            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.75), new Setter(global::Avalonia.Media.ScaleTransform.ScaleXProperty, 1.0), new Setter(global::Avalonia.Media.ScaleTransform.ScaleYProperty, 1.0) } },
                            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0.0), new Setter(global::Avalonia.Media.ScaleTransform.ScaleXProperty, gx), new Setter(global::Avalonia.Media.ScaleTransform.ScaleYProperty, gy) } },
                        },
                    }.RunAsync(ripple);
                }
            }
            catch (Exception ex) { Log.Debug("FlashSaveAbsorb: {E}", ex.Message); }
        }

        /// <summary>CelebrateLevelUp's FlashOverlay(XPBarFlashOverlay): 0 -> 1 over 250ms, auto-reversed.</summary>
        private void FlashLevelUp()
        {
            if (!AmbientFxCanvas.Env.AllowTransitions || Named<Border>("XPBarFlashOverlay") is not { } overlay) return;
            _ = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(250),
                IterationCount = new IterationCount(2),
                PlaybackDirection = PlaybackDirection.Alternate,
                Easing = new QuadraticEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.0) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1.0) } },
                },
            }.RunAsync(overlay);
        }
    }
}
