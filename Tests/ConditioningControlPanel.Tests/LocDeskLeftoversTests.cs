using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 desk run (wave 4, lane D item 2): English seen on the German and Japanese pages. Five keys
/// were still English in eight language files (Italian had them), and the Haptics page wrote its
/// status, connect button, device line and temperament line from code once, so a language switch
/// left them in the language the page was built in.
/// </summary>
public class LocDeskLeftoversTests
{
    private static readonly string[] Keys =
    {
        "flash_exit_shatter_note", "settings_section_monitors", "settings_section_plans",
        "plans_header_title", "nav_tab_ramp",
    };

    private static string Repo()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void The_desk_run_keys_are_translated_in_every_language()
    {
        var langDir = SourceRoots.LanguagesDirectory;
        using var en = JsonDocument.Parse(File.ReadAllText(Path.Combine(langDir, "en.json")));
        foreach (var file in Directory.GetFiles(langDir, "*.json").Where(f => Path.GetFileName(f) != "en.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var key in Keys)
            {
                var english = en.RootElement.GetProperty(key).GetString();
                var local = doc.RootElement.GetProperty(key).GetString();
                Assert.False(string.IsNullOrWhiteSpace(local), $"{Path.GetFileName(file)} {key} is empty");
                Assert.True(local != english, $"{Path.GetFileName(file)} {key} is still English");
            }
        }
    }

    [Fact]
    public void The_haptics_page_rewrites_its_code_set_lines_on_a_language_switch()
    {
        var src = File.ReadAllText(Path.Combine(Repo(), "ConditioningControlPanel", "MainWindow", "MainWindow.Haptics.cs"));
        Assert.Contains("LanguageChanged += (_, _) => HapticsRunOnUi(RefreshHapticLanguageText)", src);
        var body = src.Substring(src.IndexOf("internal void RefreshHapticLanguageText()"));
        body = body.Substring(0, body.IndexOf("private void OnHapticRoutingRowChanged"));
        Assert.Contains("RefreshHapticConnectionUi()", body);
        Assert.Contains("TxtHapticTemperamentDesc", body);
    }
}
