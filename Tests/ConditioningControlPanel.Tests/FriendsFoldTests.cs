using System;
using ConditioningControlPanel.Controls.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The drawer's folds (owner 2026-11-22): every new string is in all ten languages.
/// </summary>
public partial class FriendsDrawerTests
{
    [Fact]
    public void Every_fold_string_is_in_all_ten_languages()
    {
        var dir = System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        foreach (var lang in new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, lang + ".json")));
            foreach (var k in FriendsFold.AllKeys)
                Assert.True(json[k] != null, $"{lang}.json is missing {k}");
        }
    }
}
