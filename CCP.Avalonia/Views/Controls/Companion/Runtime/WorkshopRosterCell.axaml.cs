using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;
using Serilog;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z8 · ROSTER. See the XAML header.
    ///
    /// <para>Like every re-parented cell, this control does no work of its own: it forwards to the
    /// MainWindow handler the old tab forwarded to, so the roster's behaviour is byte-for-byte what
    /// it was before the move.</para>
    /// </summary>
    public partial class WorkshopRosterCell : UserControl
    {
        // The WPF cell forwards both clicks to MainWindow (Window.GetWindow(this) is MainWindow).
        // This head has no MainWindow API surface yet and the cell must not grow App. coupling, so
        // the two actions leave the cell as events and the host wires them - the same contract, one
        // indirection later. Unlike the sibling cells' bare EventHandler these carry the card index,
        // which is what MainWindow.CompanionCard_Click / BtnCompanionPersonality_Click parse out of
        // the sender's Tag; the Tag stays on the markup so the two files still diff.
        // The personality button is handled here: its whole target (CommunityPromptService) is in Core.
        public event EventHandler<int>? CompanionCardClicked;

        /// <summary>Test seam for the JSON picker: returns the chosen path, or null for Cancel.</summary>
        internal Func<string?, Task<string?>>? PickPromptFileForTests { get; set; }

        public WorkshopRosterCell()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new WorkshopRosterCellViewModel();

            for (int i = 0; i < 5; i++)
            {
                int index = i;

                // WPF's MouseLeftButtonDown is left-button-only; Avalonia's PointerPressed fires for
                // every button, so the guard below is what keeps a right-click from switching
                // companions. That guard is the only new mechanic in this port.
                this.FindControl<Border>($"CompanionCard{index}")!.PointerPressed += (s, e) =>
                {
                    if (e.GetCurrentPoint((Border)s!).Properties.IsLeftButtonPressed)
                        CompanionCardClicked?.Invoke(this, index);
                };

                // WPF needed PreviewMouseLeftButtonDown here (plus a visual-tree walk in
                // MainWindow.CompanionCard_Click that ignores clicks originating inside a
                // "Personality" button) because the card's bubbling MouseLeftButtonDown would
                // otherwise fire too. Neither is ported: Avalonia's Button marks PointerPressed
                // handled, and a routed handler does not see handled events by default, so the
                // card's handler never runs for a click on this button.
                this.FindControl<Button>($"BtnCompanion{index}Personality")!.Click +=
                    async (_, _) =>
                    {
                        try { await AssignPersonalityAsync(index); }
                        catch (Exception ex) { Log.Warning(ex, "Roster: personality picker failed"); }
                    };
            }
        }

        /// <summary>
        /// WPF MainWindow.CompanionTab.cs:367 BtnCompanionPersonality_Click. The locked branch is not
        /// ported: WPF IsCompanionUnlocked (CompanionService.cs:309) always returns true.
        /// </summary>
        internal async Task AssignPersonalityAsync(int companionIndex)
        {
            var companionId = (CompanionId)companionIndex;
            var def = CompanionDefinition.GetById(companionId);
            var owner = TopLevel.GetTopLevel(this) as Window;

            // Open on the user's own prompts folder when they made one, else on the shipped presets.
            var promptsFolder = Path.Combine(CorePaths.EffectiveAssets, "prompts");
            var shippedPrompts = Path.Combine(AppContext.BaseDirectory, "assets", "prompts");
            string? start = Directory.Exists(promptsFolder) && Directory.EnumerateFiles(promptsFolder, "*.json").Any()
                ? promptsFolder : Directory.Exists(shippedPrompts) ? shippedPrompts : null;

            string? path;
            if (PickPromptFileForTests != null) path = await PickPromptFileForTests(start);
            else
            {
                if (owner == null) return;
                var sp = owner.StorageProvider;
                path = (await sp.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Select AI Personality for {def.Name}",
                    FileTypeFilter = new[] { new FilePickerFileType("Prompt JSON files") { Patterns = new[] { "*.json" } } },
                    SuggestedStartLocation = start == null ? null : await sp.TryGetFolderFromPathAsync(start),
                })).FirstOrDefault()?.TryGetLocalPath();
            }
            if (path == null) return;

            string title, message;
            try
            {
                var prompt = CompanionCore.Prompts.ImportFromFile(path);
                if (prompt == null) return;   // WPF: an unreadable file closes silently
                CoreSettings.Current.SetCompanionPromptId(companionIndex, prompt.Id);
                CoreSettings.Save();

                // Assigning to the already-active companion never goes through SwitchCompanion
                // (old == new early-returns), so activate here as WPF does.
                if (CompanionCore.ActiveCompanion == companionId)
                    CompanionCore.ApplyCompanionPrompt(companionId);

                UpdatePromptLabels();
                Log.Information("Assigned prompt '{Prompt}' to companion {Companion}", prompt.Name, def.Name);
                title = Loc.Get("title_personality_assigned");
                message = Loc.GetF("msg_personality_assigned", def.Name, prompt.Name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to assign prompt to companion");
                title = Loc.Get("title_error");
                message = Loc.GetF("msg_failed_to_import_prompt", ex.Message);
            }
            LastDialogForTests = message;
            if (owner is { IsVisible: true }) await MessageDialog.ShowAsync(owner, title, message);
        }

        internal string? LastDialogForTests { get; private set; }

        /// <summary>WPF MainWindow.CompanionTab.cs:464 UpdateCompanionPromptLabels.</summary>
        private void UpdatePromptLabels()
        {
            var mods = CoreMods.Service;
            for (int i = 0; i < 5; i++)
            {
                var promptName = CompanionCore.GetAssignedPromptName((CompanionId)i);
                var displayName = mods?.MakeModAware(promptName ?? "") ?? promptName ?? "";
                var label = this.FindControl<TextBlock>($"TxtCompanion{i}Prompt")!;
                label.Text = displayName;
                ToolTip.SetTip(label, string.IsNullOrEmpty(displayName) ? null : Loc.GetF("tooltip_ai_personality", displayName));
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_watchingCore)
            {
                _watchingCore = true;
                CompanionCore.Switched += OnCompanionSwitched;
                CompanionCore.LevelUp += OnCompanionLevelUp;
                _host = TopLevel.GetTopLevel(this) as Window;
                if (_host != null) _host.Closed += OnHostClosed;
            }
            Refresh();
        }

        private bool _watchingCore;
        private Window? _host;

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            StopCoreUpdates();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnHostClosed(object? sender, EventArgs e) => StopCoreUpdates();

        private void StopCoreUpdates()
        {
            if (!_watchingCore) return;
            _watchingCore = false;
            CompanionCore.Switched -= OnCompanionSwitched;
            CompanionCore.LevelUp -= OnCompanionLevelUp;
            if (_host != null) _host.Closed -= OnHostClosed;
            _host = null;
        }

        private void OnCompanionSwitched(CompanionId id) => RefreshFromCore();
        private void OnCompanionLevelUp(CompanionId id, int level) => RefreshFromCore();

        private void RefreshFromCore()
        {
            // Core events can originate on an engine thread. A queued repaint must also
            // re-check the subscription: a closed/detached cell must never be repainted.
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshFromCore); return; }
            if (_watchingCore) Refresh();
        }

        /// <summary>
        /// WPF MainWindow.CompanionTab.cs:85 UpdateCompanionCardsUI, the five cards' half: hidden
        /// when the mod does not support the companion, mod-aware name, "MAX"/"Lv.N", flavour +
        /// XP-mechanic tooltip, accent ring on the active one, lock visuals off. Every input is in
        /// Core (CoreSettings progress/active id, CompanionDefinition, CoreMods.Service), read the
        /// way CompanionService.GetProgress/ActiveCompanion read them. Called on attach and when
        /// the room's tab is shown again (CompanionRoomView.ResumeClocks).
        /// </summary>
        public void Refresh()
        {
            var s = CoreSettings.Current;
            if (s == null) return;
            var mods = CoreMods.Service;
            var activeId = (CompanionId)s.ActiveCompanionId;
            var colors = new[] { mods?.GetAccentColorHex() ?? "#FF69B4", "#9370DB", "#50C878", "#FF6B6B", "#F5DEB3" };

            for (int i = 0; i < 5; i++)
            {
                var id = (CompanionId)i;
                var card = this.FindControl<Border>($"CompanionCard{i}")!;
                if (mods?.IsCompanionSupported(id) == false)
                {
                    card.IsVisible = false;
                    continue;
                }
                card.IsVisible = true;

                var def = CompanionDefinition.GetById(id);
                var name = def.GetDisplayName(s.SlutModeEnabled);
                this.FindControl<TextBlock>($"TxtCompanion{i}Name")!.Text = mods?.MakeModAware(name) ?? name;

                // Read-only, unlike CompanionService.GetProgress, which also seeds a missing entry.
                var progress = s.CompanionProgressData.TryGetValue(i, out var saved) ? saved : CompanionProgress.CreateNew(id);
                this.FindControl<TextBlock>($"TxtCompanion{i}Level")!.Text = progress.IsMaxLevel ? "MAX" : $"Lv.{progress.Level}";

                var mechanic = mods?.MakeModAware(def.XPMechanicDescription) ?? def.XPMechanicDescription;
                var flavor = mods?.MakeModAware(def.Description) ?? def.Description;
                ToolTip.SetTip(card, string.IsNullOrWhiteSpace(mechanic) ? flavor : $"{flavor}\n{mechanic}");

                card.BorderBrush = id == activeId && Color.TryParse(colors[i], out var c)
                    ? new SolidColorBrush(c) : Brushes.Transparent;

                this.FindControl<TextBlock>($"TxtCompanion{i}Lock")!.IsVisible = false;
                card.Opacity = 1.0;
            }
            UpdatePromptLabels();   // WPF UpdateCompanionCardsUI :150
        }
    }

    /// <summary>Strings from CCP.Core's Loc. See the porting notes in the repo-root CLAUDE.md
    /// for why {loc:Str} becomes a binding.</summary>
    public sealed class WorkshopRosterCellViewModel
    {
        public string LocBeta => Loc.Get("label_beta");
        public string LocTooltipAssignAiPersonality => Loc.Get("tooltip_assign_ai_personality");
    }
}
