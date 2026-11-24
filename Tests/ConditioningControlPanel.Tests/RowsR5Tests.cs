using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Emblems;
using ConditioningControlPanel.Views.Controls.AppSettingsSections;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows sweep, lane R5 (2026-11-24): every Settings section under Views/Controls/AppSettings,
/// realized off screen at 900 px through the shared rows check (emblem, OptionRow height, untrimmed
/// label, no overlap, silence), with the rows that start hidden shown (Gentle choices, the audio sync
/// pair, the Devices diagnostics expander, the Account privacy card). With CCP_ROWS_DUMP set each
/// saves r5-&lt;section&gt;.png. Plain construction of these sections plays nothing and opens no device
/// (the BEFORE stills were made the same way); nothing here clicks or invokes a control.
/// The Updates section has no option rows (a version line and two buttons), so it has no check.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsR5Tests
{
    private static void ExpandAll(FrameworkElement page)
    {
        foreach (var e in RowsRender.Descendants(page).OfType<Expander>()) e.IsExpanded = true;
    }

    [Fact]
    public void Performance_rows_read_and_motion_stays_first() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new PerformanceSettingsSection();
        var labels = RowsRender.Check(page, "r5-performance", minLabels: 11, show: new[] { "PnlGentleChoices" });
        // Owner rule: Motion is the first row of Performance.
        Assert.Equal(EmblemKind.Motion, labels.OrderBy(l => l.TranslatePoint(new Point(), page).Y).First().Emblem);
        // The section hue reaches the rows (no OptionSection here: the card sets it).
        var hue = (Color)Application.Current.FindResource("SectionHuePerformance");
        Assert.All(labels, l => Assert.Equal(hue, l.EffectiveHue));
    });

    [Fact]
    public void General_rows_read() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new GeneralSettingsSection(), "r5-general", minLabels: 8));

    [Fact]
    public void Audio_rows_read() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new AudioSettingsSection();
        RowsRender.Check(page, "r5-audio", minLabels: 8, show: new[] { "AudioSyncLatencyPanel" });
        Assert.IsType<RowValue>(page.TxtMaster);
    });

    [Fact]
    public void Devices_rows_read() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new DevicesSettingsSection(), "r5-devices", minLabels: 18, before: ExpandAll));

    // At 520 px the Devices diagnostics row "Keep gaze-reactive effects on the calibrated screen"
    // trims (RowLabel never wraps; the old chip text wrapped): left to Integration, see NIGHT-BRIEF R5.
    [Fact]
    public void Performance_rows_fit_at_popup_width() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new PerformanceSettingsSection(), "r5-performance-520", minLabels: 11, width: 520,
            show: new[] { "PnlGentleChoices" }));

    [Fact]
    public void General_rows_fit_at_popup_width() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new GeneralSettingsSection(), "r5-general-520", minLabels: 8, width: 520));

    [Fact]
    public void Monitors_rows_read() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new MonitorsSettingsSection(), "r5-monitors", minLabels: 5));

    [Fact]
    public void Notifications_rows_read() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new NotificationsSettingsSection(), "r5-notifications", minLabels: 3));

    [Fact]
    public void Emi_desk_rows_read() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new EmiDeskSettingsSection(), "r5-emidesk", minLabels: 6));

    [Fact]
    public void Account_privacy_row_reads() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new AccountSettingsSection(), "r5-account", minLabels: 1, show: new[] { "DataPrivacySection" }));

    [Fact]
    public void Data_rows_read() => WpfRenderHarness.OnStaThread(() =>
        RowsRender.Check(new DataSettingsSection(), "r5-data", minLabels: 3));
}
