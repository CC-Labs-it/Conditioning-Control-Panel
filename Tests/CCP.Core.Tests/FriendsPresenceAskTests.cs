using System;
using System.IO;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FriendsPresenceAskTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    public void OnlyAnUnansweredHiddenAccountIsAsked(bool available, bool shared, bool asked, bool expected)
        => Assert.Equal(expected, FriendsPresenceAsk.ShouldAsk(available, shared, asked));

    [Fact]
    public void AChoicePersistsAcrossReads_InTheSuppliedSandbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-presence-ask-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(FriendsPresenceAsk.Asked(root));
            FriendsPresenceAsk.MarkAsked(root);
            Assert.True(FriendsPresenceAsk.Asked(root));
            Assert.Single(Directory.GetFiles(root));
            Assert.Contains("T", File.ReadAllText(Path.Combine(root, "friends_presence_asked.flag")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StorageFailureNeverBreaksTheDrawer()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ccp-presence-missing-" + Guid.NewGuid().ToString("N"), "child");
        FriendsPresenceAsk.MarkAsked(missing);
        Assert.False(FriendsPresenceAsk.Asked(missing));
    }
}
