using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Bubble Pop panel, ported from the WPF head, settings logic restored against
    /// <see cref="CoreSettings"/>. Every toggle and slider round-trips the AppSettings property
    /// the WPF original wrote and saves; the trigger-options reveal and the seven trigger-type
    /// checkboxes drive <c>BubbleTriggerVariants</c> the same way.
    ///
    /// <para>The WPF <c>SettingsHook</c>/<c>ISettingsRebindable</c> pair is inlined: a cloud
    /// restore SWAPS the settings instance, so the PropertyChanged subscription is tracked by
    /// instance and re-pointed on <c>SettingsService.CurrentReplaced</c>.</para>
    ///
    /// <para>The easter-egg hint names the active persona from <see cref="CoreMods.ActiveModId"/>,
    /// and <see cref="CoreMods.ModChanged"/> repaints it - the rack hosts this control
    /// permanently, so a mod switch has to be followed.</para>
    /// </summary>
    public partial class BubblePopFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public BubblePopFeatureControl()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and everything below reads them.
            InitializeComponent();

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            SliderFreq.ValueChanged += SliderFreq_Changed;
            SliderVolume.ValueChanged += SliderVolume_Changed;
            SliderSize.ValueChanged += SliderSize_Changed;
            SliderSpeed.ValueChanged += SliderSpeed_Changed;
            ChkSolidMode.IsCheckedChanged += ChkSolidMode_Changed;
            ChkTriggers.IsCheckedChanged += ChkTriggers_Changed;
            SliderTriggerChance.ValueChanged += SliderTriggerChance_Changed;
            foreach (var box in TriggerTypeBoxes()) box.IsCheckedChanged += TriggerType_Changed;
            CmbMotion.SelectionChanged += CmbMotion_Changed;
            ChkBubbleGazePop.IsCheckedChanged += ChkBubbleGazePop_Changed;

            // WPF ApplyFeatureArt: the hero strip and side plate, repainted on a mod switch.
            Helpers.ModArt.BindFeaturePlates(this, "features/Bubble_pop.png", HeroArt, SideArt);
            RowGetBubblesV2.RowChanged += (_, _) => RebuildMotionPicker();
            // WPF includes the Brain Drain bubble in this prize (BubbleService.cs:1159,1202).
            // Keep the offer out of reach until that effect is implemented on this head.
            RowGetBubblesV2.Configure(Services.Prizes.V2PurchaseRule.BubblesPrizeId, "v2_get_bubble_blurb", "label_bubbles_v2_box", available: false);

            LoadFromSettings();
        }

        /// <summary>The seven effect boxes, each carrying its variant id in <c>Tag</c>.</summary>
        private IEnumerable<CheckBox> TriggerTypeBoxes()
        {
            yield return ChkTypeFlash;
            yield return ChkTypeSubliminal;
            yield return ChkTypePink;
            yield return ChkTypeSpiral;
            yield return ChkTypeGlitch;
            yield return ChkTypeCascade;
            yield return ChkTypeVideo;
        }

        // ---- settings instance tracking (WPF: SettingsHook + ISettingsRebindable) --------------

        private AppSettings? _hooked;
        private bool _ownershipAttached;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            CoreMods.ModChanged += OnModChanged;
            Overlays.BubbleOverlay.XpBudgetChanged += UpdateAmbientXpBudgetLine;   // WPF AmbientXpBudgetChanged
            _ownershipAttached = true;
            Services.Prizes.PrizeGrants.GrantsChanged += OnGrantsChanged;
            CoreAccount.UnifiedIdentityChanged += OnIdentityChanged;
            Platform.WebcamTracker.Instance.StateChanged += OnWebcamStateChanged;
            RebindToCurrentSettings();
            RebuildMotionPicker();   // grants may have changed while detached (WPF OnLoaded:30)
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            CoreMods.ModChanged -= OnModChanged;
            Overlays.BubbleOverlay.XpBudgetChanged -= UpdateAmbientXpBudgetLine;
            _ownershipAttached = false;
            Services.Prizes.PrizeGrants.GrantsChanged -= OnGrantsChanged;
            CoreAccount.UnifiedIdentityChanged -= OnIdentityChanged;
            Platform.WebcamTracker.Instance.StateChanged -= OnWebcamStateChanged;
            Unhook();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(RebindToCurrentSettings);

        private void RebindToCurrentSettings()
        {
            Unhook();
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsPropertyChanged;
            LoadFromSettings();
        }

        private void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsPropertyChanged;
            _hooked = null;
        }

        /// <summary>
        /// ModChanged can be raised off the UI thread, so the repaint is marshalled. The persona
        /// line is the only thing here that changes answer on a mod switch (WPF also repainted the
        /// two art plates, which this head does not draw).
        /// </summary>
        private void OnModChanged(object? sender, ModPackage mod) =>
            Dispatcher.UIThread.Post(LoadFromSettings);

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.BubblesEnabled;
                SliderFreq.Value = s.BubblesFrequency;
                TxtFreq.Text = s.BubblesFrequency.ToString();
                SliderVolume.Value = s.BubblesVolume;
                TxtVolume.Text = $"{s.BubblesVolume}%";
                SliderSize.Value = s.BubblesSize;
                TxtSize.Text = $"{s.BubblesSize}%";
                SliderSpeed.Value = s.BubbleSpeedBoost;
                TxtSpeed.Text = $"+{s.BubbleSpeedBoost}%";
                ChkSolidMode.IsChecked = s.BubbleSharedHost;
                ChkBubbleGazePop.IsChecked = s.BubbleGazePopEnabled;
                UpdateGazeHint();

                // Easter-egg hint (companion auto-pops a lingering effect bubble) — name the active persona.
                var persona = CoreMods.ActiveModId switch
                {
                    "builtin-bambisleep" => "Bambi",
                    "builtin-sissyhypno" => "your bimbo",
                    "builtin-locked" => "Circe",
                    _ => "your companion"
                };
                TxtTriggerEggHint.Text = Loc.GetF("label_trigger_bubbles_egg_hint", persona);

                ChkTriggers.IsChecked = s.BubbleTriggersEnabled;
                TriggerOptionsPanel.IsVisible = s.BubbleTriggersEnabled;
                SliderTriggerChance.Value = s.BubbleTriggerChance;
                TxtTriggerChance.Text = $"{s.BubbleTriggerChance}%";
                var ids = s.BubbleTriggerVariants ?? new List<string>();
                foreach (var box in TriggerTypeBoxes())
                    box.IsChecked = box.Tag is string id && ids.Contains(id);
                // WPF builds the items on GrantsChanged and only calls SelectMotion on a settings tick.
                if (CmbMotion.Items.Count == 0) RebuildMotionPicker();
                SelectMotion(s.BubbleMotionStyle);
                UpdateAmbientXpBudgetLine();
            }
            finally { _isLoading = false; }
        }

        // ---- Bubbles v2 motion picker (WPF BubblePopFeatureControl.xaml.cs:310-396) -----------
        // WPF BubblePopFeatureControl.xaml.cs:24-46,299-307: listen only while attached,
        // marshal grants to the UI thread, and ignore a queued repaint after detach.
        private void OnIdentityChanged(object? sender, EventArgs e) => OnGrantsChanged();

        private void OnGrantsChanged() => Dispatcher.UIThread.Post(() =>
        {
            if (_ownershipAttached) RowGetBubblesV2.Apply();
        });

        /// <summary>Float up always; Rain / Spiral In only when owned (each with its v2 pill); Mix once
        /// any v2 style is owned. The purchase row can also keep the box visible.</summary>
        private void RebuildMotionPicker()
        {
            bool rain = Platform.PrizeOwnership.IsGranted(Platform.PrizeOwnership.BubbleRain);
            bool spiral = Platform.PrizeOwnership.IsGranted(Platform.PrizeOwnership.BubbleSpiralIn);
            MotionRow.IsVisible = rain || spiral;
            V2Box.IsVisible = rain || spiral || !RowGetBubblesV2.IsRowHidden;
            var wasLoading = _isLoading;
            _isLoading = true;
            try
            {
                CmbMotion.Items.Clear();
                AddMotionChoice(BubbleMotionStyle.FloatUp, "bubble_motion_float_up", v2: false);
                if (rain) AddMotionChoice(BubbleMotionStyle.Rain, "bubble_motion_rain", v2: true);
                if (spiral) AddMotionChoice(BubbleMotionStyle.SpiralIn, "bubble_motion_spiral_in", v2: true);
                if (rain || spiral) AddMotionChoice(BubbleMotionStyle.Mix, "bubble_motion_mix", v2: false);
                SelectMotion(CoreSettings.Current.BubbleMotionStyle);
            }
            finally { _isLoading = wasLoading; }
        }

        private void SelectMotion(BubbleMotionStyle current)
        {
            // An unowned (absent) style shows as Float up, which is how it plays.
            CmbMotion.SelectedItem = CmbMotion.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag is BubbleMotionStyle t && t == current) ?? CmbMotion.Items[0];
        }

        private void AddMotionChoice(BubbleMotionStyle style, string key, bool v2)
        {
            var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = Loc.Get(key), VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center });
            if (v2)
                row.Children.Add(new Border
                {
                    // WPF V2Pill: the FeatureCard tier-badge family in neon purple.
                    Margin = new Thickness(7, 0, 0, 0), Padding = new Thickness(5, 1, 6, 2), CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x1A, 0x1A, 0x2E)),
                    BorderBrush = V2Purple, BorderThickness = new Thickness(1), IsHitTestVisible = false,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Child = new TextBlock { Text = Loc.Get("fx_v2_badge"), Foreground = V2Purple, FontSize = 9, FontWeight = FontWeight.Bold },
                });
            CmbMotion.Items.Add(new ComboBoxItem { Content = row, Tag = style });
        }

        private static IBrush V2Purple =>
            global::Avalonia.Application.Current?.TryFindResource("NeonPurpleBrush", out var v) == true && v is IBrush b ? b : Brushes.MediumPurple;

        private void CmbMotion_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (CmbMotion.SelectedItem is not ComboBoxItem { Tag: BubbleMotionStyle style }) return;
            var s = CoreSettings.Current;
            if (s.BubbleMotionStyle == style) return;
            s.BubbleMotionStyle = style;
            CoreSettings.Save();
            // No service bounce: every spawn resolves the picker, so the next bubble uses it.
        }

        /// <summary>
        /// "Ambient bubble XP: N/300 today" (#1019/#1026). Ambient pops stop paying once the daily
        /// bucket is spent; before this line the ceiling was completely invisible.
        /// </summary>
        private void UpdateAmbientXpBudgetLine()
        {
            TxtAmbientXpBudget.Text = Loc.GetF("label_ambient_bubble_xp_budget",
                AmbientBubbleXp.PaidToday(CoreSettings.Current), AmbientBubbleXp.DailyXpCap);
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.BubblesEnabled) ||
                e.PropertyName == nameof(AppSettings.BubblesFrequency) ||
                e.PropertyName == nameof(AppSettings.BubblesVolume) ||
                e.PropertyName == nameof(AppSettings.BubblesSize) ||
                e.PropertyName == nameof(AppSettings.BubbleSpeedBoost) ||
                e.PropertyName == nameof(AppSettings.BubbleGazePopEnabled) ||
                e.PropertyName == nameof(AppSettings.BubbleTriggersEnabled) ||
                e.PropertyName == nameof(AppSettings.BubbleTriggerChance) ||
                e.PropertyName == nameof(AppSettings.BubbleTriggerVariants) ||
                e.PropertyName == nameof(AppSettings.BubbleMotionStyle))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.BubblesEnabled = ChkEnable.IsChecked ?? false;
            CoreSettings.Save();

            // Live-apply: start/stop the bubble service if the engine is running.
            if (CoreSession.IsEngineRunning)
            {
                if (CoreSettings.Current.BubblesEnabled) CoreBubbles.Start(); else CoreBubbles.Stop();
            }
        }

        private void SliderFreq_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtFreq.Text = v.ToString();
            CoreSettings.Current.BubblesFrequency = v;
            CoreBubbles.RefreshFrequency();
            CoreSettings.Save();
        }

        private void SliderVolume_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtVolume.Text = $"{v}%";
            CoreSettings.Current.BubblesVolume = v;
            CoreSettings.Save();
        }

        private void SliderSize_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtSize.Text = $"{v}%";
            CoreSettings.Current.BubblesSize = v;
            CoreSettings.Save();
            // No live-apply hook: size is read when each bubble is CONSTRUCTED, so the change
            // shows on the next spawn without disturbing the ones already drifting. Restarting the
            // service to resize mid-flight would pop the field out from under the user.
        }

        private void SliderSpeed_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtSpeed.Text = $"+{v}%";
            CoreSettings.Current.BubbleSpeedBoost = v;
            CoreSettings.Save();
        }

        private void ChkSolidMode_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.BubbleSharedHost = ChkSolidMode.IsChecked ?? false;
            CoreSettings.Save();

            // The render path is latched per Start->Stop session, so a live bubble service has to
            // be bounced to pick up the new mode. Two of WPF's three conjuncts are real here; the
            // third ("is it actually running") is the service's own.
            if (CoreSession.IsEngineRunning && CoreSettings.Current.BubblesEnabled)
            {
                CoreBubbles.Stop();
                CoreBubbles.Start();
            }
        }

        private void ChkTriggers_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var on = ChkTriggers.IsChecked ?? false;
            CoreSettings.Current.BubbleTriggersEnabled = on;
            TriggerOptionsPanel.IsVisible = on;
            CoreSettings.Save();
        }

        private void SliderTriggerChance_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtTriggerChance.Text = $"{v}%";
            CoreSettings.Current.BubbleTriggerChance = v;
            CoreSettings.Save();
        }

        private void TriggerType_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox cb || cb.Tag is not string id) return;

            var s = CoreSettings.Current;
            var ids = new List<string>(s.BubbleTriggerVariants ?? new List<string>());
            if (cb.IsChecked ?? false) { if (!ids.Contains(id)) ids.Add(id); }
            else ids.Remove(id);
            s.BubbleTriggerVariants = ids;   // reassign so the setter fires change notification
            CoreSettings.Save();
        }

        /// <summary>
        /// WPF ChkBubbleGazePop_Changed: the bubble twin of the Flash page's gaze pop. GazeFocus
        /// follows the setting itself, so there is nothing to start or stop here.
        /// </summary>
        private void ChkBubbleGazePop_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var want = ChkBubbleGazePop.IsChecked ?? false;
            if (s.BubbleGazePopEnabled == want) return;   // an echo of the seed must not save
            s.BubbleGazePopEnabled = want;
            CoreSettings.Save();
            UpdateGazeHint();
        }

        /// <summary>The camera reports state off the UI thread; the repaint is marshalled.</summary>
        private void OnWebcamStateChanged() => Dispatcher.UIThread.Post(UpdateGazeHint);

        /// <summary>
        /// WPF UpdateGazeHint: "Stare to pop" is a stored preference that does nothing until the
        /// shared camera runs on a stored calibration with current consent - the conditions
        /// GazeFocus checks before it starts the dwell engine. Without the line the toggle reads
        /// as broken, so the row stays live and gains a hint.
        /// </summary>
        private void UpdateGazeHint()
        {
            try
            {
                var cam = Platform.WebcamTracker.Instance;
                TxtBubbleGazeHint.IsVisible = !(cam.IsRunning && cam.Calibration != null
                    && Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current));
            }
            catch (Exception ex) { Serilog.Log.Debug("BubblePop gaze hint: {E}", ex.Message); }
        }
    }
}
