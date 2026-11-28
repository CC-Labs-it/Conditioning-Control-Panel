using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Emblems;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows sweep, lane R4 (2026-11-24): Scheduler, Intensity Ramp, the Session editor's
/// Duration row and the Home dashboard's click choice popup. Each is realised off screen through
/// <see cref="RowsRender.Check"/>. The Dollhouse (<c>Chaos/ChaosHubWindow</c>) is converted too but
/// is NEVER built here: it starts its menu music on construction. Nothing here plays media or opens
/// a window.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsR4Tests
{
    [Fact]
    public void Scheduler_rows_wear_the_when_picture()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var labels = RowsRender.Check(new SchedulerFeatureControl(), "r4-scheduler", minLabels: 2);
            Assert.Equal(2, labels.Count(l => l.Emblem == EmblemKind.When));
        });
    }

    [Fact]
    public void Ramp_rows_and_link_chips_wear_emblems()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new IntensityRampFeatureControl();
            var labels = RowsRender.Check(page, "r4-ramp", minLabels: 13,
                show: new[] { "RowRangeStart", "RowRangeEnd" },
                before: _ =>
                {
                    page.ChkEndAt.IsChecked = false;
                    page.ChkLinkSpiral.IsChecked = true;
                    page.ChkLinkMaster.IsChecked = false;
                });

            Assert.False(labels.Single(l => l.Emblem == EmblemKind.After).IsOn);
            Assert.True(labels.Single(l => l.Emblem == EmblemKind.Spiral).IsOn);
            Assert.Equal(2, labels.Count(l => l.Emblem == EmblemKind.Ramp));
            Assert.Equal(2, labels.Count(l => l.Emblem == EmblemKind.Loud));
            Assert.Contains(labels, l => l.Emblem == EmblemKind.Fog);
            Assert.Contains(labels, l => l.Emblem == EmblemKind.Many && l.Subject == EmblemSubject.Image);
            // The number keeps the text the code-behind writes (RowValue has Text only).
            Assert.EndsWith("min", page.TxtDuration.Text);
        });
    }

    [Fact]
    public void Session_editor_duration_row_reads_as_a_row()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var w = new SessionEditorWindow();
            var content = (FrameworkElement)w.Content;
            w.Content = null;
            var labels = RowsRender.Check(content, "r4-session-editor", minLabels: 1);
            Assert.Equal(EmblemKind.Long, labels.Single().Emblem);
        });
    }

    /// <summary>
    /// The Dollhouse setup rows, read as TEXT only: building ChaosHubWindow starts its menu music,
    /// so it is never constructed in a test. Every RowLabel there names a picture and every switch
    /// label follows its own switch.
    /// </summary>
    [Fact]
    public void Dollhouse_setup_rows_wear_emblems_read_as_text_only()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "ConditioningControlPanel", "Chaos", "ChaosHubWindow.xaml"));
        var labels = System.Text.RegularExpressions.Regex.Matches(xaml, "<rows:RowLabel [^>]*/>")
            .Select(m => m.Value).ToList();
        Assert.True(labels.Count >= 20, $"only {labels.Count} RowLabels in the Dollhouse");
        Assert.All(labels, l => Assert.Contains("Emblem=\"", l));
        foreach (var chk in new[] { "ChkShake", "ChkFlashes", "ChkSkiaFx", "ChkPinTop", "ChkSharedHost", "ChkAnnouncer",
                     "ChkNarrative", "ChkBackdrop", "ChkTunnel", "ChkDarters", "ChkBoonDraft", "ChkCurses" })
            Assert.Contains(labels, l => l.Contains("ElementName=" + chk + "}"));
        Assert.Contains("rows:RowHue.Hue=\"#FFE84393\"", xaml);
    }

    [Theory]
    [InlineData(900.0)]
    [InlineData(520.0)]
    public void Home_click_choice_switch_wears_the_click_picture(double width)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var home = new SettingsTabView();
            var body = home.ClickChoiceBody;
            home.ClickChoicePopup.Child = null;
            body.HorizontalAlignment = HorizontalAlignment.Left;
            home.InvertDashboardClicks.IsChecked = false;
            var labels = RowsRender.Check(body, $"r4-home-clickchoice-{width:0}", minLabels: 1, width: width);
            var invert = labels.Single();
            Assert.Equal(EmblemKind.Click, invert.Emblem);
            Assert.False(invert.IsOn);
        });
    }
}
