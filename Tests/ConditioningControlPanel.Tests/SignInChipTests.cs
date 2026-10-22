using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Signed out, every "Sign in" surface opens the sign-in dialog. The header chip used to jump to
/// the Account page, which only says "sign in to unlock" and has no button: a dead end (owner,
/// 2026-10-19).
/// </summary>
public class SignInChipTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + System.AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static string Flat(string s) => Regex.Replace(s, @"\s+", " ");

    private static string Body(string src, string signature)
    {
        int at = src.IndexOf(signature, System.StringComparison.Ordinal);
        Assert.True(at >= 0, signature + " not found");
        int open = src.IndexOf('{', at), depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
        }
        return src.Substring(open);
    }

    [Fact]
    public void Header_chip_opens_the_sign_in_dialog_when_signed_out()
    {
        var body = Flat(Body(ReadSource("MainWindow", "MainWindow.UiUpdates.cs"), "void BtnAccountChip_Click("));
        int signIn = body.IndexOf("if (!App.IsLoggedIn) { OpenUnifiedLoginDialog(); return; }", System.StringComparison.Ordinal);
        int page = body.IndexOf("ShowTab(\"appsettings\")", System.StringComparison.Ordinal);
        Assert.True(signIn >= 0, "signed out, the chip must open the sign-in dialog");
        Assert.True(page > signIn, "signed in, the chip still opens the Account page, after the sign-in branch");
    }

    [Fact]
    public void Account_card_is_a_sign_in_button_when_signed_out()
    {
        var xaml = ReadSource("Views", "Controls", "AppSettings", "AccountSettingsSection.xaml");
        Assert.Contains("MouseLeftButtonUp=\"AccountTierCard_MouseLeftButtonUp\"", xaml);

        var cs = ReadSource("Views", "Controls", "AppSettings", "AccountSettingsSection.xaml.cs");
        var handler = Flat(Body(cs, "void AccountTierCard_MouseLeftButtonUp("));
        Assert.Contains("if (App.IsLoggedIn) return;", handler);
        Assert.Contains("OpenUnifiedLoginDialog()", handler);
        Assert.Contains("AccountTierCard.Cursor = App.IsLoggedIn ? null : System.Windows.Input.Cursors.Hand;", Flat(cs));
    }
}
