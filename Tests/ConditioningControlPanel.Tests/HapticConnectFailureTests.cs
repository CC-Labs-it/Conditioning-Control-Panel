using System.IO;
using ConditioningControlPanel.Localization;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 review (wave 6, item 2): after a FAILED connect the Haptics page lost "Failed" and its hint:
/// the connect's own finally (since 7.3.x) and, from 7.4, the language-switch repaint both rewrote the
/// lines as "Disconnected" / "No devices". The failure is now kept until a connect works or is pressed
/// again, and every repaint draws it in the current language.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class HapticConnectFailureTests
{
    private static readonly MainWindow.HapticConnectFailure Failed = new("label_failed", "haptics_connect_failed_hint", null);

    [Fact]
    public void A_failed_connect_keeps_its_own_lines_in_every_language()
    {
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            RowsRender.InLanguage(code, () =>
            {
                var (status, devices) = MainWindow.HapticConnectionLines(false, 0, "", Failed);
                Assert.Equal(Loc.Get("label_failed"), status);
                Assert.Equal(Loc.Get("haptics_connect_failed_hint"), devices);
                Assert.NotEqual(Loc.Get("label_disconnected"), status);
                return 0;
            });
        }
    }

    [Fact]
    public void An_error_keeps_its_message_and_a_working_connection_wins()
    {
        var error = new MainWindow.HapticConnectFailure("label_error", null, "Intiface refused the socket");
        var (status, devices) = MainWindow.HapticConnectionLines(false, 0, "", error);
        Assert.Equal(Loc.Get("label_error"), status);
        Assert.Equal("Intiface refused the socket", devices);

        var (onStatus, onDevices) = MainWindow.HapticConnectionLines(true, 0, "", Failed);
        Assert.Equal(Loc.Get("label_connected"), onStatus);
        Assert.Equal(Loc.Get("label_no_devices"), onDevices);

        var (offStatus, _) = MainWindow.HapticConnectionLines(false, 0, "", null);
        Assert.Equal(Loc.Get("label_disconnected"), offStatus);
    }

    [Fact]
    public void The_connect_press_and_every_repaint_go_through_the_kept_failure()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "MainWindow", "MainWindow.Haptics.cs"));
        var refresh = src.Substring(src.IndexOf("internal void RefreshHapticConnectionUi()"));
        refresh = refresh.Substring(0, refresh.IndexOf("internal void RefreshAudioSyncCardVisibility"));
        Assert.Contains("HapticConnectionLines(connected, count", refresh);
        Assert.DoesNotContain("\"label_disconnected\"", refresh);
        Assert.Contains("_hapticConnectFailure = new HapticConnectFailure(\"label_failed\", \"haptics_connect_failed_hint\", null)", src);
    }

    /// <summary>Wave 8 (reviewer nit): a connect press that stops early (no provider ticked, an unusable
    /// Intiface address) clears the kept failure too, so an old "Failed" and its hint do not stay up.</summary>
    [Fact]
    public void A_press_that_stops_early_clears_the_kept_failure()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "MainWindow", "MainWindow.Haptics.cs"));
        var click = src.Substring(src.IndexOf("internal async void BtnHapticConnect_Click("));
        click = click.Substring(0, click.IndexOf("internal void BtnHapticPanic_Click"));
        int clear = click.IndexOf("_hapticConnectFailure = null;");
        int noProvider = click.IndexOf("\"haptics_no_provider_enabled\"");
        Assert.True(clear >= 0, "the connect press never clears the kept failure");
        Assert.True(clear < noProvider, "the no-provider return leaves before the clear");
        Assert.True(clear < click.IndexOf("\"haptics_intiface_url_invalid\""), "the bad Intiface address return leaves before the clear");
        int repaint = click.IndexOf("RefreshHapticConnectionUi();");
        Assert.True(repaint > clear && repaint < noProvider, "the cleared failure is not repainted before an early return");
    }
}
