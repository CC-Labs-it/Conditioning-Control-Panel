using System;
using System.Globalization;
using System.IO;
using ConditioningControlPanel.Localization;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 review (wave 6, item 5, second desk run): the invite card read "Resets 1 dic" in English chrome:
/// the month came from the OS culture (Italian). Dates the invite card and the redeem box write into
/// their own sentences now follow the app's language.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class InviteDateCultureTests
{
    [Fact]
    public void The_reset_month_follows_the_app_language_not_the_os()
    {
        var os = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
            var when = new DateTime(2026, 12, 1, 9, 0, 0);
            RowsRender.InLanguage("en", () =>
            {
                Assert.Equal("1 Dec", Loc.Date(when, "d MMM"));
                Assert.Equal("Resets 1 Dec", Loc.GetF("invites_resets", Loc.Date(when, "d MMM")));
                return 0;
            });
            RowsRender.InLanguage("de", () =>
            {
                Assert.Equal("1 Dez.", Loc.Date(when, "d MMM"));
                return 0;
            });
            Assert.Equal(CultureInfo.GetCultureInfo("pt-BR"), Loc.CultureFor("pt-BR"));
            Assert.Equal(CultureInfo.GetCultureInfo("zh-CN"), Loc.CultureFor("zh-CN"));
            Assert.Equal(CultureInfo.GetCultureInfo("en"), Loc.CultureFor(null));
        }
        finally { CultureInfo.CurrentCulture = os; }
    }

    [Fact]
    public void Every_app_language_is_a_culture()
    {
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
            Assert.Equal(code, Loc.CultureFor(code).Name);
    }

    [Fact]
    public void The_invite_card_and_redeem_box_format_dates_with_the_app_culture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var invites = Path.Combine(dir!.FullName, "ConditioningControlPanel", "Controls", "Invites");
        foreach (var file in new[] { "InvitePanel.cs", "InviteRedeemBox.cs" })
        {
            var src = File.ReadAllText(Path.Combine(invites, file));
            Assert.DoesNotContain(".ToLocalTime().ToString(\"d MMM", src);
            Assert.Contains("Loc.Date(", src);
        }
    }
}
