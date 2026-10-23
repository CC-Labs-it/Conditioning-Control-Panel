using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// PHASE 5 (G3): the custom keyword-trigger + Screen OCR editors, rescued from the
    /// permanently-Collapsed <c>PatreonTabView</c> and mounted on the Awareness tab.
    ///
    /// <para>Ported from the WPF code-behind, where every handler forwards to a
    /// <c>MainWindow</c> method. Those handlers split cleanly in two, and so does this port.</para>
    ///
    /// <para><b>Restored:</b> the four sliders and the two master-driven detail sections. Their
    /// state is plain <c>AppSettings</c>, which is in Core, so they are seeded from
    /// <see cref="CoreSettings"/> and write back through it - the same fields and the same clamps
    /// as <c>MainWindow.KeywordTriggers.cs</c>. The panel used to hardcode both masters ON, which
    /// showed the detail rows over a source that was switched off.</para>
    ///
    /// <para>The trigger list (add, import, per-row editors) is in KeywordTriggersPanel.TriggerList.cs.
    /// <b>Still stubbed:</b> the live services - <c>App.ScreenOcr</c>, <c>App.KeywordHighlight</c>
    /// and the KeywordTriggerService engine that would fire these triggers.</para>
    /// </summary>
    public partial class KeywordTriggersPanel : UserControl
    {
        private readonly Expander _expander;
        private readonly Border _drawerGlow;
        private BeatLoop? _drawerPulse;
        private readonly TextBlock _txtScreenOcrOffHint;
        private readonly StackPanel _screenOcrIntervalPanel;
        private readonly TextBlock _txtHighlightOffHint;
        private readonly StackPanel _highlightDurationPanel;

        /// <summary>Raised while the seed writes the sliders, so an echo is not a user edit.</summary>
        private bool _isLoading = true;

        public KeywordTriggersPanel()
        {
            AvaloniaXamlLoader.Load(this);

            _expander = this.FindControl<Expander>("KeywordTriggersExpander")!;
            _drawerGlow = this.FindControl<Border>("KeywordDrawerGlow")!;
            _txtScreenOcrOffHint = this.FindControl<TextBlock>("TxtScreenOcrOffHint")!;
            _screenOcrIntervalPanel = this.FindControl<StackPanel>("ScreenOcrIntervalPanel")!;
            _txtHighlightOffHint = this.FindControl<TextBlock>("TxtHighlightOffHint")!;
            _highlightDurationPanel = this.FindControl<StackPanel>("HighlightDurationPanel")!;

            this.FindControl<Button>("BtnAddKeywordTrigger")!.Click += (_, _) => AddTrigger();
            this.FindControl<Button>("BtnImportFromCustomTriggers")!.Click += async (_, _) => await ImportFromCustomTriggersAsync();
            // WPF KeywordTriggers.cs:135-151 persists both; the OCR scanner / highlight overlay
            // that read them are not on this head yet (ponytail: ScreenOcrService, KeywordHighlightService).
            var confirm = this.FindControl<ComboBox>("CmbOcrConfirmation")!;
            confirm.SelectionChanged += (_, _) =>
            {
                if (_isLoading || confirm.SelectedIndex < 0) return;
                CoreSettings.Current.OcrConfirmationScans = confirm.SelectedIndex + 1;
                CoreSettings.Save();
            };
            var mode = this.FindControl<ComboBox>("CmbOcrHighlightMode")!;
            mode.SelectionChanged += (_, _) =>
            {
                if (_isLoading || mode.SelectedIndex < 0) return;
                CoreSettings.Current.OcrHighlightAll = mode.SelectedIndex == 0;
                CoreSettings.Save();
            };

            // WPF's PreviewMouseWheel redirects the custom list to the outer Awareness viewport.
            var listScroll = this.FindControl<ScrollViewer>("KeywordTriggerScroller")!;
            listScroll.AddHandler(PointerWheelChangedEvent, OnTriggerListWheel, RoutingStrategies.Tunnel);

            SyncFromSettings();

            var sliders = new[]
            {
                "SliderKeywordBufferTimeout", "SliderKeywordSessionMultiplier",
                "SliderScreenOcrInterval", "SliderKeywordHighlightDuration",
            };
            foreach (var name in sliders)
                this.FindControl<Slider>(name)!.ValueChanged += OnSliderChanged;
        }

        private void OnTriggerListWheel(object? sender, PointerWheelEventArgs e)
        {
            if (sender is not ScrollViewer inner ||
                inner.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is not { } outer) return;
            // Avalonia reports one unit per notch; WPF's e.Delta is 120 per notch.
            outer.Offset = new Vector(outer.Offset.X, Math.Clamp(outer.Offset.Y - e.Delta.Y * 120,
                0, Math.Max(0, outer.Extent.Height - outer.Viewport.Height)));
            e.Handled = true;
        }

        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            StopDrawerPulse();
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        /// <summary>
        /// WPF's <c>SyncKeywordRescuePanelUi</c> half that this control owns: the four slider
        /// positions and the two master-driven sections. The clamps are the WPF ones, so a
        /// settings file written by a build with different bounds cannot throw a slider out of
        /// range here either.
        /// </summary>
        internal void SyncFromSettings()
        {
            _isLoading = true;
            try
            {
                var s = CoreSettings.Current;
                Set("SliderKeywordBufferTimeout", Math.Clamp(s.KeywordBufferTimeoutMs, 1000, 10000));
                Set("SliderKeywordSessionMultiplier", Math.Clamp(s.KeywordSessionMultiplier, 1.0, 3.0));
                Set("SliderScreenOcrInterval", Math.Clamp(s.ScreenOcrIntervalMs / 1000.0, 2, 10));
                Set("SliderKeywordHighlightDuration", Math.Clamp(s.KeywordHighlightDurationMs / 1000.0, 0.3, 5.0));

                this.FindControl<ComboBox>("CmbOcrConfirmation")!.SelectedIndex = Math.Clamp(s.OcrConfirmationScans - 1, 0, 2);
                this.FindControl<ComboBox>("CmbOcrHighlightMode")!.SelectedIndex = s.OcrHighlightAll ? 0 : 1;

                // The masters themselves live on the Awareness tab; this panel only follows them.
                // WPF SyncKeywordRescuePanelUi ANDs the OCR one with KeywordTriggerService.HasAccess.
                SetScreenOcrDetail(s.ScreenOcrEnabled && HasAccess());
                SetHighlightDetail(s.KeywordHighlightEnabled);
            }
            catch (Exception ex)
            {
                Log.Debug("KeywordTriggersPanel.SyncFromSettings failed: {E}", ex.Message);
            }
            finally
            {
                _isLoading = false;
            }
            RefreshTriggerList();

            void Set(string name, double value)
            {
                var slider = this.FindControl<Slider>(name)!;
                slider.Value = value;
                UpdateLabel(name, value);   // ValueChanged does not fire when the value is unchanged
            }
        }

        /// <summary>
        /// The four sliders' single editor. WPF keeps four one-line handlers; the fields differ but
        /// the shape does not, so one switch is the whole of it.
        /// </summary>
        private void OnSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (sender is not Slider slider || slider.Name is not { } name) return;
            UpdateLabel(name, e.NewValue);
            if (_isLoading) return;

            var s = CoreSettings.Current;
            switch (name)
            {
                case "SliderKeywordBufferTimeout":
                    var buffer = (int)e.NewValue;
                    if (s.KeywordBufferTimeoutMs == buffer) return;
                    s.KeywordBufferTimeoutMs = buffer;
                    break;
                case "SliderKeywordSessionMultiplier":
                    if (Math.Abs(s.KeywordSessionMultiplier - e.NewValue) < 0.0001) return;
                    s.KeywordSessionMultiplier = e.NewValue;
                    break;
                case "SliderScreenOcrInterval":
                    var ocr = (int)e.NewValue * 1000;
                    if (s.ScreenOcrIntervalMs == ocr) return;
                    s.ScreenOcrIntervalMs = ocr;
                    // ponytail: WPF also pushes the new interval into the running scanner
                    // (App.ScreenOcr.UpdateInterval). No OCR service and no Core seam for one on
                    // this head, so there is nothing running to re-time - the stored value is the
                    // whole effect here, which is why this is a restore and not a half-port.
                    break;
                case "SliderKeywordHighlightDuration":
                    var ms = (int)(e.NewValue * 1000);
                    if (s.KeywordHighlightDurationMs == ms) return;
                    s.KeywordHighlightDurationMs = ms;
                    break;
                default:
                    return;
            }
            CoreSettings.Save();
        }

        /// <summary>Follows the Screen OCR master: detail rows when on, the "needs source" hint when off.</summary>
        internal void SetScreenOcrDetail(bool masterOn)
        {
            _screenOcrIntervalPanel.IsVisible = masterOn;
            _txtScreenOcrOffHint.IsVisible = !masterOn;
        }

        /// <summary>Follows the highlight master: detail rows when on, the "needs source" hint when off.</summary>
        internal void SetHighlightDetail(bool masterOn)
        {
            _highlightDurationPanel.IsVisible = masterOn;
            _txtHighlightOffHint.IsVisible = !masterOn;
        }

        /// <summary>WPF KeywordTriggersPanel: expand, align the header 24px below the
        /// viewport top, and pulse on every reveal, including an already-expanded drawer.</summary>
        internal void RevealTriggerEditor()
        {
            try
            {
                _expander.IsExpanded = true;
                UpdateLayout();
                var scroll = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
                if (scroll != null && this.TranslatePoint(default, scroll) is { } top)
                {
                    var target = scroll.Offset.Y + top.Y - 24;
                    var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
                    scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(target, 0, maximum));
                }
                else this.BringIntoView();
                PulseDrawer();
            }
            catch (InvalidOperationException)
            {
                // Layout torn down mid-navigation; expansion has already landed.
            }
        }

        private void PulseDrawer()
        {
            StopDrawerPulse();
            if (!AmbientFxCanvas.Env.AllowTransitions) return;
            // WPF: 220ms QuadraticEase InOut, auto-reverse, twice, peak opacity 0.9.
            // ponytail: a sibling BoxShadow replaces the animated bitmap Effect, on the
            // existing shared beat. This finite interaction leaves no clock at rest.
            _drawerPulse ??= new BeatLoop(this, seconds =>
            {
                if (seconds >= 0.88 || !AmbientFxCanvas.Env.AllowTransitions)
                {
                    StopDrawerPulse();
                    return;
                }
                double phase = seconds % 0.44 / 0.22;
                double k = phase <= 1 ? phase : 2 - phase;
                double eased = k < 0.5 ? 2 * k * k : 1 - 2 * (1 - k) * (1 - k);
                _drawerGlow.Opacity = Math.Clamp(0.9 * eased, 0, 1);
            });
            _drawerPulse.Start();
        }

        private void StopDrawerPulse()
        {
            _drawerPulse?.Stop();
            _drawerGlow.Opacity = 0;
        }

        /// <summary>Value labels; formats copied from MainWindow.KeywordTriggers.cs, which owns
        /// them on WPF.</summary>
        private void UpdateLabel(string sliderName, double value)
        {
            var (label, text) = sliderName switch
            {
                "SliderKeywordBufferTimeout" => ("TxtKeywordBufferTimeout", $"{(int)value / 1000.0:F1}s"),
                "SliderKeywordSessionMultiplier" => ("TxtKeywordSessionMultiplier", $"{value:F1}x"),
                "SliderScreenOcrInterval" => ("TxtScreenOcrInterval", $"{(int)value}s"),
                "SliderKeywordHighlightDuration" => ("TxtKeywordHighlightDuration", $"{value:0.0}s"),
                _ => (null, null),
            };
            if (label == null) return;
            var block = this.FindControl<TextBlock>(label);
            if (block != null) block.Text = text;
        }
    }
}
