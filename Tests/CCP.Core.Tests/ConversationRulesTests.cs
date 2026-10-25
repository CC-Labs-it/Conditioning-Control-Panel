using System.Globalization;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Xunit;
using Rules = ConditioningControlPanel.Services.Companion.ConversationRules;

namespace CCP.Core.Tests;

/// <summary>The v2 conversation page rules both heads call (WPF V2/PreferredNameEditor, ConversationPageVm).</summary>
public sealed class ConversationRulesTests
{
    private sealed class Guard(bool block) : IModerationGuard
    {
        public ModerationResult CheckInput(string text) =>
            block ? ModerationResult.Block(ProhibitedCategory.ProfessionalAdvice, "test") : ModerationResult.Pass();
        public ModerationResult CheckOutput(string text) => ModerationResult.Pass();
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(" \r\n ", "")]
    [InlineData("  Alex\t  Rose \n", "Alex Rose")]
    public void NamesAreSingleLineAndEmptyClears(string? input, string expected)
        => Assert.Equal(expected, Rules.NormalizePreferredName(input));

    [Fact]
    public void LengthLimitPreservesCombinedCharacters()
    {
        var normalized = Rules.NormalizePreferredName(string.Concat(Enumerable.Repeat("e\u0301", 70)));
        Assert.Equal(64, new StringInfo(normalized).LengthInTextElements);
        Assert.EndsWith("e\u0301", normalized);
    }

    [Fact]
    public void SaveWritesClearsRefusesAndIgnoresAStaleAccount()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-name-").FullName;
        try
        {
            using var store = new MemoryStore(Path.Combine(dir, "memory.json"));
            using var other = new MemoryStore(Path.Combine(dir, "other.json"));

            Assert.Equal((Rules.NameSave.Saved, "Alex Rose"), Rules.SavePreferredName(store, store, " Alex  Rose ", new Guard(false)));
            Assert.Equal("Alex Rose", Rules.PreferredName(store));

            Assert.Equal((Rules.NameSave.Refused, ""), Rules.SavePreferredName(store, store, "Bad", new Guard(true)));
            Assert.Equal("Alex Rose", Rules.PreferredName(store));

            // The account changed after the editor opened: nothing is written to either store.
            Assert.Equal(Rules.NameSave.Stale, Rules.SavePreferredName(other, store, "Kim", new Guard(false)).Result);
            Assert.Equal(Rules.NameSave.Stale, Rules.SavePreferredName(null, store, "Kim", new Guard(false)).Result);
            Assert.Equal("", Rules.PreferredName(other));
            Assert.Equal("Alex Rose", Rules.PreferredName(store));

            Assert.Equal((Rules.NameSave.Saved, ""), Rules.SavePreferredName(store, store, "  ", new Guard(false)));
            Assert.Equal("", Rules.PreferredName(store));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RecapIsEmptyAndClearRefusesWithoutABrain()
    {
        Assert.Empty(Rules.Recap(null));
        Assert.False(Rules.ClearRecap(null, null));
    }

    [Theory]
    [InlineData(TurnKind.UserChat, true)]
    [InlineData(TurnKind.AssistantChat, true)]
    [InlineData(TurnKind.AmbientEvent, false)]
    [InlineData(TurnKind.AmbientReply, true)]
    [InlineData(TurnKind.BarkEcho, false)]
    [InlineData(TurnKind.SystemNote, false)]
    [InlineData(TurnKind.TubeLine, true)]
    public void OnlyChatTurnsShow(TurnKind kind, bool expected) => Assert.Equal(expected, Rules.Shows(kind));

    [Theory]
    [InlineData("hi", true, true)]
    [InlineData("hi", false, false)]
    [InlineData("  ", true, false)]
    public void RepliesAreSpokenOnlyWhenTheTubeIsOn(string text, bool enabled, bool expected)
        => Assert.Equal(expected, Rules.ShouldSpeak(text, enabled));

    [Theory]
    [InlineData(AiFailureKind.SignInRequired, "companion_v2_error_signin")]
    [InlineData(AiFailureKind.DailyLimit, "companion_v2_error_limit")]
    [InlineData(AiFailureKind.BudgetLimit, "companion_v2_error_budget")]
    [InlineData(AiFailureKind.Offline, "companion_v2_error_offline")]
    [InlineData(AiFailureKind.Busy, "companion_v2_busy")]
    [InlineData(AiFailureKind.Cancelled, "companion_v2_cancelled")]
    [InlineData(AiFailureKind.InvalidResponse, "companion_v2_error_invalid")]
    [InlineData(null, "companion_v2_unavailable")]
    public void FailuresMapToTheirNotice(AiFailureKind? failure, string key) => Assert.Equal(key, Rules.FailureNoticeKey(failure));
}
