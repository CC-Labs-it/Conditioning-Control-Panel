using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class IntakeSessionCreditTests
{
    [Fact]
    public void RunnerCreditsOnlyTheDraftAtHalfOrNaturalCompletion()
    {
        var dir = Directory.CreateTempSubdirectory("intake-credit-");
        var previous = AvApp.IntakePunchCard;
        using var card = new IntakePunchCardService(Path.Combine(dir.FullName, "card.json"));
        var runner = new SessionRunner(new SessionLogService());
        AvApp.IntakePunchCard = card;
        AvApp.WireIntakeSessionCredit(runner);
        PhrasePoolCustody.Seed();
        try
        {
            card.NotifyIntakeCompleted("draft");
            var draft = new Session { Id = "draft", Name = "Draft", DurationMinutes = 10 };
            runner.Start(new Session { Id = "other", Name = "Other", DurationMinutes = 10 });
            runner.Tick(TimeSpan.FromMinutes(6));
            runner.Stop();
            Assert.Equal(1, card.PunchedCount);
            runner.Start(draft);
            runner.Tick(TimeSpan.FromMinutes(4.99));
            runner.Stop();
            Assert.Equal(1, card.PunchedCount);
            runner.Start(draft);
            runner.Tick(TimeSpan.FromMinutes(5));
            Assert.Equal(2, card.PunchedCount);
            runner.Tick(TimeSpan.FromMinutes(6));
            runner.Stop();
            Assert.Equal(2, card.PunchedCount);
            card.NotifyIntakeCompleted("natural");
            runner.Start(new Session { Id = "natural", Name = "Natural", DurationMinutes = 10 });
            runner.Tick(TimeSpan.FromMinutes(10));
            Assert.Equal(3, card.PunchedCount);
            card.Save();
            using var loaded = new IntakePunchCardService(Path.Combine(dir.FullName, "card.json"));
            Assert.Equal(3, loaded.PunchedCount);
            Assert.True(loaded.HasEverCompletedIntake);
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop();
            AvApp.IntakePunchCard = previous;
            card.Save();
            dir.Delete(true);
        }
    }
}
