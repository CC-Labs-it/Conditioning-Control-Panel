using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class IntakeDeskHoldTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void PublishedLineDataContainsTheIntakeHold()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "emi", "desk-lines.json");
        Assert.True(File.Exists(path), "The line engine needs its data beside the app");
        var moment = JObject.Parse(File.ReadAllText(path))["moments"]!["intakeRunning"]!;
        Assert.True((bool)moment["hold"]!);
        Assert.True((bool)moment["holdUntilReleased"]!);
    }

    [Fact]
    public Task NewIntakeOpensTeachTheDeskRingButRefocusingDoesNot() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var state = EmiState.Current;
        var usage = new Dictionary<string, int>(state.Usage);
        var score = new Dictionary<string, double>(state.OpenScore);
        var at = new Dictionary<string, long>(state.UsageAt);
        state.Usage.TryGetValue("intake", out var before);
        try
        {
            var intake = GradedIntakeTabView.OpenIntake(null, duckMain: false);
            Assert.Equal(before + 1, state.Usage["intake"]);
            Assert.True(state.OpenScore["intake"] > 0);
            Assert.True(state.UsageAt["intake"] > 0);
            var firstScore = state.OpenScore["intake"];
            Assert.Same(intake, GradedIntakeTabView.OpenIntake(null, duckMain: false));
            Assert.Equal(before + 1, state.Usage["intake"]);
            Assert.Equal(firstScore, state.OpenScore["intake"]);
            intake.Close();
            GradedIntakeTabView.OpenIntake(null, duckMain: false).Close();
            Assert.Equal(before + 2, state.Usage["intake"]);
            Assert.True(state.OpenScore["intake"] > firstScore);
        }
        finally
        {
            IntakeHostWindow.CloseAllForPanic();
            state.Usage.Clear(); foreach (var item in usage) state.Usage[item.Key] = item.Value;
            state.OpenScore.Clear(); foreach (var item in score) state.OpenScore[item.Key] = item.Value;
            state.UsageAt.Clear(); foreach (var item in at) state.UsageAt[item.Key] = item.Value;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task IntakeMutesDeskCuesUntilItCloses() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var engine = EmiLineEngine.Instance;
        var previous = CoreAudio.PlayOneShotProvider;
        int volume = CoreSettings.Current.MasterVolume;
        var clock = new SteppedClock();
        var played = new List<string>();
        try
        {
            engine.ReleaseHold("intakeRunning");
            CoreSettings.Current.MasterVolume = 50;
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => played.Add(tag);
            EmiSfx.Clock = clock;
            EmiSfx.Pat();
            Assert.Equal(new[] { "emi-sfx-pat" }, played);

            var intake = GradedIntakeTabView.OpenIntake(null, duckMain: false);
            Assert.Equal("intakeRunning", engine.HeldBy);
            Assert.Same(intake, GradedIntakeTabView.OpenIntake(null, duckMain: false));
            clock.Now += TimeSpan.FromSeconds(1);
            EmiSfx.Pat();
            Assert.Single(played);

            intake.Close();
            Assert.Null(engine.HeldBy);
            clock.Now += TimeSpan.FromSeconds(1);
            EmiSfx.Pat();
            Assert.Equal(2, played.Count);
        }
        finally
        {
            IntakeHostWindow.CloseAllForPanic();
            engine.ReleaseHold("intakeRunning");
            CoreAudio.PlayOneShotProvider = previous;
            CoreSettings.Current.MasterVolume = volume;
            EmiSfx.Clock = TimeProvider.System;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicAndHeartbeatRecoveryReleaseAndRearmTheHold() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var engine = EmiLineEngine.Instance;
        try
        {
            engine.ReleaseHold("intakeRunning");
            IntakeHostWindow.RelaunchedOnce = false;
            GradedIntakeTabView.OpenIntake(null, duckMain: false);
            Assert.Equal("intakeRunning", engine.HeldBy);
            IntakeHostWindow.CloseAllForPanic();
            Assert.Null(engine.HeldBy);

            var first = GradedIntakeTabView.OpenIntake(null, duckMain: false);
            var clock = new SteppedClock();
            first.Clock = clock;
            first.HandleMessage("{\"type\":\"ready\"}");
            clock.Now += TimeSpan.FromSeconds(21);
            first.CheckHeartbeat();
            var retry = Assert.Single(IntakeHostWindow.Snapshot());
            Assert.NotSame(first, retry);
            Assert.Equal("intakeRunning", engine.HeldBy);
            retry.HandleMessage("{\"type\":\"intake-close\"}");
            Assert.Null(engine.HeldBy);
        }
        finally
        {
            IntakeHostWindow.CloseAllForPanic();
            engine.ReleaseHold("intakeRunning");
            IntakeHostWindow.RelaunchedOnce = false;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PresentationKeepsTheWpfMomentGate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var engine = EmiLineEngine.Instance;
        var desk = EmiDeskService.Instance.BeginPresentation();
        Assert.NotNull(desk);
        try
        {
            engine.ReleaseHold("intakeRunning");
            desk.BeginPresentation(() => { });
            var intake = GradedIntakeTabView.OpenIntake(null, duckMain: false);
            Assert.Null(engine.HeldBy);
            intake.Close();

            desk.EndPresentation();
            intake = GradedIntakeTabView.OpenIntake(null, duckMain: false);
            Assert.Equal("intakeRunning", engine.HeldBy);
            intake.Close();
            Assert.Null(engine.HeldBy);
        }
        finally
        {
            IntakeHostWindow.CloseAllForPanic();
            engine.ReleaseHold("intakeRunning");
            desk.EndPresentation();
            desk.ShutDown();
        }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow.AddDays(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
