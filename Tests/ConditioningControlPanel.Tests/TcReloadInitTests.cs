using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 review (wave 6, item 6): the T&amp;C pause card's "Main menu" reloads the page in the same window
/// (persist, leaving, location.reload). The reloaded page posts "ready" again and waits for init with no
/// timeout (bridge.js webview transport), so the host MUST answer every ready with a fresh init carrying
/// the caps (edition, platform, save slot, Expo). It does: ChaosWebViewHost invokes OnReady on each ready
/// and TcHostService.OnPageReady posts the init built from the current caps. A once-guard here would
/// leave a blank window after Main menu (proved headless: C:/wt-74rev/tools/w6f-reload.mjs once).
/// </summary>
public class TcReloadInitTests
{
    private static string App(params string[] parts)
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", Path.Combine(parts)));
    }

    private static string Between(string src, string from, string to)
    {
        int a = src.IndexOf(from, System.StringComparison.Ordinal);
        Assert.True(a >= 0, $"'{from}' not found");
        int b = src.IndexOf(to, a + from.Length, System.StringComparison.Ordinal);
        Assert.True(b > a, $"'{to}' not found after '{from}'");
        return src.Substring(a, b - a);
    }

    [Fact]
    public void Every_ready_reaches_the_page_owner()
    {
        var ready = Between(App("Chaos", "ChaosWebViewHost.cs"), "case \"ready\":", "break;");
        Assert.Contains("_pageReady?.Invoke()", ready);
        Assert.DoesNotContain("return", ready);
        Assert.DoesNotContain("if (IsReady", ready);
    }

    [Fact]
    public void The_tc_host_answers_every_ready_with_a_fresh_init()
    {
        var src = App("Services", "Tc", "TcHostService.cs");
        Assert.Contains("OnReady = OnPageReady", src);
        var onReady = Between(src, "private static void OnPageReady()", "private static void PostInit()");
        Assert.Contains("PostInit()", onReady);
        var post = Between(src, "private static void PostInit()", "private static void OnPageMessage(");
        Assert.Contains("TcRules.Caps(_edition, saveSlot: SaveSlotOpen, ExpoCap())", post);
        Assert.Contains("_host?.Post(init)", post);
    }
}
