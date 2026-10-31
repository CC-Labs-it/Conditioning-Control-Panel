using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Animation;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// WPF CompanionPickerCard: avatar + personality side by side with a live preview. It owns no
    /// switching logic: the personality goes through <see cref="MainShellWindow.ActivatePersonalityPresetAsync"/>
    /// (the explicit-content gate), the on switch through <see cref="MainShellWindow.SetAvatarEnabled"/>,
    /// the fit through <see cref="TubeFitDialog"/>, the perk through <see cref="PerkPicker"/>.
    /// </summary>
    public partial class CompanionPickerCard : UserControl
    {
        private bool _syncing;
        private bool _previewing;
        private bool _hooked;

        public CompanionPickerCard()
        {
            InitializeComponent();
            CmbPersonality.SelectionChanged += CmbPersonality_SelectionChanged;
            CmbAvatar.SelectionChanged += (_, _) =>
            {
                if (_syncing || CmbAvatar.SelectedItem is not ComboBoxItem { Tag: int set }) return;
                if ((Shell?.Tube ?? AvatarTubeWindow.Live)?.SelectAvatarSet(set) == true) Refresh();
            };
            BtnMorePersonality.Click += BtnMorePersonality_Click;
            BtnTurnOn.Click += (_, _) => { Shell?.SetAvatarEnabled(true); Refresh(); };
            BtnTubeFit.Click += (_, _) => _ = new TubeFitDialog().ShowDialogSafe(TopLevel.GetTopLevel(this) as Window);
            BtnPerkChange.Click += Perk_Click;
            Refresh();
            AttachedToVisualTree += (_, _) =>
            {
                if (!_hooked) PersonalityService.Shared.PersonalityChanged += OnPersonalityChanged;
                _hooked = true;
                if (!_previewing) Refresh();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                if (_hooked) PersonalityService.Shared.PersonalityChanged -= OnPersonalityChanged;
                _hooked = false;
            };
        }

        /// <summary>The shell that owns the gates: this card's window, or the window that owns it
        /// (the Customise dialog is owned by the shell), else the app's main window.</summary>
        internal MainShellWindow? Shell
        {
            get
            {
                for (var w = TopLevel.GetTopLevel(this) as Window; w != null; w = w.Owner as Window)
                    if (w is MainShellWindow shell) return shell;
                return MainShellWindow.Current;
            }
        }

        /// <summary>Re-reads the live companion. Cheap; call it after a mod switch.</summary>
        public void Refresh()
        {
            _syncing = true;
            try
            {
                _previewing = false;
                LivePanel.IsVisible = true;
                PreviewPanel.IsVisible = false;
                TxtPreviewGlyph.Bind(ConditioningControlPanel.Avalonia.Controls.IconGlyph.ForegroundProperty, TxtPreviewGlyph.GetResourceObservable("TextMutedBrush"));
                TxtLiveName.Text = LiveName();
                ShowPerk();
                FillAvatars();
                FillPersonalities();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[CompanionPicker] refresh failed");
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>The companion's own name (the active mod's identity), not the look's name.</summary>
        private static string LiveName()
        {
            var name = CoreMods.ActiveModPackage?.Manifest?.Identity?.CompanionName;
            if (string.IsNullOrWhiteSpace(name)) name = Loc.Get("modmgr_companion_fallback");
            return CoreMods.MakeModAware(name!);
        }

        /// <summary>WPF ShowPerk, over CompanionService.ActivePerk's own resolution.</summary>
        private void ShowPerk()
        {
            var type = CompanionPerks.Active(CoreSettings.Current);
            var perk = CompanionPerks.For(type);
            BtnPerkChange.IsVisible = CompanionExperience.IsV2Enabled && !_previewing;
            TxtPerkGlyph.Text = perk.Glyph;
            TxtPerk.Text = (CompanionExperience.IsV2Enabled ? Loc.Get(CompanionPerks.NameKey(type)) + ": " : string.Empty) + Loc.Get(perk.LocKey);
            // A cost keeps its salmon; everything else is body copy (Type.Body ink).
            if (perk.Negative) TxtPerk.Foreground = Brushes.Salmon;
            else TxtPerk.ClearValue(TextBlock.ForegroundProperty);
            if (perk.Negative) TxtPerkGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            else TxtPerkGlyph.Bind(TextBlock.ForegroundProperty, TxtPerkGlyph.GetResourceObservable("SectionInkBrush"));
        }

        private async void Perk_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            await PerkPicker.Show(owner);
            Refresh();
        }

        // ------------------------------------------------------------------ preview

        /// <summary>
        /// Read-only face for a mod the user is looking at but not using (WPF ShowPreview).
        /// Reads the manifest and the mod's personality list off disk; activates nothing.
        /// </summary>
        public void ShowPreview(ModPackage mod, Color accent)
        {
            LivePanel.IsVisible = false;
            PreviewPanel.IsVisible = true;
            _previewing = true;
            ShowPerk();
            try
            {
                var personalities = ModCompanionContent.GetPersonalities(
                    mod.Id, mod.InstalledPath, mod.Manifest.Personalities, out _);
                var singleEmote = (mod.Id == BuiltInMods.BambiSleepId || mod.Id == BuiltInMods.SissyHypnoId)
                                  && (mod.Manifest.SupportedAvatarSets?.Count ?? 0) == 0
                                  && (mod.Manifest.CustomAvatarSets?.Count ?? 0) == 0;
                var info = CompanionPreview.Build(mod.Manifest, personalities, singleEmote,
                    neutral: mod.Id == BuiltInMods.CCPDefaultId);

                TxtPreviewName.Text = info.Name;
                TxtPreviewCounts.Text = Loc.GetF("modmgr_preview_counts",
                    Loc.GetF(info.Looks == 1 ? "modmgr_looks_one" : "modmgr_looks_n", info.Looks),
                    Loc.GetF(info.Personalities == 1 ? "modmgr_personalities_one" : "modmgr_personalities_n", info.Personalities));
                PreviewSampleBorder.IsVisible = !string.IsNullOrWhiteSpace(info.SampleLine);
                TxtPreviewSample.Text = "\u201C" + info.SampleLine + "\u201D";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[CompanionPicker] preview failed for {Mod}", mod.Id);
            }

            TxtPreviewGlyph.Foreground = new SolidColorBrush(accent);
            // ponytail: WPF previews an animated companion's idle emote frame first
            // (AvatarTubeWindow.EmoteIdleClipUri); this head has no emote clips yet, so it goes
            // straight to WPF's fallback, pose 1 from the mod's own folder.
            ShowPortrait(PortraitInFolder(mod.InstalledPath));
        }

        /// <summary>Pose 1 from a mod's own folder, never through the resolver (which answers
        /// for the ACTIVE mod). Null when the mod ships no pose art on disk.</summary>
        private static global::Avalonia.Media.Imaging.Bitmap? PortraitInFolder(string? installedPath)
        {
            if (string.IsNullOrEmpty(installedPath)) return null;
            foreach (var name in new[] { "avatar3_pose1.png", "avatar_pose1.png" })
            {
                try
                {
                    var full = System.IO.Path.Combine(installedPath, "resources", name);
                    if (!System.IO.File.Exists(full)) continue;
                    using var stream = System.IO.File.OpenRead(full);
                    return global::Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 240);
                }
                catch { }
            }
            return null;
        }

        // ------------------------------------------------------------------ look

        private void FillAvatars()
        {
            CmbAvatar.Items.Clear();
            var tube = Shell?.Tube ?? AvatarTubeWindow.Live;
            BtnTurnOn.IsVisible = tube == null;
            BtnTubeFit.IsVisible = tube != null;
            var set = Math.Max(1, tube?.CurrentAvatarSet ?? CoreSettings.Current.SelectedAvatarSet);
            if (tube == null)
            {
                CmbAvatar.IsEnabled = false;
                ShowHint(Loc.Get("modmgr_avatar_off_hint"));
            }
            else
            {
                var sets = tube.PickableAvatarSets();
                foreach (var pick in sets)
                {
                    var item = new ComboBoxItem { Content = AvatarTubeWindow.AvatarSetTitle(pick), Tag = pick };
                    CmbAvatar.Items.Add(item);
                    if (pick == set) CmbAvatar.SelectedItem = item;
                }
                CmbAvatar.IsEnabled = sets.Length > 1;
                if (sets.Length <= 1) ShowHint(Loc.Get("modmgr_avatar_single_hint"));
                else TxtAvatarHint.IsVisible = false;
            }
            ShowPortrait(ModArt.TryLoad(set == 1 ? "avatar_pose1.png" : $"avatar{set}_pose1.png", 240));
        }

        private void ShowHint(string text)
        {
            TxtAvatarHint.Text = text;
            TxtAvatarHint.IsVisible = true;
        }

        private void ShowPortrait(IImage? source)
        {
            ImgPreview.Transitions = null; // cancel a previous portrait's fade before replacing it
            ImgPreview.Source = source;
            TxtPreviewGlyph.IsVisible = source == null;
            if (source == null || !AmbientFxCanvas.Env.AllowTransitions)
            {
                ImgPreview.Opacity = 1;
                return;
            }
            // WPF ShowPreview: a linear 0.2 -> 1 entrance, 160 ms, including reduced motion.
            ImgPreview.Opacity = 0.2;
            ImgPreview.Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }
            };
            ImgPreview.Opacity = 1;
        }

        // ------------------------------------------------------------------ personality

        private void FillPersonalities()
        {
            CmbPersonality.Items.Clear();
            var svc = PersonalityService.Shared;
            var active = svc.GetActivePreset();
            foreach (var preset in svc.GetAllPresets())
            {
                var item = new ComboBoxItem { Content = DisplayName(preset), Tag = preset.Id };
                CmbPersonality.Items.Add(item);
                if (preset.Id == active?.Id) CmbPersonality.SelectedItem = item;
            }
            CmbPersonality.IsEnabled = CmbPersonality.Items.Count > 1;
            ShowSamples(active);
        }

        private static string DisplayName(PersonalityPreset p) =>
            CoreMods.Service?.GetPersonalityDisplayName(p.Name) ?? p.Name;

        private async void CmbPersonality_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_syncing || CmbPersonality.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            await ActivateAsync(id);
        }

        /// <summary>The gate lives on the shell; a refusal or a failed switch puts the combo back
        /// on whatever is really active.</summary>
        internal async Task ActivateAsync(string id)
        {
            var ok = Shell is { } shell && await shell.ActivatePersonalityPresetAsync(id);
            if (!ok) Refresh();
            else ShowSamples(PersonalityService.Shared.GetPresetById(id));
        }

        private void OnPersonalityChanged(object? sender, PersonalityPreset preset)
            // Another door (chip row, tube menu) switched her. Follow it.
            => Dispatcher.UIThread.Post(() => { if (!_previewing) Refresh(); });

        private void ShowSamples(PersonalityPreset? preset)
        {
            SamplePanel.Children.Clear();
            if (preset == null) return;

            var lines = PersonalitySamples.For(preset);
            if (lines.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(preset.Description))
                    SamplePanel.Children.Add(Line(CoreMods.MakeModAware(preset.Description), italic: false));
                return;
            }

            foreach (var line in lines)
                SamplePanel.Children.Add(Line("\u201C" + CoreMods.MakeModAware(line) + "\u201D", italic: true));
        }

        // A sample line is body copy (Type.Body); the quote marks and the italic say it is the personality talking.
        private TextBlock Line(string text, bool italic)
        {
            var line = new TextBlock
            {
                Text = text,
                FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
                Margin = new global::Avalonia.Thickness(0, 0, 0, 4),
            };
            if (this.TryFindResource("Type.Body", out var theme) && theme is global::Avalonia.Styling.ControlTheme t)
                line.Theme = t;
            return line;
        }

        // ------------------------------------------------------------------ more options

        private Func<Window, Task>? _morePersonalityOptions;

        /// <summary>Opens the deeper personality editor; null hides the link (WPF MorePersonalityOptions).</summary>
        public Func<Window, Task>? MorePersonalityOptions
        {
            get => _morePersonalityOptions;
            set
            {
                _morePersonalityOptions = value;
                BtnMorePersonality.IsVisible = value != null;
            }
        }

        private async void BtnMorePersonality_Click(object? sender, RoutedEventArgs e)
        {
            if (_morePersonalityOptions == null || TopLevel.GetTopLevel(this) is not Window owner) return;
            await _morePersonalityOptions(owner);
            if (!_previewing) Refresh();
        }
    }
}
