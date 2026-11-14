using System;
using System.IO;
using ConditioningControlPanel.Services.Companion;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
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
        public event EventHandler<int>? CompanionCardClicked;
        public event EventHandler<int>? PersonalityAssignRequested;

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
                    (_, _) => PersonalityAssignRequested?.Invoke(this, index);
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
                InstalledCommunityPrompts.ValidationAdvisory += OnValidationAdvisory;
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
            InstalledCommunityPrompts.ValidationAdvisory -= OnValidationAdvisory;
            if (_host != null) _host.Closed -= OnHostClosed;
            _host = null;
        }

        private void OnValidationAdvisory(int count) => Dispatcher.UIThread.Post(async () =>
        {
            if (_watchingCore && _host is { IsVisible: true } owner)
                await WorkshopAccordion.ShowRosterMessageAsync(owner, Loc.Get("community_prompt_warning_title"),
                    Loc.GetF("community_prompt_warning_body", count));
        });

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

            // WPF UpdateCompanionPromptLabels runs after UpdateCompanionCardsUI, even for
            // unsupported cards. Reuse the installed JSON reader WPF's service delegates to.
            var folder = Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName);
            for (int i = 0; i < 5; i++)
            {
                var promptId = s.GetCompanionPromptId(i);
                var promptName = string.IsNullOrEmpty(promptId) ? null
                    : InstalledCommunityPrompts.Load(folder, promptId, s.ActiveCommunityPromptId)?.Name;
                var displayName = CoreMods.MakeModAware(promptName ?? "");
                var label = this.FindControl<TextBlock>($"TxtCompanion{i}Prompt")!;
                label.Text = displayName;
                ToolTip.SetTip(label, string.IsNullOrEmpty(displayName)
                    ? null : Loc.GetF("tooltip_ai_personality", displayName));
            }
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
