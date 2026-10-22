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
// XP readout, meniscus and chip pop now follow WPF on the existing shared beat (see below).
//
// Level-up also bursts on the track's cap (CelebrateLevelUp FireBurstAt, EventFx.cs:262), shows the
// level-up notification and plays lvup.mp3 (OnLevelUp, MainWindow.xaml.cs:770-776); the rank title
// follows the level (UpdateLevelDisplay, UiUpdates.cs:71-79). START/Save hero FX: HeroStart.cs.
// STILL MISSING vs WPF, so the row stays a stub: THE BANK hold (MainWindow.BankFx.cs, a stub here,
// so CelebrateLevelUp's AbortBankFlight has nothing to abort) and the avatar tube's level art
// (_avatarTubeWindow.UpdateAvatarForLevel).
// Sign-in, startup profile restore and logout repaint through UpdateQuickLoginUI
// (MainShellWindow.Login.cs), and the login dialog's ProfileLoad is awaited before a repaint.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
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
            Action<int> levelUp = newLevel => Dispatcher.UIThread.Post(() =>
            {
                CelebrateLevelUp();
                UpdateLevelDisplay();
                // WPF OnLevelUp: the tray balloon (here the OS notification) and the level-up sound.
                Platform.OsNotifications.Show(Loc.Get("toast_level_up_title"), Loc.GetF("toast_level_up_body", newLevel));
                PlayLevelUpSound();
            });
            HookHeroXpFx();
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
                // WPF UiUpdates.cs:71-79: the rank title by level, made mod-aware. The XAML {loc:Str}
                // binds at Template priority, so this local value survives a language switch (P09).
                var rankTitle = level switch
                {
                    < 20 => "BASIC BIMBO",
                    < 50 => "DUMB AIRHEAD",
                    < 100 => "SYNTHETIC BLOWDOLL",
                    _ => "PERFECT FUCKPUPPET",
                };
                if (Named<TextBlock>("TxtPlayerTitle") is { } title) title.Text = CoreMods.MakeModAware(rankTitle);
                if (Named<TextBlock>("TxtXP") is { } txt) AnimateXpReadout(txt, xp, needed, level);
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
            double targetWidth = _xpFraction * available;
            bar.Width = targetWidth;
            AnimateXpMeniscus(targetWidth, animate);
        }

        private BeatLoop? _xpMeniscusPulse, _xpOdometer, _levelChipPop;
        private double _xpMeniscusFillWidth, _lastXpShown = double.NaN;
        private int _lastXpLevelShown;
        private string? _xpReadoutTarget;
        private bool _heroClosed;
        internal bool XpMeniscusPulsing => _xpMeniscusPulse?.IsRunning == true;
        internal bool XpReadoutCounting => _xpOdometer?.IsRunning == true;
        internal bool LevelChipPopping => _levelChipPop?.IsRunning == true;

        private void HookHeroXpFx()
        {
            HookHeroStartFx();
            Opened += (_, _) =>
            {
                // Opened fires again on every tray Hide/Show: never stack the static subscription (P41).
                AmbientFxCanvas.Env.MotionGateChanged -= RefreshHeroXpFx;
                AmbientFxCanvas.Env.MotionGateChanged += RefreshHeroXpFx;
                RefreshHeroXpFx();
            };
            Activated += (_, _) => { ApplyXpMeniscusPulse(); ApplyStartHeroState(); };
            Deactivated += (_, _) => { ApplyXpMeniscusPulse(); ApplyStartHeroState(); };
            PropertyChanged += (_, e) =>
            {
                if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) RefreshHeroXpFx();
            };
            Closed += (_, _) =>
            {
                _heroClosed = true;
                AmbientFxCanvas.Env.MotionGateChanged -= RefreshHeroXpFx;
                _xpMeniscusPulse?.Stop();
                _xpOdometer?.Stop();
                _levelChipPop?.Stop();
            };
        }

        private void RefreshHeroXpFx()
        {
            ApplyXpMeniscusPulse();
            ApplyStartHeroState();
            if (AmbientFxCanvas.Env.AllowTransitions && IsVisible && WindowState != WindowState.Minimized) return;
            EndStartIgnition();
            EndSaveAbsorb();
            _xpOdometer?.Stop();
            if (_xpReadoutTarget != null && Named<TextBlock>("TxtXP") is { } txt) txt.Text = _xpReadoutTarget;
            _levelChipPop?.Stop();
            PaintLevelChipPop(1);
            FillXpBar(animate: false);
        }

        // Adapted from parity/trunk 7b6f94800 (AnimateXpReadout); use the existing shared beat
        // instead of importing the trunk's independent 16ms DispatcherTimer.
        private void AnimateXpReadout(TextBlock txt, double xp, double needed, int level)
        {
            string target = $"{(int)xp} / {(int)needed} XP";
            // Awarded and LevelUp can repaint the same total in one dispatcher turn. Keep the
            // first count alive rather than snapping it to its own target on the second repaint.
            if (_xpOdometer?.IsRunning == true && level == _lastXpLevelShown && xp == _lastXpShown
                && target == _xpReadoutTarget && AmbientFxCanvas.Env.AllowTransitions && IsVisible) return;
            double from = !double.IsNaN(_lastXpShown) && level == _lastXpLevelShown ? _lastXpShown : 0;
            (_lastXpShown, _lastXpLevelShown) = (xp, level);
            _xpOdometer?.Stop();
            _xpReadoutTarget = target;
            if (_heroClosed || !AmbientFxCanvas.Env.AllowTransitions || !IsVisible
                || WindowState == WindowState.Minimized || Math.Abs(xp - from) < 0.5)
            {
                txt.Text = _xpReadoutTarget;
                return;
            }
            string tail = $" / {(int)needed} XP";
            _xpOdometer = new BeatLoop(txt, seconds =>
            {
                double p = Math.Clamp(seconds / 0.7, 0, 1);
                double eased = 1 - (1 - p) * (1 - p);
                txt.Text = (from + (xp - from) * eased).ToString("F0", System.Globalization.CultureInfo.CurrentCulture) + tail;
                if (p >= 1) _xpOdometer?.Stop();
            });
            _xpOdometer.Start();
        }

        // WPF AnimateXpMeniscus: exactly the fill's 600ms quadratic-out target and easing.
        private void AnimateXpMeniscus(double toWidth, bool animate)
        {
            _xpMeniscusFillWidth = toWidth;
            if (Named<Border>("XPMeniscus") is not { RenderTransform: TranslateTransform slide } dot) return;
            slide.Transitions = animate && AmbientFxCanvas.Env.AllowTransitions
                ? new Transitions
                {
                    new DoubleTransition { Property = TranslateTransform.XProperty,
                        Duration = TimeSpan.FromSeconds(0.6), Easing = new QuadraticEaseOut() },
                } : null;
            slide.X = Math.Max(0, toWidth - (double.IsNaN(dot.Width) ? 12 : dot.Width) / 2);
            ApplyXpMeniscusPulse();
        }

        private void ApplyXpMeniscusPulse()
        {
            if (Named<Border>("XPMeniscus") is not { } dot) return;
            bool ambient = !_heroClosed && AmbientFxCanvas.Env.AllowAmbientLoops && IsActive
                && IsVisible && WindowState != WindowState.Minimized;
            if (_xpMeniscusFillWidth < 5 || !ambient)
            {
                _xpMeniscusPulse?.Stop();
                dot.Opacity = _xpMeniscusFillWidth < 5 ? 0 : 0.55;
                return;
            }
            _xpMeniscusPulse?.Stop();
            _xpMeniscusPulse ??= new BeatLoop(dot, seconds => dot.Opacity = 0.35 + 0.60 * BeatLoop.Breath(seconds, 2));
            _xpMeniscusPulse.Start();
        }

        // WPF PopLevelChip: scale/tilt peak at 180ms, settle at 600ms. Plain transform writes on
        // the shared beat avoid Animation.RunAsync's invalid Transform target cast.
        internal void PopLevelChip()
        {
            if (_heroClosed || !AmbientFxCanvas.Env.AllowTransitions) return;
            _levelChipPop?.Stop();
            if (Named<Border>("LevelChip") is not { } chip) return;
            _levelChipPop = new BeatLoop(chip, seconds =>
            {
                double p = Math.Clamp(seconds / 0.6, 0, 1);
                PaintLevelChipPop(p);
                if (p >= 1) _levelChipPop?.Stop();
            });
            _levelChipPop.Start();
        }

        private void PaintLevelChipPop(double p)
        {
            if (Named<Border>("LevelChip")?.RenderTransform is not TransformGroup { Children.Count: 2 } group
                || group.Children[0] is not ScaleTransform scale || group.Children[1] is not RotateTransform rotate) return;
            double amount;
            if (p <= 0.3)
            {
                double t = p / 0.3;
                amount = 1 - (1 - t) * (1 - t);
            }
            else
            {
                double t = 1 - (p - 0.3) / 0.7;
                amount = t * t * t - t * 0.6 * Math.Sin(t * Math.PI);
            }
            scale.ScaleX = scale.ScaleY = 1 + 0.35 * amount;
            rotate.Angle = -4 * amount;
        }

        private readonly EventBurstLayer _levelUpBurst = new();
        private Action? _levelUpSoundStop;

        /// <summary>Level-up bursts fired (test hook).</summary>
        internal int LevelUpBursts => _levelUpBurst.Count;

        /// <summary>WPF CelebrateLevelUp (EventFx.cs:249): bloom, chip pop and a 110-spark burst on the
        /// track's right edge (the cap the fill just reached), all off at MotionLevel Off.</summary>
        private void CelebrateLevelUp()
        {
            try
            {
                if (!AmbientFxCanvas.Env.AllowTransitions) return;
                FlashLevelUp();
                PopLevelChip();
                if (Named<Border>("XPBarTrack") is { } track) _levelUpBurst.Fire(track, 110, rightEdge: true);
            }
            catch (Exception ex) { Log.Debug("CelebrateLevelUp: {E}", ex.Message); }
        }

        /// <summary>WPF PlayLevelUpSound (MainWindow.xaml.cs:782): lvup.mp3 at master^1.5 x 0.2625, the
        /// previous one stopped first.</summary>
        private void PlayLevelUpSound()
        {
            try
            {
                var soundPath = new[]
                {
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "lvup.mp3"),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "lvlup.mp3"),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sounds", "lvlup.mp3"),
                }.FirstOrDefault(System.IO.File.Exists);
                if (soundPath == null) { Log.Debug("Level up sound not found"); return; }
                StopLevelUpSound();
                var curved = (float)Math.Pow(CoreSettings.Current.MasterVolume / 100f, 1.5) * 0.2625f;
                _levelUpSoundStop = CoreAudio.PlayStoppable(soundPath, Math.Max(0.01f, curved), "level-up");
            }
            catch (Exception ex) { Log.Warning("Failed to play level up sound: {Error}", ex.Message); }
        }

        private void StopLevelUpSound()
        {
            try { _levelUpSoundStop?.Invoke(); } catch { }
            _levelUpSoundStop = null;
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
