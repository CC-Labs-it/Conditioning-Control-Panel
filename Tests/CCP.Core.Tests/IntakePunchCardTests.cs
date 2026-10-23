using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core IntakePunchCard against WPF IntakePunchCardService, driven by a real SessionRunner:
/// a drafted session stamps once at 50 % or on completion, never on an early stop or for another session,
/// and the card on disk is what the intake's duck rule reads.</summary>
[Collection(SessionStatics.Name)]
public sealed class IntakePunchCardTests : IDisposable
{
    private const string Id = "punch-drafted";
    private readonly SessionLogService _logs = new();
    private readonly SessionRunner _runner;
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-punch-").FullName;

    public IntakePunchCardTests()
    {
        PhrasePoolCustody.Seed();
        _runner = new SessionRunner(_logs);
    }

    public void Dispose()
    {
        _runner.Stop();
        CoreEngine.Stop();
        CoreSession.IsSessionRunningProvider = null;
        CoreSession.NoteUserPhrasePoolEdit = null;
        CoreSession.ReapplyPhrasePoolOverrides = null;
        CoreSession.UserPhrasePoolsWhileOverriding = null;
        foreach (var f in Directory.GetFiles(_logs.LogsFolder, "*_" + Id + ".json")) File.Delete(f);
        Directory.Delete(_dir, true);
    }

    private string CardPath => Path.Combine(_dir, IntakePunchCardState.FileName);

    private static Session TwoMinutes(string id = Id) => new() { Id = id, Name = "Drafted", DurationMinutes = 2 };

    private IntakePunchCard NewCard()
    {
        var card = new IntakePunchCard(CardPath);
        card.Attach(_runner);
        return card;
    }

    [Fact]
    public void HalfARunOfTheDraftedSessionPunchesOnceAndTheDuckRuleSeesIt()
    {
        var card = NewCard();
        Assert.Equal(1, card.PunchedCount);   // the first hole is on the house
        Assert.True(File.Exists(CardPath));
        Assert.False(IntakePunchCardState.ReadEverCompletedIntake(_dir));

        card.NotifyIntakeCompleted(Id);
        card.NotifyIntakeCompleted(Id);   // a re-draft queues no second stamp
        Assert.Single(card.State.PendingDrafts);
        Assert.True(IntakePunchCardState.ReadEverCompletedIntake(_dir));   // persisted at once

        _runner.Start(TwoMinutes("someone-else"));
        _runner.Tick(TimeSpan.FromSeconds(90));
        _runner.Stop();
        Assert.Equal(1, card.PunchedCount);   // only the drafted session redeems

        _runner.Start(TwoMinutes());
        _runner.Tick(TimeSpan.FromSeconds(59));
        Assert.Equal(1, card.PunchedCount);
        _runner.Tick(TimeSpan.FromSeconds(60));   // 50 %
        Assert.Equal(2, card.PunchedCount);
        _runner.Tick(TimeSpan.FromSeconds(90));
        Assert.Equal(2, card.PunchedCount);
        Assert.Empty(card.State.PendingDrafts);
        Assert.Equal(2, new IntakePunchCard(CardPath).PunchedCount);
    }

    [Fact]
    public void AnEarlyStopKeepsTheStampPendingAndANaturalEndRedeemsIt()
    {
        var card = NewCard();
        card.NotifyIntakeCompleted(Id);

        _runner.Start(TwoMinutes());
        _runner.Tick(TimeSpan.FromSeconds(40));
        _runner.Stop();
        Assert.Equal(1, card.PunchedCount);
        Assert.Single(card.State.PendingDrafts);

        _runner.Start(TwoMinutes());
        _runner.Tick(TimeSpan.FromSeconds(120));   // completes before any progress tick
        Assert.False(_runner.IsRunning);
        Assert.Equal(2, card.PunchedCount);
        Assert.Empty(new IntakePunchCard(CardPath).State.PendingDrafts);
    }
}
