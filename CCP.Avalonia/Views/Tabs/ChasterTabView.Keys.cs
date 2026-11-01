// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: 4. the keys, Preset_Click
// and RefreshPresets (:1075-1124), Stakes (ChasterTabView.Help.cs:52), on Core TabPresets.
// A key rewrites the price list and requests the preset's limits, as WPF; it never touches the lock
// itself (prices only book when an event happens), so it does not go through the import confirm.
// ponytail: FxPreset/FxKeyTurned (Fx.cs) remain in the named follow-up slice.
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private bool _nudgingKeys;
        private BeatLoop? _keysBreath;
        private DropShadowEffect _keysGlow = null!;
        private IDisposable? _keysVisibility;

        // WPF ChasterTabView.xaml.cs:283-303 and Fx.cs:206-248: the empty menu points to the keys.
        private void NudgeInit()
        {
            _keysGlow = (DropShadowEffect)this.FindResource("KeysNudgeGlow")!;
            AttachedToVisualTree += (_, _) =>
            {
                _keysVisibility = EffectiveVisibility.Watch(this, () => Dispatcher.UIThread.Post(RefreshKeysNudge, DispatcherPriority.Loaded));
                AmbientFxCanvas.Env.MotionGateChanged += RefreshKeysNudge;
                Dispatcher.UIThread.Post(RefreshKeysNudge, DispatcherPriority.Loaded);
            };
            DetachedFromVisualTree += (_, _) =>
            {
                _keysVisibility?.Dispose(); _keysVisibility = null;
                AmbientFxCanvas.Env.MotionGateChanged -= RefreshKeysNudge;
                _keysBreath?.Stop();
            };
        }

        private void SetupHint_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_nudgingKeys || e.InitialPressMouseButton != MouseButton.Left) return;
            PresetRow.BringIntoView();
            e.Handled = true;
        }

        private void NudgeKeys(bool on)
        {
            _nudgingKeys = on;
            SetupHint.Cursor = on ? new Cursor(StandardCursorType.Hand) : null;
            SetupHint.Foreground = on ? new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x52)) : Brushes.White;
            RefreshKeysNudge();
        }

        private void RefreshKeysNudge()
        {
            _keysBreath?.Stop();
            PresetRow.Opacity = 1;
            PresetRow.Classes.Remove("keys-nudge");
            if (!_nudgingKeys || !IsEffectivelyVisible || VisualRoot == null) return;
            var tier = AmbientFxCanvas.Env.CurrentTier;
            if (AmbientFxCanvas.Env.AllowGlow(tier))
            {
                _keysGlow.BlurRadius = Math.Min(26, AmbientFxCanvas.Env.MaxGlowBlurRadius(tier));
                _keysGlow.Opacity = 0.8;
                PresetRow.Classes.Add("keys-nudge");
            }
            if (!AmbientFxCanvas.Env.AllowAmbientLoops) return;
            _keysBreath ??= new BeatLoop(this, t =>
            {
                var breath = BeatLoop.Breath(t, 1.1);
                if (PresetRow.Classes.Contains("keys-nudge")) _keysGlow.Opacity = 0.35 + 0.6 * breath;
                else PresetRow.Opacity = 0.7 + 0.3 * breath;
            });
            _keysBreath.Start();
        }

        private void KeysInit()
        {
            foreach (var key in new[] { BtnPresetGentle, BtnPresetStrict, BtnPresetCirce, BtnPresetCustom })
                key.Click += Preset_Click;
            RefreshPresets();
        }

        private void Preset_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as ToggleButton)?.Tag is not string id) return;
            // The fourth key mirrors a hand-built set; pressing it rewrites nothing.
            if (id != TabPresets.Custom && TabPresets.Apply(id) is { Count: > 0 } ids)
            {
                var settings = CoreSettings.Current;
                settings.ChasterPrices = new List<string>(ids);
                // The preset sets the stakes too: a lower limit now, a higher one after its day.
                if (TabPresets.Find(id) is { } preset)
                    (settings.ChasterDayLimit, settings.ChasterBacklogLimit) =
                        TabPresets.RequestLimits(preset, settings.ChasterDayLimit, settings.ChasterBacklogLimit, DateTime.UtcNow);
                CoreSettings.Save();
                RefreshNumbers();
            }
            ApplyPriceToggles();
            RefreshLimits();
            RefreshPresets();
            RefreshHero();
        }

        /// <summary>Light the key for the set that is on. The fourth key lights for a hand-built set.</summary>
        internal void RefreshPresets()
        {
            var match = TabPresets.Match(CoreSettings.Current.ChasterPrices);
            BtnPresetGentle.IsChecked = match == TabPresets.Gentle;
            BtnPresetStrict.IsChecked = match == TabPresets.Strict;
            BtnPresetCirce.IsChecked = match == TabPresets.Circe;
            BtnPresetCustom.IsChecked = match == TabPresets.Custom;
            Stakes(TxtStakesGentle, TabPresets.Gentle);
            Stakes(TxtStakesStrict, TabPresets.Strict);
            Stakes(TxtStakesCirce, TabPresets.Circe);
            RefreshMood(ChasterHead.Service); // WPF :1123, the heat row may have just gone on or off
            PaintMenuHelp(); // Natasha's row may have just gone on or off
        }

        private static void Stakes(TextBlock line, string presetId)
        {
            if (TabPresets.Find(presetId) is not { } preset) return;
            var (value, days) = TabPresets.WorstMonth(preset);
            line.Text = Loc.GetF(days ? "chaster_preset_stakes_days" : "chaster_preset_stakes_hours", value);
            ToolTip.SetTip(line, Loc.GetF("chaster_preset_stakes_tip", CircesTab.Format(preset.DayMinutes * 60, signed: false), TabPresets.MonthDays));
        }
    }
}
