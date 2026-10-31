using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Subliminal settings panel, ported from the WPF head. Every editor reads and writes
    /// <see cref="CoreSettings.Current"/> and persists through <see cref="CoreSettings.Save"/>,
    /// and the settings hook is inlined for the reason spelled out in
    /// <see cref="BubbleCountFeatureControl"/>.
    ///
    /// <para>Installed fonts preview per row; the bundled Fredoka sentinel resolves through AppFonts.</para>
    ///
    /// <para>Still head-side: the flash SURFACE (Views/Overlays/SubliminalOverlay) and the
    /// mod-aware feature art. The loop itself is not - the enable toggle, the interval and the
    /// phrase pick are <see cref="CoreSubliminal"/>; see <see cref="ChkEnable_Changed"/> for how
    /// this head arms it.</para>
    /// </summary>
    public partial class SubliminalFeatureControl : UserControl
    {
        /// <summary>What the picker falls back to when the stored family is gone - the WPF
        /// <c>Populate(CmbFont, s.SubliminalFont, "Arial")</c> fallback, unchanged.</summary>
        private const string FontFallback = "Arial";

        private bool _isLoading = true;
        private AppSettings? _hooked;

        public SubliminalFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            PopulateFonts();
            // WPF FontPickerHelper.cs:246: preview the selected face in the closed picker.
            // Scope the face to its text so sibling icons retain their inherited font.
            CmbFont.SelectionBoxItemTemplate = new FuncDataTemplate<string>((name, _) => new TextBlock
            {
                Text = name,
                FontFamily = CmbFont.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, name)).FontFamily,
            });

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            SliderPerMin.ValueChanged += SliderPerMin_Changed;
            SliderFrames.ValueChanged += SliderFrames_Changed;
            SliderOpacity.ValueChanged += SliderOpacity_Changed;
            ChkWhispers.IsCheckedChanged += ChkWhispers_Changed;
            SliderWhisperVol.ValueChanged += SliderWhisperVol_Changed;
            ChkSolidMode.IsCheckedChanged += ChkSolidMode_Changed;
            CmbFont.SelectionChanged += CmbFont_Changed;
            BtnManageMessages.Click += BtnManageMessages_Click;
            BtnAdvanced.Click += BtnAdvanced_Click;

            Loaded += (_, _) => { RebindToCurrentSettings(); CoreMods.ModChanged += OnModChanged; };
            Unloaded += (_, _) => { Unhook(); CoreMods.ModChanged -= OnModChanged; };

            Helpers.ModArt.BindFeaturePlates(this, "features/subliminal.png", HeroArt, SideArt);

            RebindToCurrentSettings();
        }

        /// <summary>Re-points the settings hook at the live instance and repaints from it.</summary>
        public void RebindToCurrentSettings()
        {
            Unhook();
            // Hidden rack controls can be constructed without ever loading/unloading.
            if (IsLoaded)
            {
                _hooked = CoreSettings.Current;
                _hooked.PropertyChanged += OnSettingsPropertyChanged;
            }
            LoadFromSettings();
        }

        private void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsPropertyChanged;
            _hooked = null;
        }

        /// <summary>
        /// Built once, in the constructor. The settings hook re-runs
        /// <see cref="LoadFromSettings"/> on every property in this feature's chain - a slider drag
        /// included - and rebuilding several hundred rows each time would stutter, which is the
        /// same reason WPF's Populate keeps a "already filled" marker. Only the selection moves.
        /// </summary>
        private void PopulateFonts()
        {
            string[] names;
            try { names = FontManager.Current.SystemFonts.Select(f => f.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Subliminal font enumeration failed, using the fallback list");
                names = Array.Empty<string>();
            }
            if (names.Length == 0) names = new[] { FontFallback };

            foreach (var name in new[] { "Fredoka (bundled)" }.Concat(names))
            {
                // Tag carries the value that gets stored; Content is what the row shows. Each row
                // renders in its own face, so the list reads as a real preview, as on WPF.
                CmbFont.Items.Add(new ComboBoxItem
                {
                    Content = name,
                    Tag = name,
                    FontFamily = name == "Fredoka (bundled)" ? new FontFamily(Platform.AppFonts.Fredoka) : new FontFamily($"{name}, {FontFallback}"),
                    FontSize = 14,
                });
            }
        }

        /// <summary>
        /// Moves the selection to the stored family, else to the fallback, else row 0 - WPF's
        /// <c>match ?? fallbackItem</c> chain.
        /// </summary>
        private void SelectFont(string? wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted)) wanted = FontFallback;
            ComboBoxItem? fallbackItem = null;
            foreach (var obj in CmbFont.Items)
            {
                if (obj is not ComboBoxItem item || item.Tag is not string name) continue;
                if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    CmbFont.SelectedItem = item;
                    return;
                }
                if (fallbackItem == null && string.Equals(name, FontFallback, StringComparison.OrdinalIgnoreCase))
                    fallbackItem = item;
            }
            if (fallbackItem != null) CmbFont.SelectedItem = fallbackItem;
            else if (CmbFont.SelectedItem == null && CmbFont.ItemCount > 0) CmbFont.SelectedIndex = 0;
        }

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.SubliminalEnabled;
                SliderPerMin.Value = s.SubliminalFrequency;
                TxtPerMin.Text = s.SubliminalFrequency.ToString();
                SliderFrames.Value = s.SubliminalDuration;
                TxtFrames.Text = s.SubliminalDuration.ToString();
                SliderOpacity.Value = s.SubliminalOpacity;
                TxtOpacity.Text = $"{s.SubliminalOpacity}%";
                ChkWhispers.IsChecked = s.SubAudioEnabled;
                SliderWhisperVol.Value = s.SubAudioVolume;
                TxtWhisperVol.Text = $"{s.SubAudioVolume}%";
                ChkSolidMode.IsChecked = s.SubliminalSolidMode;
                SelectFont(s.SubliminalFont);
            }
            finally { _isLoading = false; }
            RefreshSilentHint();
        }

        private void OnModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(RefreshSilentHint);

        internal void RefreshSilentHint() => TxtWhispersSilentHint.IsVisible =
            ChkWhispers.IsChecked == true && Services.SpokenClipRule.AnySilent(
                CoreSettings.Current.SubliminalPool.Where(x => x.Value).Select(x => x.Key),
                Overlays.SubliminalWhisperShow.HasRecordedVoice);

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.SubliminalEnabled) ||
                e.PropertyName == nameof(AppSettings.SubliminalFrequency) ||
                e.PropertyName == nameof(AppSettings.SubliminalDuration) ||
                e.PropertyName == nameof(AppSettings.SubliminalOpacity) ||
                e.PropertyName == nameof(AppSettings.SubAudioEnabled) ||
                e.PropertyName == nameof(AppSettings.SubAudioVolume) ||
                e.PropertyName == nameof(AppSettings.SubliminalSolidMode) ||
                e.PropertyName == nameof(AppSettings.SubliminalFont))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        /// <summary>
        /// The whole toggle, routed through the one authority - <see cref="CoreSubliminal.SetEnabled"/>,
        /// which is where WPF's <c>SubliminalService.SetEnabled</c> now lives too. It writes the
        /// flag, and while the engine is running it also arms or disarms the scheduler. The card
        /// does not repeat any of that: two copies of "compare, write, gate, save" is exactly how
        /// the heads drift apart.
        /// Core arms the scheduler only behind <c>CoreSession.IsEngineRunning</c> (seeded from
        /// <see cref="CoreEngine"/>), so while stopped the toggle only saves.
        /// </summary>
        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var on = ChkEnable.IsChecked ?? false;
            CoreSubliminal.SetEnabled(on);
        }

        private void SliderPerMin_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtPerMin.Text = v.ToString();
            if (s.SubliminalFrequency == v) return;
            s.SubliminalFrequency = v;
            CoreSettings.Save();
        }

        private void SliderFrames_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtFrames.Text = v.ToString();
            if (s.SubliminalDuration == v) return;
            s.SubliminalDuration = v;
            CoreSettings.Save();
        }

        private void SliderOpacity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtOpacity.Text = $"{v}%";
            if (s.SubliminalOpacity == v) return;
            s.SubliminalOpacity = v;
            CoreSettings.Save();
        }

        private void ChkWhispers_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkWhispers.IsChecked ?? false;
            if (s.SubAudioEnabled == on) return;
            s.SubAudioEnabled = on;
            CoreSettings.Save();
            RefreshSilentHint();
        }

        private void SliderWhisperVol_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtWhisperVol.Text = $"{v}%";
            if (s.SubAudioVolume == v) return;
            s.SubAudioVolume = v;
            CoreSettings.Save();
        }

        private void ChkSolidMode_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkSolidMode.IsChecked ?? false;
            if (s.SubliminalSolidMode == on) return;
            s.SubliminalSolidMode = on;
            CoreSettings.Save();
            // No service bounce needed: each show reads the setting, so the next subliminal
            // uses the new renderer. An in-flight card finishes out on whichever spawned it.
        }

        // No service bounce: the text blocks are built per flash, so the next subliminal picks
        // the new face up on its own.
        private void CmbFont_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var name = (CmbFont.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrWhiteSpace(name) || s.SubliminalFont == name) return;
            s.SubliminalFont = name;
            CoreSettings.Save();
        }

        /// <summary>
        /// The pool editor, plus the bookkeeping that keeps a mod's top-up honest: phrases the
        /// user added by hand are remembered so a cross-mod prune never deletes them, and a
        /// DELETED DEFAULT is recorded so ModService's top-up does not put it straight back on the
        /// next launch (#892).
        /// </summary>
        private async void BtnManageMessages_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var s = CoreSettings.Current;

            var oldKeys = new HashSet<string>(s.SubliminalPool.Keys);
            // OrdinalIgnoreCase: the removed-set and ModService's top-up compare
            // case-insensitively, so detection must too or modded defaults slip through.
            var defaults = new HashSet<string>(
                CoreMods.GetDefaultSubliminalPool().Keys, StringComparer.OrdinalIgnoreCase);

            var dialog = new Dialogs.TextEditorDialog("Subliminal Messages", s.SubliminalPool);
            dialog.MarkSilentLines(Overlays.SubliminalWhisperShow.HasRecordedVoice);
            if (await dialog.ShowDialogSafe<bool?>(owner) != true || dialog.ResultData == null) return;

            // Remember hand-added phrases (and forget removed ones) so the cross-mod prune
            // never silently deletes a custom phrase that collides with another mod's default.
            var newKeys = new HashSet<string>(dialog.ResultData.Keys);
            foreach (var key in newKeys)
                if (!oldKeys.Contains(key)) s.UserAddedSubliminals.Add(key);
            foreach (var key in oldKeys)
                if (!newKeys.Contains(key)) s.UserAddedSubliminals.Remove(key);

            // Record deleted DEFAULTS, or ModService's top-up puts them straight back on the
            // next launch — the phrase the user deliberately deleted returns forever (#892).
            foreach (var key in oldKeys)
                if (!newKeys.Contains(key) && defaults.Contains(key))
                    s.RemovedDefaultSubliminals.Add(key);
            // A default they added back is no longer "removed".
            foreach (var key in newKeys)
                s.RemovedDefaultSubliminals.Remove(key);

            s.SubliminalPool = dialog.ResultData;
            CoreSettings.Save();
            RefreshSilentHint();
            Log.Information("Subliminal pool updated: {Count} items", dialog.ResultData.Count);
        }

        private async void BtnAdvanced_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            await new Dialogs.ColorEditorDialog().ShowDialogSafe(owner);
        }
    }
}
