using System;
using System.IO;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Post-merge review of the friend link stack (#2498-#2501): a link pasted inside a message
/// emptied the add box, and a friend who came back on another platform kept the old mark.
/// </summary>
public class FriendsReviewFixTests
{
    [Theory]
    [InlineData("add me https://app.cclabs.app/f/7K2Q9ABCDEFG", "7K2Q9ABCDEFG")]
    [InlineData("Be my friend on CCP: <https://app.cclabs.app/f/7k2q9abcdefg>.", "7K2Q9ABCDEFG")]
    [InlineData("link (app.cclabs.app/f/7K2Q9ABCDEFG) thanks", "7K2Q9ABCDEFG")]
    public void A_link_inside_a_message_is_read(string pasted, string token)
    {
        Assert.Equal(token, FriendsLinkRules.LinkInText(pasted));
        Assert.Equal((token, true), FriendsLinkRules.BoxText(pasted));
    }

    [Theory]
    [InlineData("add me https://evil.example/f/7K2Q9ABCDEFG")]
    [InlineData("go to https://app.cclabs.app.evil/f/7K2Q9ABCDEFG now")]
    [InlineData("my code is 7K2Q9ABCDEFG")]
    [InlineData("nothing here")]
    [InlineData("")]
    [InlineData(null)]
    public void Another_host_or_a_bare_word_in_a_message_is_never_a_token(string? pasted)
        => Assert.Null(FriendsLinkRules.LinkInText(pasted));

    [Fact]
    public void Going_online_or_offline_drops_the_old_platform_marks()
    {
        var src = SourceRoots.ReadProductFile("Services", "Friends", "FriendsService.cs");
        Assert.Contains("return f with { Online = on, Presence = p, Platforms = Array.Empty<string>() };", src);
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}
