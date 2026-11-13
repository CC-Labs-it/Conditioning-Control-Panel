using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class IntakeSessionCreditTests
{
    [Fact]
    public void OnlyNaturalCompletionQueuesCompanionFeedback() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        var service = ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.Instance;
        var field = service.GetType().GetField("_pendingFeedback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var saved = field.GetValue(service);
        var runner = new SessionRunner(new SessionLogService());
        AvApp.WireIntakeSessionCredit(runner);
        PhrasePoolCustody.Seed();
        try
        {
            field.SetValue(service, null);
            var session = new Session { Id = "feedback-test", Name = "Feedback test", DurationMinutes = 1 };
            runner.Start(session);
            runner.Stop();
            Assert.Null(field.GetValue(service));
            runner.Start(session);
            runner.Tick(TimeSpan.FromMinutes(1));
            var feedback = Assert.IsType<ValueTuple<string, string?, DateTime>>(field.GetValue(service));
            Assert.Equal(session.GetModeAwareName(), feedback.Item1);
            Assert.Null(feedback.Item2);
            Assert.True(feedback.Item3 > DateTime.UtcNow);
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop();
            field.SetValue(service, saved);
        }
    });

    [Fact]
    public void IntakeCreditTestRestoresPendingFeedback()
    {
        var service = ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.Instance;
        var field = service.GetType().GetField("_pendingFeedback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var saved = field.GetValue(service);
        var pending = ("earlier session", (string?)null, DateTime.UtcNow.AddMinutes(10));
        try
        {
            field.SetValue(service, pending);
            RunnerCreditsOnlyTheDraftAtHalfOrNaturalCompletion();
            Assert.Equal(pending, field.GetValue(service));
        }
        finally { field.SetValue(service, saved); }
    }

    [Fact]
    public void RunnerCreditsOnlyTheDraftAtHalfOrNaturalCompletion() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<AvApp>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("intake-credit-");
        var previous = AvApp.IntakePunchCard;
        var service = ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.Instance;
        var field = service.GetType().GetField("_pendingFeedback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var savedFeedback = field.GetValue(service);
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
            field.SetValue(service, savedFeedback);
            AvApp.IntakePunchCard = previous;
            card.Save();
            dir.Delete(true);
        }
    });
}
