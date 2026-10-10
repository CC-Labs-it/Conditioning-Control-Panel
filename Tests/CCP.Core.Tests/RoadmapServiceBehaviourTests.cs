using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Step lifecycle, photo diary, notes, track unlocks and persistence of the roadmap. Uses the
/// internal (progressPath, diaryPath) ctor with a temp dir, never the real profile.
/// </summary>
public sealed class RoadmapServiceBehaviourTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-roadmap-behaviour-").FullName;
    private string ProgressPath => Path.Combine(_dir, "roadmap.json");
    private string DiaryPath => Path.Combine(_dir, "roadmap_diary");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private RoadmapService New() => new(ProgressPath, DiaryPath);

    private string Photo(string name = "shot.png", string content = "pixels")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static void CompleteTrack(RoadmapService roadmap, string prefix, string photo)
    {
        foreach (var id in new[] { "step1", "step2", "step3", "step4", "step5", "step6", "boss" })
            roadmap.SubmitPhoto($"{prefix}_{id}", photo, null);
    }

    [Fact]
    public void FreshServiceStartsOnTrackOneStepOneAndCreatesTheDiary()
    {
        using var roadmap = New();

        Assert.True(Directory.Exists(DiaryPath));
        Assert.Equal(DiaryPath, roadmap.DiaryFolderPath);
        Assert.True(roadmap.IsTrackUnlocked(RoadmapTrack.EmptyDoll));
        Assert.False(roadmap.IsTrackUnlocked(RoadmapTrack.ObedientPuppet));
        Assert.False(roadmap.IsTrackUnlocked(RoadmapTrack.SluttyBlowdoll));
        Assert.True(roadmap.IsStepActive("t1_step1"));
        Assert.False(roadmap.IsStepActive("t1_step2"));
        Assert.False(roadmap.IsStepActive("no_such_step"));
        Assert.False(roadmap.IsStepLocked("t1_step1"));
        Assert.True(roadmap.IsStepLocked("t1_step2"));
        Assert.False(roadmap.IsStepCompleted("t1_step1"));
        Assert.Null(roadmap.GetStepProgress("t1_step1"));
        Assert.Equal((0, 7), roadmap.GetTrackProgress(RoadmapTrack.EmptyDoll));
        Assert.False(File.Exists(ProgressPath)); // nothing dirty, nothing written
    }

    [Fact]
    public void DefaultCtorKeepsTheDiaryBesideTheProfile()
    {
        using var roadmap = new RoadmapService();
        Assert.Equal(Path.Combine(CorePaths.UserData, "roadmap_diary"), roadmap.DiaryFolderPath);
    }

    [Fact]
    public void StartStepRecordsStartAndJourneyStartAndIsSavedOnDispose()
    {
        using (var roadmap = New())
        {
            roadmap.StartStep("t1_step1");
            var progress = roadmap.GetStepProgress("t1_step1");
            Assert.NotNull(progress);
            Assert.NotNull(progress!.StartedAt);
            Assert.False(progress.IsCompleted);
            Assert.NotNull(roadmap.Progress.JourneyStartedAt);
            Assert.False(File.Exists(ProgressPath)); // dirty only; the timer or Dispose saves
        }

        using var reloaded = New();
        Assert.NotNull(reloaded.GetStepProgress("t1_step1")!.StartedAt);
        Assert.NotNull(reloaded.Progress.JourneyStartedAt);
    }

    [Fact]
    public void StartStepTwiceKeepsTheFirstTimes()
    {
        using var roadmap = New();
        roadmap.StartStep("t1_step1");
        var started = new DateTime(2020, 1, 2, 3, 4, 5);
        var journey = new DateTime(2019, 1, 1);
        roadmap.GetStepProgress("t1_step1")!.StartedAt = started;
        roadmap.Progress.JourneyStartedAt = journey;

        roadmap.StartStep("t1_step1");
        roadmap.SubmitPhoto("t1_step1", Photo(), null);
        roadmap.StartStep("t1_step2");

        Assert.Equal(started, roadmap.GetStepProgress("t1_step1")!.StartedAt);
        Assert.NotNull(roadmap.GetStepProgress("t1_step2")!.StartedAt);
        Assert.Equal(journey, roadmap.Progress.JourneyStartedAt);
    }

    [Fact]
    public void StartStepOnALockedStepDoesNothing()
    {
        using (var roadmap = New())
        {
            roadmap.StartStep("t1_step2");
            roadmap.StartStep("no_such_step");
            Assert.Null(roadmap.GetStepProgress("t1_step2"));
            Assert.Null(roadmap.Progress.JourneyStartedAt);
        }
        Assert.False(File.Exists(ProgressPath));
    }

    [Fact]
    public void SubmitPhotoCompletesFilesThePhotoAdvancesAndSavesImmediately()
    {
        using var roadmap = New();
        roadmap.StartStep("t1_step1");
        roadmap.GetStepProgress("t1_step1")!.StartedAt = DateTime.Now.AddMinutes(-10.5);
        var events = new List<RoadmapStepCompletedEventArgs>();
        roadmap.StepCompleted += (_, e) => events.Add(e);

        roadmap.SubmitPhoto("t1_step1", Photo("shot.png", "abc"), "first!");

        var progress = roadmap.GetStepProgress("t1_step1")!;
        Assert.True(progress.IsCompleted);
        Assert.NotNull(progress.CompletedAt);
        Assert.Equal("first!", progress.UserNote);
        Assert.Equal(10, progress.TimeToCompleteMinutes);
        Assert.StartsWith("t1_step1_", progress.PhotoPath);
        Assert.EndsWith(".png", progress.PhotoPath);
        Assert.Equal(Path.Combine(DiaryPath, progress.PhotoPath!), roadmap.GetFullPhotoPath(progress.PhotoPath!));
        Assert.Equal("abc", File.ReadAllText(roadmap.GetFullPhotoPath(progress.PhotoPath!)));

        Assert.Equal(1, roadmap.Progress.TotalStepsCompleted);
        Assert.Equal(1, roadmap.Progress.TotalPhotosSubmitted);
        Assert.True(roadmap.IsStepCompleted("t1_step1"));
        Assert.True(roadmap.IsStepActive("t1_step2"));
        Assert.Equal((1, 7), roadmap.GetTrackProgress(RoadmapTrack.EmptyDoll));

        var e = Assert.Single(events);
        Assert.Equal("t1_step1", e.StepDefinition.Id);
        Assert.Same(progress, e.StepProgress);
        Assert.False(e.UnlockedNewTrack);
        Assert.False(e.EarnedBadge);

        // Saved on completion, not on Dispose.
        using var reloaded = New();
        Assert.True(reloaded.IsStepCompleted("t1_step1"));
        Assert.Equal("first!", reloaded.GetStepProgress("t1_step1")!.UserNote);
        Assert.Equal(progress.PhotoPath, reloaded.GetStepProgress("t1_step1")!.PhotoPath);
        Assert.True(reloaded.IsStepActive("t1_step2"));
        Assert.Equal(1, reloaded.Progress.TotalPhotosSubmitted);
    }

    [Fact]
    public void SubmitPhotoWithoutStartCompletesWithZeroMinutes()
    {
        using var roadmap = New();
        roadmap.SubmitPhoto("t1_step1", Photo(), null);

        var progress = roadmap.GetStepProgress("t1_step1")!;
        Assert.True(progress.IsCompleted);
        Assert.Equal(0, progress.TimeToCompleteMinutes);
        Assert.Null(progress.UserNote);
    }

    [Fact]
    public void SubmitPhotoForANonActiveStepIsRefused()
    {
        using var roadmap = New();
        var fired = 0;
        roadmap.StepCompleted += (_, _) => fired++;

        roadmap.SubmitPhoto("t1_step2", Photo(), "nope");
        roadmap.SubmitPhoto("no_such_step", Photo(), "nope");

        Assert.Equal(0, fired);
        Assert.Null(roadmap.GetStepProgress("t1_step2"));
        Assert.Equal(0, roadmap.Progress.TotalStepsCompleted);
        Assert.Empty(Directory.GetFiles(DiaryPath));
        Assert.False(File.Exists(ProgressPath));
    }

    [Fact]
    public void MissingSourcePhotoStillCompletesWithAnEmptyPhotoPath()
    {
        using var roadmap = New();
        roadmap.SubmitPhoto("t1_step1", Path.Combine(_dir, "gone.png"), null);

        Assert.True(roadmap.IsStepCompleted("t1_step1"));
        Assert.Equal("", roadmap.GetStepProgress("t1_step1")!.PhotoPath);
        Assert.Equal("", roadmap.GetFullPhotoPath(""));
    }

    [Fact]
    public void SavePhotoToDiaryRecreatesADeletedDiaryFolder()
    {
        using var roadmap = New();
        Directory.Delete(DiaryPath);

        var name = roadmap.SavePhotoToDiary("t2_step3", Photo("x.jpg", "j"));

        Assert.StartsWith("t2_step3_", name);
        Assert.EndsWith(".jpg", name);
        Assert.Equal("j", File.ReadAllText(Path.Combine(DiaryPath, name)));
    }

    [Fact]
    public void BossesUnlockTheNextTrackAndTheLastEarnsTheBadge()
    {
        using var roadmap = New();
        var unlocked = new List<RoadmapTrack>();
        var badges = 0;
        var completions = new List<RoadmapStepCompletedEventArgs>();
        roadmap.TrackUnlocked += (_, t) => unlocked.Add(t);
        roadmap.BadgeEarned += (_, _) => badges++;
        roadmap.StepCompleted += (_, e) => completions.Add(e);
        var photo = Photo();

        CompleteTrack(roadmap, "t1", photo);
        Assert.Equal(new[] { RoadmapTrack.ObedientPuppet }, unlocked);
        Assert.True(completions[^1].UnlockedNewTrack);
        Assert.False(completions[^2].UnlockedNewTrack);
        Assert.True(roadmap.IsTrackUnlocked(RoadmapTrack.ObedientPuppet));
        Assert.Null(roadmap.Progress.ActiveTrack1Step); // track finished
        Assert.True(roadmap.IsStepActive("t2_step1"));
        Assert.False(roadmap.IsTrackUnlocked(RoadmapTrack.SluttyBlowdoll));

        CompleteTrack(roadmap, "t2", photo);
        Assert.Equal(new[] { RoadmapTrack.ObedientPuppet, RoadmapTrack.SluttyBlowdoll }, unlocked);
        Assert.True(completions[^1].UnlockedNewTrack);
        Assert.True(roadmap.IsStepActive("t3_step1"));
        Assert.Equal(0, badges);
        Assert.Null(roadmap.Progress.JourneyCompletedAt);

        CompleteTrack(roadmap, "t3", photo);
        Assert.Equal(1, badges);
        Assert.Equal(2, unlocked.Count);
        Assert.True(completions[^1].EarnedBadge);
        Assert.False(completions[^1].UnlockedNewTrack);
        Assert.True(roadmap.Progress.HasCertifiedBlowdollBadge);
        Assert.NotNull(roadmap.Progress.JourneyCompletedAt);
        Assert.Equal(21, roadmap.Progress.TotalStepsCompleted);
        Assert.Equal((7, 7), roadmap.GetTrackProgress(RoadmapTrack.SluttyBlowdoll));

        using var reloaded = New();
        Assert.True(reloaded.Progress.HasCertifiedBlowdollBadge);
        Assert.True(reloaded.IsTrackUnlocked(RoadmapTrack.SluttyBlowdoll));
    }

    [Fact]
    public void BossOfAnAlreadyUnlockedTrackDoesNotUnlockAgain()
    {
        using var roadmap = New();
        roadmap.Progress.Track2Unlocked = true;
        roadmap.Progress.ActiveTrack2Step = "t2_step4";
        roadmap.Progress.ActiveTrack1Step = "t1_boss";
        var unlocked = 0;
        RoadmapStepCompletedEventArgs? last = null;
        roadmap.TrackUnlocked += (_, _) => unlocked++;
        roadmap.StepCompleted += (_, e) => last = e;

        roadmap.SubmitPhoto("t1_boss", Photo(), null);

        Assert.Equal(0, unlocked);
        Assert.False(last!.UnlockedNewTrack);
        Assert.Equal("t2_step4", roadmap.Progress.ActiveTrack2Step); // progress on track 2 kept
    }

    [Fact]
    public void BadgeIsEarnedOnlyOnce()
    {
        using var roadmap = New();
        roadmap.Progress.HasCertifiedBlowdollBadge = true;
        roadmap.Progress.Track3Unlocked = true;
        roadmap.Progress.ActiveTrack3Step = "t3_boss";
        var badges = 0;
        roadmap.BadgeEarned += (_, _) => badges++;

        roadmap.SubmitPhoto("t3_boss", Photo(), null);

        Assert.Equal(0, badges);
        Assert.Null(roadmap.Progress.JourneyCompletedAt);
    }

    [Fact]
    public void UpdateStepNoteSavesAtOnceAndIgnoresUnknownSteps()
    {
        using (var roadmap = New())
        {
            roadmap.UpdateStepNote("t1_step1", "ghost");
            Assert.Null(roadmap.GetStepProgress("t1_step1"));
        }
        Assert.False(File.Exists(ProgressPath));

        using var first = New();
        first.SubmitPhoto("t1_step1", Photo(), "old");
        first.UpdateStepNote("t1_step1", "new");
        Assert.Equal("new", first.GetStepProgress("t1_step1")!.UserNote);

        using var reloaded = New();
        Assert.Equal("new", reloaded.GetStepProgress("t1_step1")!.UserNote);
    }

    [Fact]
    public void SaveCreatesAMissingProfileFolderAndOverwrites()
    {
        var nested = Path.Combine(_dir, "a", "b", "roadmap.json");
        using var roadmap = new RoadmapService(nested, DiaryPath);
        roadmap.Save();
        Assert.True(File.Exists(nested));

        roadmap.Progress.TotalStepsCompleted = 42;
        roadmap.Save();

        using var reloaded = new RoadmapService(nested, DiaryPath);
        Assert.Equal(42, reloaded.Progress.TotalStepsCompleted);
    }

    [Fact]
    public void ExplicitSaveClearsDirtySoDisposeDoesNotRewrite()
    {
        var roadmap = New();
        roadmap.StartStep("t1_step1");
        roadmap.Save();
        File.Delete(ProgressPath);

        roadmap.Dispose();

        Assert.False(File.Exists(ProgressPath));
    }
}
