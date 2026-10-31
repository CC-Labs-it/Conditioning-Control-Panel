using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public sealed class IntakePendingDraftTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("intake-card-").FullName;
    private string Pathname => Path.Combine(_dir, IntakePunchCardState.FileName);
    private IntakePunchCardState Read() => JsonSerializer.Deserialize<IntakePunchCardState>(File.ReadAllText(Pathname))!;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void CompletionPersistsOnlyAHalfStampAndMakesTheNextLaunchAReturnVisit()
    {
        Assert.False(IntakePunchCardState.ReadEverCompletedIntake(_dir));
        IntakePunchCardState.RecordCompletedDraft(_dir, "draft");
        var card = Read();
        Assert.True(IntakePunchCardState.ReadEverCompletedIntake(_dir));
        Assert.NotNull(card.CardStartedUtc);
        Assert.Equal(1, card.PunchedCount); // first hole is free, completion alone earns no second hole
        Assert.Single(card.PunchedUtc);
        Assert.Equal("draft", Assert.Single(card.PendingDrafts).SessionId);
        Assert.Null(card.CompletedUtc);
        Assert.Null(card.PrizeClaimedUtc);
        Assert.False(File.Exists(Pathname + ".tmp"));
    }

    [Fact]
    public void BlankAndRepeatedIdsDoNotCreateExtraDrafts()
    {
        IntakePunchCardState.RecordCompletedDraft(_dir, " ");
        Assert.False(File.Exists(Pathname));
        IntakePunchCardState.RecordCompletedDraft(_dir, "same");
        var first = Read().PendingDrafts.Single().DraftedUtc;
        IntakePunchCardState.RecordCompletedDraft(_dir, "same");
        Assert.Equal(first, Read().PendingDrafts.Single().DraftedUtc);
    }

    [Fact]
    public void QueueKeepsEightNewestAndDropsExpiredDrafts()
    {
        IntakePunchCardState.RecordCompletedDraft(_dir, "expired");
        var card = Read();
        card.PendingDrafts[0].DraftedUtc = DateTime.UtcNow.AddDays(-91);
        File.WriteAllText(Pathname, JsonSerializer.Serialize(card));
        IntakePunchCardState.RecordCompletedDraft(_dir, "fresh");
        Assert.Equal("fresh", Read().PendingDrafts.Single().SessionId);
        for (var i = 0; i < 9; i++) IntakePunchCardState.RecordCompletedDraft(_dir, i.ToString());
        Assert.Equal(Enumerable.Range(1, 8).Select(i => i.ToString()), Read().PendingDrafts.Select(d => d.SessionId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletedCardsNeverQueueMoreWork(bool timestamp)
    {
        var card = new IntakePunchCardState { CardStartedUtc = DateTime.UtcNow,
            PunchedCount = timestamp ? 2 : 8, CompletedUtc = timestamp ? DateTime.UtcNow : null };
        File.WriteAllText(Pathname, JsonSerializer.Serialize(card));
        IntakePunchCardState.RecordCompletedDraft(_dir, "extra");
        Assert.Empty(Read().PendingDrafts);
    }

    [Fact]
    public void CorruptMainRecoversTheTemporaryCardWithoutLosingProgress()
    {
        var card = new IntakePunchCardState { CardStartedUtc = DateTime.UtcNow, PunchedCount = 4 };
        File.WriteAllText(Pathname, "broken");
        File.WriteAllText(Pathname + ".tmp", JsonSerializer.Serialize(card));
        IntakePunchCardState.RecordCompletedDraft(_dir, "recovered");
        Assert.Equal(4, Read().PunchedCount);
        Assert.Equal("recovered", Read().PendingDrafts.Single().SessionId);
    }
}
