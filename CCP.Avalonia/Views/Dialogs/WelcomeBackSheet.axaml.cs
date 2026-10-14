using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>WPF WelcomeBackSheetContent (WelcomeBackSheet.xaml.cs:16), minus the retired tour action.</summary>
    public sealed class WelcomeBackSheetContent
    {
        public string DisplayName { get; init; } = "";
        public int Level { get; init; }
        public string? BackupModName { get; init; }
        public DateTime? BackupTakenAt { get; init; }
        public WelcomeBackPlan Plan { get; init; } = WelcomeBackPlan.Nothing;
        public string? FlavourModName { get; init; }
        public string FlavourSizeText { get; init; } = "";
        public string VersionLabel { get; init; } = "";
        public string PatchNotes { get; init; } = "";
        public string? SeasonLine { get; init; }
    }

    /// <summary>
    /// PORTED from ConditioningControlPanel/Dialogs/WelcomeBackSheet.xaml.cs: the one sheet a returning
    /// user meets on a new PC. It decides nothing; rows come from the <see cref="WelcomeBackPlan"/> and its
    /// whole output is <see cref="RestoreChosen"/> / <see cref="BringFlavourChosen"/>.
    /// </summary>
    public partial class WelcomeBackSheet : Window
    {
        private readonly WelcomeBackSheetContent _content;

        public bool RestoreChosen { get; private set; }
        public bool BringFlavourChosen { get; private set; }

        /// <summary>Render constructor: every row on, so --render-all draws the full sheet.</summary>
        internal WelcomeBackSheet() : this(new WelcomeBackSheetContent
        {
            DisplayName = "Bambi", Level = 24, BackupModName = "Circe",
            BackupTakenAt = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc),
            Plan = new WelcomeBackPlan(true, true, true, "circe"), FlavourSizeText = "329 MB",
            VersionLabel = "7.1.5", PatchNotes = "• Sample patch notes.",
            SeasonLine = "The monthly board rolled over to 2026-10 while you were away.",
        }) { }

        public WelcomeBackSheet(WelcomeBackSheetContent content)
        {
            _content = content ?? new WelcomeBackSheetContent();
            InitializeComponent();
            TxtRestoreLabel.PointerReleased += (_, _) => ChkRestore.IsChecked = ChkRestore.IsChecked != true;
            TxtFlavourLabel.PointerReleased += (_, _) => ChkFlavour.IsChecked = ChkFlavour.IsChecked != true;
            TxtWhatsNewToggle.PointerReleased += (_, _) => ToggleNotes();
            TxtWhatsNewToggle.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; ToggleNotes(); } };
            BtnGo.Click += (_, _) => Commit();
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
            SheetRoot.PointerPressed += (_, e) =>   // WPF Sheet_MouseLeftButtonDown: no chrome to drag by
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
            };
            Render();
        }

        // WPF Render (:96), line for line.
        private void Render()
        {
            var plan = _content.Plan ?? WelcomeBackPlan.Nothing;

            TxtHeading.Text = string.IsNullOrWhiteSpace(_content.DisplayName)
                ? Str("wb_heading_anon", "Welcome back.")
                : StrF("wb_heading", "Welcome back, {0}.", _content.DisplayName.Trim());

            TxtSubline.Text = BuildSubline();
            TxtSubline.IsVisible = !string.IsNullOrWhiteSpace(TxtSubline.Text);

            RowRestore.IsVisible = plan.ShowRestoreRow;
            ChkRestore.IsChecked = plan.ShowRestoreRow;
            TxtRestoreLabel.Text = Str("wb_restore_label", "Restore my settings from the cloud backup");
            TxtRestoreHint.Text = Str("wb_restore_hint",
                "Your sliders, doors and preferences replace this PC's defaults. Your level, your XP and your unlocks are untouched.");

            RowFlavour.IsVisible = plan.ShowFlavourRow;
            ChkFlavour.IsChecked = plan.ShowFlavourRow;
            var flavourName = FirstNonEmpty(_content.FlavourModName, _content.BackupModName)
                              ?? Str("wb_flavour_generic", "your flavour");
            TxtFlavourLabel.Text = StrF("wb_flavour_label", "Bring {0} with me", flavourName);
            TxtFlavourHint.Text = string.IsNullOrWhiteSpace(_content.FlavourSizeText)
                ? Str("wb_flavour_hint", "It downloads in the background. You can use the app while it lands.")
                : StrF("wb_flavour_hint_size",
                       "{0}, downloading in the background. You can use the app while it lands.",
                       _content.FlavourSizeText);

            RowWhatsNew.IsVisible = !string.IsNullOrWhiteSpace(_content.PatchNotes);
            TxtWhatsNewToggle.Text = string.IsNullOrWhiteSpace(_content.VersionLabel)
                ? Str("wb_whats_new_toggle", "What changed since you were last here")
                : StrF("wb_whats_new_toggle_version", "What changed since you were last here (v{0})",
                       _content.VersionLabel);
            TxtNotes.Text = _content.PatchNotes;

            if (!string.IsNullOrWhiteSpace(_content.SeasonLine))
            {
                TxtSeasonLine.Text = _content.SeasonLine;
                TxtSeasonLine.IsVisible = true;
            }

            TxtGo.Text = Str("wb_go", "Let's go");
        }

        /// <summary>WPF BuildSubline (:155): "Level 24  ·  Circe  ·  backup from Sep 3, 2026".</summary>
        private string BuildSubline()
        {
            var parts = new List<string>(3);
            if (_content.Level > 0) parts.Add(StrF("wb_sub_level", "Level {0}", _content.Level));
            if (!string.IsNullOrWhiteSpace(_content.BackupModName)) parts.Add(_content.BackupModName!.Trim());
            if (_content.BackupTakenAt is DateTime taken)
                parts.Add(StrF("wb_sub_backup", "backup from {0}", taken.ToLocalTime().ToString("MMM d, yyyy")));
            return string.Join("  ·  ", parts);
        }

        private static string? FirstNonEmpty(params string?[] candidates)
        {
            foreach (var c in candidates)
                if (!string.IsNullOrWhiteSpace(c)) return c!.Trim();
            return null;
        }

        private void ToggleNotes() => NotesPanel.IsVisible = !NotesPanel.IsVisible;

        /// <summary>WPF Commit (:229): a hidden row can never report a choice.</summary>
        internal void Commit()
        {
            var plan = _content.Plan ?? WelcomeBackPlan.Nothing;
            RestoreChosen = plan.ShowRestoreRow && ChkRestore.IsChecked == true;
            BringFlavourChosen = plan.ShowFlavourRow && ChkFlavour.IsChecked == true;
            Close(true);
        }

        /// <summary>WPF Window_PreviewKeyDown (:243): Esc is "Let's go with nothing ticked".</summary>
        private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            ChkRestore.IsChecked = false;
            ChkFlavour.IsChecked = false;
            Commit();
        }

        /// <summary>WPF Str (:262): a missing wb_ key renders the English draft, never the raw key.</summary>
        private static string Str(string key, string english)
        {
            try
            {
                var value = Loc.Get(key);
                return string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal) ? english : value;
            }
            catch { return english; }
        }

        private static string StrF(string key, string english, params object[] args)
        {
            var template = Str(key, english);
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }
        }
    }
}
