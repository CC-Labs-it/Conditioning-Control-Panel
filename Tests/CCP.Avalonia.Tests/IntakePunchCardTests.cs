using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using AvApp = ConditioningControlPanel.Avalonia.App;
using System.Text.Json;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class IntakePunchCardTests
{
    [Fact]
    public void DraftsAndRedemptionsSurviveReloadAndStopAtEight()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            IntakePunchCardState Read() => JsonSerializer.Deserialize<IntakePunchCardState>(File.ReadAllText(Path.Combine(dir, IntakePunchCardState.FileName)))!;
            Assert.False(IntakePunchCardState.ReadEverCompletedIntake(dir));
            IntakePunchCardHead.Draft(dir, "a");
            IntakePunchCardHead.Draft(dir, "a");
            Assert.True(IntakePunchCardState.ReadEverCompletedIntake(dir));
            Assert.Single(Read().PendingDrafts);
            IntakePunchCardHead.Progress(dir, "a", 49.9);
            Assert.Equal(1, Read().PunchedCount);
            IntakePunchCardHead.Progress(dir, "a", 50);
            Assert.Equal(2, Read().PunchedCount);
            IntakePunchCardHead.Progress(dir, "a", 100, true);
            Assert.Equal(2, Read().PunchedCount);
            Assert.Empty(Read().PendingDrafts);
            for (var i = 0; i < 8; i++)
            {
                var id = i.ToString();
                IntakePunchCardHead.Draft(dir, id);
                IntakePunchCardHead.Progress(dir, id, 0, true);
            }
            Assert.Equal(8, Read().PunchedCount);
            Assert.NotNull(Read().CompletedUtc);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public void LoadRepairsExpiredAndMalformedDraftsAndFullCard()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, IntakePunchCardState.FileName);
        try
        {
            var now = DateTime.UtcNow;
            var state = new IntakePunchCardState { CardStartedUtc = now, PunchedCount = 1 };
            state.PendingDrafts.Add(new PendingIntakeDraft { SessionId = "expired", DraftedUtc = now.AddDays(-91) });
            state.PendingDrafts.Add(new PendingIntakeDraft { SessionId = "", DraftedUtc = now });
            for (var i = 0; i < 9; i++)
                state.PendingDrafts.Add(new PendingIntakeDraft { SessionId = "live" + i, DraftedUtc = now });
            var mixed = JsonSerializer.Serialize(state);
            state.PendingDrafts.RemoveAll(d => d.SessionId != "expired");
            File.WriteAllText(path, JsonSerializer.Serialize(state));
            Assert.False(IntakePunchCardHead.HasEverCompleted(dir));
            Assert.Empty(JsonSerializer.Deserialize<IntakePunchCardState>(File.ReadAllText(path))!.PendingDrafts);
            File.WriteAllText(path, mixed);
            IntakePunchCardHead.Progress(dir, "expired", 100);
            var loaded = JsonSerializer.Deserialize<IntakePunchCardState>(File.ReadAllText(path))!;
            Assert.Equal(1, loaded.PunchedCount);
            Assert.Equal(8, loaded.PendingDrafts.Count);
            Assert.DoesNotContain(loaded.PendingDrafts, d => d.SessionId == "expired" || d.SessionId == "" || d.SessionId == "live0");
            Assert.Contains(loaded.PendingDrafts, d => d.SessionId == "live8");

            state.PunchedCount = 12;
            state.PunchedUtc.Add(now.AddDays(-1));
            state.PendingDrafts.Clear();
            File.WriteAllText(path, JsonSerializer.Serialize(state));
            IntakePunchCardHead.Progress(dir, "missing", 100);
            loaded = JsonSerializer.Deserialize<IntakePunchCardState>(File.ReadAllText(path))!;
            Assert.Equal(8, loaded.PunchedCount);
            Assert.Equal(state.PunchedUtc[0], loaded.CompletedUtc);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadRepairsNullCollectionsAndNegativeCount()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, IntakePunchCardState.FileName);
        try
        {
            File.WriteAllText(path, "{\"CardStartedUtc\":\"2026-01-01T00:00:00Z\",\"PunchedCount\":-3,\"PunchedUtc\":null,\"PendingDrafts\":null}");
            Assert.False(IntakePunchCardHead.HasEverCompleted(dir));
            var state = JsonSerializer.Deserialize<IntakePunchCardState>(File.ReadAllText(path))!;
            Assert.Equal(0, state.PunchedCount);
            Assert.Empty(state.PendingDrafts);
            Assert.Empty(state.PunchedUtc);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task RunnerEventsRedeemAtHalfAndCompletionButNotEarlyStop(bool tick) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        var previousRunner = AvApp.Sessions;
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        var path = Path.Combine(CorePaths.UserData, IntakePunchCardState.FileName);
        IntakePunchCardState Read() => JsonSerializer.Deserialize<IntakePunchCardState>(File.ReadAllText(path))!;
        Session NewSession(string id) => new() { Id = id, Name = id, DurationMinutes = 1 };
        try
        {
            if (File.Exists(path)) File.Delete(path);
            AvApp.AttachSessionEvents(runner, shell);
            if (tick)
            {
                IntakePunchCardHead.Draft(CorePaths.UserData, "tick");
                runner.Start(NewSession("tick"));
                runner.Tick(TimeSpan.FromSeconds(29));
                Assert.Equal(1, Read().PunchedCount);
                runner.Pause();
                runner._pausedElapsed = TimeSpan.FromSeconds(29.9);
                runner.Resume();
                Thread.Sleep(250); // let the real elapsed clock cross 30 seconds
                runner.Tick(runner.Elapsed);
                Assert.Equal(2, Read().PunchedCount);
                runner.Stop();
            }
            else
            {
                IntakePunchCardHead.Draft(CorePaths.UserData, "early");
                runner.Start(NewSession("early"));
                runner.Stop(completed: false);
                Assert.Equal(1, Read().PunchedCount);
                Assert.Contains(Read().PendingDrafts, d => d.SessionId == "early");

                IntakePunchCardHead.Draft(CorePaths.UserData, "finish");
                runner.Start(NewSession("finish"));
                runner.Tick(TimeSpan.FromMinutes(1)); // natural end before a halfway tick
                Assert.False(runner.IsRunning);
                Assert.Equal(2, Read().PunchedCount);
                Assert.DoesNotContain(Read().PendingDrafts, d => d.SessionId == "finish");
                Assert.Contains(Read().PendingDrafts, d => d.SessionId == "early");
            }
        }
        finally
        {
            runner.Stop();
            AvApp.DetachSessionEvents(runner, shell);
            AvApp.Sessions = previousRunner;
            CoreEngine.Stop();
            CoreSession.IsSessionRunningProvider = null;
            shell.Close();
        }
        return Task.CompletedTask;
    });

}
