using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Emblems;
using ConditioningControlPanel.Views.Controls.Companion;
using ConditioningControlPanel.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows sweep, lane R6b (2026-11-24): the Awareness tab, the AI permissions card, the three
/// Workshop cells, the custom keyword panel, the privacy card's two captions, the picker card and the
/// Permissions page. Each surface is realised off screen through <see cref="RowsRender.Check"/>, which
/// also asserts the test process stayed silent. Nothing here clicks, starts a voice, a chat, OCR or a
/// player: every control below is already built the same way by the older Companion render tests.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsR6bTests
{
    [Fact]
    public void Awareness_tab_rows_wear_emblems_in_the_tab_amber()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new AwarenessTabView();
            var labels = RowsRender.Check(page, "r6b-awareness", minLabels: 10,
                before: _ =>
                {
                    // The keyword panel is its own namescope: open its fold and detail rows by hand.
                    page.KeywordPanel.KeywordTriggersExpander.IsExpanded = true;
                    page.KeywordPanel.ScreenOcrIntervalPanel.Visibility = Visibility.Visible;
                    page.KeywordPanel.HighlightDurationPanel.Visibility = Visibility.Visible;
                    page.ChkAwarenessOcr.IsChecked = true;
                    page.ChkAwarenessKeyboard.IsChecked = false;
                });

            Assert.Equal(2, labels.Count(l => l.Text == "Cooldown between effects" || l.Text == "Same-word cooldown"));
            Assert.True(labels.Single(l => l.Text == "Screen OCR").IsOn);
            Assert.False(labels.Single(l => l.Text == "Keyboard").IsOn);
            Assert.Equal(EmblemKind.Eye, labels.Single(l => l.Text == "Screen OCR").Emblem);
            Assert.Equal(2, labels.Count(l => l.Emblem == EmblemKind.Keepout));
            // The cooldown numbers keep the text the code-behind writes (RowValue has Text only).
            Assert.Equal("10s", page.TxtAwarenessGlobalCooldown.Text);
            // The tab's own amber reaches the rows.
            Assert.Equal(System.Windows.Media.Color.FromRgb(0xFF, 0xB8, 0x4E),
                RowHue.GetHue(labels.First(l => l.Text == "Loop protection")));
        });
    }

    [Fact]
    public void Permissions_card_checklist_and_memory_wear_emblems()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var grid = new AiPermissionsGrid();
            var labels = RowsRender.Check(grid, "r6b-permissions", minLabels: 12,
                show: new[] { "EffectPermsPanel" },
                before: _ => { grid.ChkAllowVideo.IsChecked = true; grid.ChkAllowFlash.IsChecked = false; });

            Assert.Contains(labels, l => l.Emblem == EmblemKind.Many && l.Subject == EmblemSubject.Image && !l.IsOn);
            Assert.Contains(labels, l => l.Emblem == EmblemKind.Many && l.Subject == EmblemSubject.Video && l.IsOn);
            Assert.Contains(labels, l => l.Emblem == EmblemKind.Most);
            Assert.Contains(labels, l => l.Emblem == EmblemKind.Bell);
            // Each switch still names its own setting through Tag; the label is only its content now.
            Assert.Equal("Flash", grid.ChkAllowFlash.Tag);
            Assert.IsType<RowLabel>(grid.ChkAllowFlash.Content);
        });
    }

    [Fact]
    public void Workshop_behavior_cell_rows_fit_the_cell()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var labels = RowsRender.Check(new WorkshopBehaviorCell(), "r6b-workshop-behavior", minLabels: 7, width: 314);
            // "Speak idle lines when the engine is off" keeps its small cell label: it cannot fit a cell.
            Assert.Single(labels, l => l.Emblem == EmblemKind.Mic);
        });
    }

    [Fact]
    public void Workshop_awareness_and_triggers_cells_fit_the_cell()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            RowsRender.Check(new WorkshopAwarenessCell(), "r6b-workshop-awareness", minLabels: 3, width: 314,
                show: new[] { "AwarenessSettingsPanel" });
            RowsRender.Check(new WorkshopTriggersCell(), "r6b-workshop-triggers", minLabels: 2, width: 314,
                show: new[] { "TriggerSettingsPanelCompanion" });
        });
    }

    [Fact]
    public void Privacy_card_captions_wear_emblems()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var card = new AwarenessPrivacyView { DataContext = MockAwarenessPrivacyVm.Live() };
            var labels = RowsRender.Check(card, "r6b-privacy", minLabels: 1, width: 430);
            Assert.Contains(labels, l => l.Emblem == EmblemKind.Safe);
        });
    }

    [Fact]
    public void Picker_card_and_permissions_page_labels_wear_emblems()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var picker = RowsRender.Check(new CompanionPickerCard(), "r6b-picker", minLabels: 2, width: 540,
                show: new[] { "LivePanel" });
            Assert.Contains(picker, l => l.Emblem == EmblemKind.Persona);

            var page = new PermissionsPage();
            page.PermissionsHost.Content = new AiPermissionsGrid();
            var labels = RowsRender.Check(page, "r6b-permissions-page", minLabels: 2);
            Assert.Contains(labels, l => l.Emblem == EmblemKind.Bell && l.Text != null);
        });
    }
}
