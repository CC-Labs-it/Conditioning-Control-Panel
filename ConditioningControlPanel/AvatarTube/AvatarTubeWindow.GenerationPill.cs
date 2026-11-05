using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The tube's V1 | V2 pill (Rain, handoff-1030/RAIN-V2-PLAN.md 2.1, lane R1). Flips the GLOBAL
    /// <c>AppSettings.AvatarGeneration</c>, re-enters emote mode (ResolveEmoteFolder goes through
    /// <see cref="AvatarGenerationRule"/>) and has the new avatar say hello with its greeting clip.
    /// The arrows and EmoteSetsForActiveMod are untouched: Rain is a generation, never a set.
    /// </summary>
    public partial class AvatarTubeWindow
    {
        // null = never laid out, so the first update always applies.
        private Visibility? _genPillTarget;

        private const int GenPillFadeInMs = 180;
        private const int GenPillFadeOutMs = 140;

        /// <summary>Show, hide and paint the pill for the current mod, portrait and glitch state.</summary>
        private void UpdateGenerationPill()
        {
            try
            {
                var pill = BtnAvatarGeneration;
                if (pill == null) return;

                // RainV2Gate off (7.2.0): no pill at all, not even its glitch slot.
                bool hasRow = RainV2Gate.On
                    && EmoteGenerationFolder(App.Mods?.ActiveModId, AvatarGenerationRule.V2) != null;
                bool glitchUp = PossessionGlitchLayer?.Visibility == Visibility.Visible;
                bool show = AvatarGenerationRule.PillVisible(RainV2Gate.On, hasRow, _portraitMode, glitchUp);
                // The glitch is a blink: keep the pill's slot (Hidden) so the plate does not jump.
                var target = show ? Visibility.Visible
                           : (hasRow && !_portraitMode && glitchUp) ? Visibility.Hidden
                           : Visibility.Collapsed;

                PaintGenerationPill();
                if (_genPillTarget == target) return;
                _genPillTarget = target;
                FadeGenerationPill(pill, target);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Avatar tube: generation pill update failed ({Error})", ex.Message);
            }
        }

        /// <summary>Fade only (Gentle Visuals lengthens it and floors the rise); never a pop or a flash.</summary>
        private void FadeGenerationPill(Border pill, Visibility target)
        {
            bool show = target == Visibility.Visible;
            pill.IsHitTestVisible = show;
            if (show) pill.Visibility = Visibility.Visible;

            int ms = show ? GentleVisuals.RiseMs(GentleVisuals.FadeMs(GenPillFadeInMs))
                          : GentleVisuals.FadeMs(GenPillFadeOutMs);
            var anim = new DoubleAnimation(show ? 1.0 : 0.0, TimeSpan.FromMilliseconds(Math.Max(1, ms)))
            {
                FillBehavior = FillBehavior.HoldEnd
            };
            if (!show)
                anim.Completed += (_, _) =>
                {
                    if (_genPillTarget == target) pill.Visibility = target;
                };
            pill.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        /// <summary>The chosen segment is PRESSED IN (depth law); the other stands raised.</summary>
        private void PaintGenerationPill()
        {
            bool v2 = AvatarGenerationRule.IsV2(App.Settings?.Current?.AvatarGeneration);
            PaintPillSegment(PillSegV1, PillTxtV1, on: !v2);
            PaintPillSegment(PillSegV2, PillTxtV2, on: v2);
            BtnAvatarGeneration.ToolTip = Loc.Get(v2 ? "tube_pill_v2_tip" : "tube_pill_v1_tip");
        }

        private void PaintPillSegment(Border seg, TextBlock txt, bool on)
        {
            seg.Background = (TryFindResource(on ? "DepthPressedShade" : "DepthRaisedSheen") as Brush) ?? Brushes.Transparent;
            if (on) txt.SetResourceReference(TextBlock.ForegroundProperty, "PinkBrush");
            else txt.Foreground = PillMutedText;
        }

        // Same muted lilac as the plate's level line (TxtAvatarLevel).
        private static readonly Brush PillMutedText = CreateFrozen(Color.FromRgb(0xB8, 0xA9, 0xC6));

        private static Brush CreateFrozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private void BtnAvatarGeneration_Click(object sender, MouseButtonEventArgs e)
        {
            // A click, never a drag start (Window_MouseLeftButtonDown) or a poke.
            e.Handled = true;
            var s = App.Settings?.Current;
            if (s == null || _genPillTarget != Visibility.Visible || !RainV2Gate.On) return;

            s.AvatarGeneration = AvatarGenerationRule.Toggle(s.AvatarGeneration);
            try { App.Settings?.Save(); }
            catch (Exception ex) { App.Logger?.Debug("Avatar tube: saving the avatar generation failed ({Error})", ex.Message); }
            App.Logger?.Information("Avatar generation switched to {Generation}.", s.AvatarGeneration);

            TryUpdateCirceEmoteMode();   // also repaints the pill
            UpdateQuickMenuState();      // the "Rain's wardrobe" door follows the generation

            // Hello from the new avatar. Queued at Background so it lands after the fresh first idle
            // EnterCirceEmoteMode defers (and after the tooltip popup's resize: the render deadlock
            // note there). skipHold: the greeting replaces that idle instead of waiting it out.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!_circeEmoteMode) return;
                var clip = ResolveReaction("greeting", null);
                if (clip != null) StartReactionOnly(clip, 0, skipHold: true);
            }));
        }
    }
}
