// WPF 7.1.5 Windows/Launcher/LauncherMediaDialog.xaml(.cs): "Media and sound", opened by the launcher's
// Media pill. Built in code with the WPF layout numbers (xaml :6 560x790, rows 44/*/Auto :77, card
// 22,20 padding, chips 14,6 / radius 15 :39). Applies on change; Done only closes. The rules are Core's
// LauncherMediaSettings (moved from the WPF head); the consent ask is the Assets page's, word for word.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Launcher;
using Serilog;
using LauncherSfx = ConditioningControlPanel.Avalonia.Views.Windows.LauncherWindow.LauncherSfx;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    internal sealed class LauncherMediaDialog : Window
    {

        private readonly List<ToggleButton> _sourceChips = new();
        private readonly List<ToggleButton> _nicheChips = new();
        private readonly TextBlock _sourceHint, _ratioLabel, _masterLabel;
        private readonly Grid _ratioRow;
        private readonly Slider _ratioSlider, _masterSlider;
        private readonly ComboBox _presetCombo = new()
            { FontSize = 13, MinHeight = 32, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };

        /// <summary>WPF _syncing: set while settings are written INTO the controls.</summary>
        private bool _syncing = true;
        private bool _asking;

        internal Func<string, string, Task<bool>>? ConsentOverride { get; set; }   // tests: answers the consent ask
        internal Task LastChange { get; private set; } = Task.CompletedTask;       // tests: the source change in flight
        /// <summary>WPF BtnAssetBrowser_Click: the launcher brings the panel up on Assets.</summary>
        internal Action? OpenAssetBrowser { get; set; }
        internal MainShellWindow? Panel { get; set; }   // the launcher's panel: preset changes go through it

        internal IReadOnlyList<ToggleButton> SourceChips => _sourceChips;
        internal IReadOnlyList<ToggleButton> NicheChips => _nicheChips;
        internal Slider MasterSlider => _masterSlider;
        internal Slider RatioSlider => _ratioSlider;
        internal bool RatioRowVisible => _ratioRow.IsVisible;

        public LauncherMediaDialog()
        {
            Title = Loc.Get("launcher_media_title");
            (Width, Height) = (560, 790);
            CanResize = false;
            ShowInTaskbar = false;
            WindowDecorations = WindowDecorations.None;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Res("DarkerBgBrush");
            var (copy, muted) = (Res("TextSecondaryBrush"), Res("TextMutedBrush"));
            // WPF MediaChip (xaml :39): hover = the elevated surface, on = the cyan wash and rim. Fluent
            // paints its template part on hover and check, so the states are styles on that part.
            Styles.Add(ChipState(":pointerover", Res("ElevatedSurfaceBrush"), null));
            Styles.Add(ChipState(":checked", ChipOn, ChipOnRim));
            // Done and the link keep their paint under the pointer (Fluent repaints the part on hover
            // and press); WPF fades Done to 0.92 and underlines the link.
            foreach (var state in new[] { ":pointerover", ":pressed" })
            {
                Styles.Add(ButtonPart("mediaDone", state, Res("AccentGradientBrush"), Brushes.White));
                Styles.Add(ButtonPart("mediaLink", state, Brushes.Transparent, copy));
            }
            Styles.Add(new Style(x => x.OfType<Button>().Class("mediaDone").Class(":pointerover")) { Setters = { new Setter(OpacityProperty, 0.92) } });
            Styles.Add(new Style(x => x.OfType<Button>().Class("mediaLink").Class(":pointerover").Child().OfType<TextBlock>())
                { Setters = { new Setter(TextBlock.TextDecorationsProperty, TextDecorations.Underline) } });

            TextBlock Copy(string key, double size = 14, double top = 0) => new()
                { Text = Loc.Get(key), Foreground = copy, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
            TextBlock Eyebrow(string key, double top) => new()
                { Text = Loc.Get(key), Foreground = muted, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, top, 0, 10) };
            Grid Row(Control left, Control right, double top)
            {
                Grid.SetColumn(right, 1);
                return new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, top, 0, 0), Children = { left, right } };
            }

            // ---- title bar (xaml :83): drag handle and the close button
            var close = new Button { Content = new IconGlyph { Kind = IconKind.Dismiss, Size = 14 }, Width = 46, Height = 36, HorizontalAlignment = HorizontalAlignment.Right };
            if (this.TryFindResource("WindowCloseButton", out var ct) && ct is ControlTheme theme) close.Theme = theme;
            AutomationProperties.SetName(close, Loc.Get("launcher_media_done"));
            close.Click += (_, _) => { LauncherSfx.Click(); Close(); };
            var heading = new TextBlock
            {
                Text = Loc.Get("launcher_media_title"), Margin = new Thickness(20, 0, 0, 0), FontFamily = new FontFamily("Fredoka, Segoe UI"),
                FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Res("TextLightBrush"), VerticalAlignment = VerticalAlignment.Center,
            };
            var titleBar = new Border { Background = Brushes.Transparent, Child = new Grid { Children = { heading, close } } };
            titleBar.PointerPressed += (_, e) =>
            {
                if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                try { BeginMoveDrag(e); } catch (Exception ex) { Log.Debug("[Launcher] media dialog drag: {E}", ex.Message); }
            };

            // ---- source chips, the mixed share, niche chips (xaml :101-121)
            var sourcePanel = new WrapPanel();
            foreach (var key in LauncherMediaSettings.Sources)
                ToolTip.SetTip(Chip(Loc.Get("launcher_media_src_" + key), key, _sourceChips, sourcePanel, SourceChip_Changed),
                    Loc.Get("launcher_media_src_" + key + "_hint"));
            _sourceHint = Copy("launcher_media_src_local_hint", 13, 2);
            _ratioSlider = new Slider { Minimum = 5, Maximum = 95, TickFrequency = 5, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            _ratioSlider.ValueChanged += (_, _) => RatioSlider_Changed();
            _ratioLabel = new TextBlock { Margin = new Thickness(14, 0, 0, 0), MinWidth = 90, Foreground = copy, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            _ratioRow = Row(_ratioSlider, _ratioLabel, 14);
            _ratioRow.IsVisible = false;

            var nichePanel = new WrapPanel();
            foreach (var niche in FypOnlineCoordinator.Catalog) Chip(niche.Label, niche.Id, _nicheChips, nichePanel, NicheChip_Changed);

            // ---- library preset (xaml :125-168) and the master volume (:171-184)
            _presetCombo.DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(AssetPreset.DisplayText));
            _presetCombo.SelectionChanged += (_, _) => PresetCombo_Changed();
            var browser = new Button
            {
                Classes = { "mediaLink" }, Content = new TextBlock { Text = Loc.Get("launcher_media_open_browser") }, Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(4, 2), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Foreground = copy, FontSize = 13, Cursor = new Cursor(StandardCursorType.Hand),
            };
            browser.Click += (_, _) => { LauncherSfx.Click(); Close(); OpenAssetBrowser?.Invoke(); };

            _masterSlider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            _masterSlider.ValueChanged += (_, _) => MasterSlider_Changed();
            // WPF MasterSlider_Released: one soft cue at the new level, so it can be set by ear.
            _masterSlider.AddHandler(PointerReleasedEvent, (_, _) => LauncherSfx.Hover(), RoutingStrategies.Tunnel);
            _masterLabel = new TextBlock { Margin = new Thickness(14, 0, 0, 0), MinWidth = 48, Foreground = copy, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };

            var card = new Border
            {
                Background = Res("SurfaceBgBrush"), CornerRadius = new CornerRadius(14),
                Margin = new Thickness(20, 4, 20, 0), Padding = new Thickness(22, 20),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            Copy("launcher_media_blurb"),
                            Eyebrow("launcher_media_source", 22), sourcePanel, _sourceHint, _ratioRow,
                            Eyebrow("launcher_media_niches", 22), nichePanel, Copy("launcher_media_niches_hint", 13, 2),
                            Eyebrow("launcher_media_library", 22), Copy("launcher_media_preset", 13),
                            Row(_presetCombo, browser, 6), Copy("launcher_media_preset_hint", 13, 2),
                            Eyebrow("launcher_media_sound", 22), Copy("launcher_media_master", 13),
                            Row(_masterSlider, _masterLabel, 6), Copy("launcher_media_master_hint", 13, 2),
                        },
                    },
                },
            };
            Grid.SetRow(card, 1);

            // ---- footnote and Done (xaml :191-199)
            var footnote = Copy("launcher_media_footnote", 12);
            (footnote.Foreground, footnote.Margin, footnote.VerticalAlignment) = (muted, new Thickness(4, 0, 120, 0), VerticalAlignment.Center);
            var done = new Button
            {
                Classes = { "mediaDone" }, Content = new TextBlock { Text = Loc.Get("launcher_media_done") },
                HorizontalAlignment = HorizontalAlignment.Right, Width = 104, Height = 38, FontSize = 14, FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White, Background = Res("AccentGradientBrush"), BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10), Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            };
            done.Click += (_, _) => { LauncherSfx.Click(); Close(); };
            var foot = new Grid { Margin = new Thickness(20, 14, 20, 18), Children = { footnote, done } };
            Grid.SetRow(foot, 2);

            Content = new Border
            {
                BorderBrush = Res("GlassBorderBrush"), BorderThickness = new Thickness(1),
                Child = new Grid { RowDefinitions = new RowDefinitions("44,*,Auto"), Children = { titleBar, card, foot } },
            };

            BuildPresets();
            Refresh();
        }

        private static IBrush Res(string key) =>
            Application.Current != null && Application.Current.TryFindResource(key, Application.Current.ActualThemeVariant, out var v) && v is IBrush b
                ? b : Brushes.Transparent;

        private static readonly IBrush ChipOff = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        private static readonly IBrush ChipOn = new SolidColorBrush(Color.FromArgb(0x33, 0x5E, 0xC8, 0xF2));
        private static readonly IBrush ChipOnRim = new SolidColorBrush(Color.FromRgb(0x5E, 0xC8, 0xF2));

        /// <summary>WPF MediaChip (xaml :39): 13 px semibold, radius 15, 14,6 padding, off = a faint wash
        /// with the glass rim; hover and on are the ChipState styles.</summary>
        private static ToggleButton Chip(string label, string tag, List<ToggleButton> list, Panel panel, Action<ToggleButton> changed)
        {
            var chip = new ToggleButton
            {
                Content = new TextBlock { Text = label }, Tag = tag, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 6),
                CornerRadius = new CornerRadius(15), FontSize = 13, FontWeight = FontWeight.SemiBold, Cursor = new Cursor(StandardCursorType.Hand),
                Foreground = Res("TextLightBrush"), Background = ChipOff, BorderBrush = Res("GlassBorderBrush"), BorderThickness = new Thickness(1),
            };
            chip.IsCheckedChanged += (_, _) => changed(chip);
            list.Add(chip);
            panel.Children.Add(chip);
            return chip;
        }

        private static Style ButtonPart(string cls, string state, IBrush background, IBrush foreground) => new(x =>
            x.OfType<Button>().Class(cls).Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, background),
                new Setter(ContentPresenter.ForegroundProperty, foreground),
            },
        };

        private static Style ChipState(string state, IBrush background, IBrush? rim) => new(x =>
            x.OfType<ToggleButton>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, background),
                new Setter(ContentPresenter.BorderBrushProperty, rim ?? Res("GlassBorderBrush")),
                new Setter(ContentPresenter.ForegroundProperty, Res("TextLightBrush")),
            },
        };

        /// <summary>WPF BuildPresets: the saved asset presets, "All Assets" first.</summary>
        private void BuildPresets()
        {
            var s = CoreSettings.Current;
            if (!s.AssetPresets.Any(p => p.IsDefault)) s.AssetPresets.Insert(0, AssetPreset.CreateDefault());
            _presetCombo.ItemsSource = s.AssetPresets.ToList();
        }

        private void PresetCombo_Changed()
        {
            if (_syncing) return;
            try
            {
                var s = CoreSettings.Current;
                if (_presetCombo.SelectedItem is not AssetPreset preset) return;
                if (string.Equals(preset.Id, s.CurrentAssetPresetId, StringComparison.Ordinal)) return;
                LauncherSfx.Click();
                // WPF mw.ApplyAssetPresetFromLauncher: the panel writes AND repaints/reloads the pools.
                if (Panel?.ApplyAssetPresetFromLauncher(preset.Id) != true && AssetPresetService.Apply(s, preset.Id) != null) CoreSettings.Save();
                Refresh();   // the preset may have switched the source and niches too (ccp-bugs #1142)
                Log.Information("[Launcher] asset preset -> {Name}", preset.Name);
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] asset preset change failed"); }
        }

        /// <summary>WPF Refresh: pushes live settings into every control.</summary>
        internal void Refresh()
        {
            var s = CoreSettings.Current;
            _syncing = true;
            try
            {
                var source = s.MediaSource;
                foreach (var chip in _sourceChips)
                    chip.IsChecked = string.Equals(chip.Tag as string, source, StringComparison.Ordinal);
                _sourceHint.Text = Loc.Get("launcher_media_src_" + source + "_hint");
                _ratioSlider.Value = s.RemoteMediaRatio;
                _ratioLabel.Text = Loc.GetF("launcher_media_mixed_share", s.RemoteMediaRatio);
                _ratioRow.IsVisible = source == LauncherMediaSettings.SourceMixed;

                var selected = new HashSet<string>(s.FypOnlineNiches ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var chip in _nicheChips)
                    chip.IsChecked = chip.Tag is string id && selected.Contains(id);
                _masterSlider.Value = s.MasterVolume;
                _masterLabel.Text = $"{s.MasterVolume}%";
                var active = AssetPresetService.Active(s)?.Id;
                _presetCombo.SelectedItem = (_presetCombo.ItemsSource as IEnumerable<AssetPreset>)?.FirstOrDefault(p => p.Id == active);
            }
            finally { _syncing = false; }
        }

        private void SourceChip_Changed(ToggleButton chip)
        {
            if (_syncing || _asking) return;
            LastChange = ChangeSourceAsync(chip);
        }

        private async Task ChangeSourceAsync(ToggleButton chip)
        {
            try
            {
                if (chip.Tag is not string key) return;
                var s = CoreSettings.Current;
                // Un-clicking the live chip would leave the app with no source at all.
                if (chip.IsChecked != true)
                {
                    if (string.Equals(key, s.MediaSource, StringComparison.Ordinal)) Refresh();
                    return;
                }
                if (string.Equals(key, s.MediaSource, StringComparison.Ordinal)) return;
                LauncherSfx.Click();

                // Leaving "local" fetches third-party content: asked once, never of someone who
                // already said yes to the For You feed (HasRemoteMediaConsent).
                if (key != LauncherMediaSettings.SourceLocal && !s.HasRemoteMediaConsent)
                {
                    Refresh();   // the chips stay on the live source while the question is open
                    if (!await AskRemoteMediaConsentAsync(s)) return;
                }
                Commit(s, key, resetChannels: true);
                Refresh();
                Log.Information("[Launcher] game media source -> {Source}", s.MediaSource);
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] game media source change failed"); }
        }

        private void NicheChip_Changed(ToggleButton chip)
        {
            if (_syncing) return;
            try
            {
                if (SelectedNiches().Count == 0)
                {
                    // The last niche stays on: an empty list would only fall back to the first
                    // catalogue niche anyway, and showing that as "nothing" would be a lie.
                    _syncing = true;
                    try { chip.IsChecked = true; } finally { _syncing = false; }
                    LauncherSfx.Denied();
                    return;
                }
                LauncherSfx.Click();
                var s = CoreSettings.Current;
                Commit(s, s.MediaSource, resetChannels: true);
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] game media niche toggle failed"); }
        }

        private void RatioSlider_Changed()
        {
            if (_syncing) return;
            try
            {
                var s = CoreSettings.Current;
                // Per drag tick; no channel reset, the ratio is read at pick time. Save is debounced.
                Commit(s, s.MediaSource, resetChannels: false);
                _ratioLabel.Text = Loc.GetF("launcher_media_mixed_share", s.RemoteMediaRatio);
            }
            catch (Exception ex) { Log.Debug("[Launcher] game media ratio change failed: {E}", ex.Message); }
        }

        private void MasterSlider_Changed()
        {
            if (_syncing) return;
            try
            {
                var s = CoreSettings.Current;
                int level = LauncherMediaSettings.ApplyMasterVolume(s, (int)Math.Round(_masterSlider.Value));
                _masterLabel.Text = $"{level}%";
                // WPF mw.SetMasterVolume: what the Settings page's master slider runs (AudioSettingsBinder).
                LayeredAudio.Instance?.SetMasterVolumeLive();
                MandatoryVideoOverlay.Instance.UpdateVolume();
                CoreSettings.Save();   // debounced: a whole drag is one write
            }
            catch (Exception ex) { Log.Debug("[Launcher] master volume change failed: {E}", ex.Message); }
        }

        private List<string> SelectedNiches() =>
            _nicheChips.Where(c => c.IsChecked == true && c.Tag is string).Select(c => (string)c.Tag!).ToList();

        /// <summary>WPF Commit: rotation state was built for the old choice, so it is dropped. WPF's
        /// InvalidateAssetPoolsAfterSelectionChange has no pool here (AssetsTabView.MediaPicker.cs:34).</summary>
        private void Commit(AppSettings s, string source, bool resetChannels)
        {
            LauncherMediaSettings.Apply(s, source, (int)Math.Round(_ratioSlider.Value), SelectedNiches());
            if (resetChannels) FypOnlineCoordinator.ResetAllChannels();
            CoreSettings.Save();
        }

        /// <summary>WPF AskRemoteMediaConsent: the Assets page's one-time ask, word for word.</summary>
        private async Task<bool> AskRemoteMediaConsentAsync(AppSettings s)
        {
            _asking = true;
            try
            {
                var (title, message) = Tabs.AssetsTabView.RemoteMediaConsentText();
                bool yes = ConsentOverride != null
                    ? await ConsentOverride(title, message)
                    : await Dialogs.MessageDialog.ConfirmAsync(this, title, message, okText: Loc.Get("label_yes"));
                if (!yes) return false;
                s.RemoteMediaConsented = true;
                CoreSettings.Save();
                return true;
            }
            finally { _asking = false; }
        }

    }
}
