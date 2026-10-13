using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Down the Rabbit Hole host rules both heads share (WPF DtrhHostService, Avalonia GameWindow.Dtrh):
/// the run-ended booking and its XP, the hub's run setup in and out, the per-descent deal and the manifest frame.</summary>
[Collection(CCP.Core.Tests.SessionStatics.Name)]   // Deal / RunConfig read the process-wide ChaosMeta
public class DtrhRunWireTests
{
    [Fact]
    public void Book_CapsXpAt250AMinuteTimesDifficulty_AndClampsThePageFigures()
    {
        var b = DtrhRunWire.Book(JObject.Parse("{\"score\":100000,\"durationSec\":120,\"difficultyMult\":9,\"sparkGainMult\":0.1,\"difficulty\":\"Hard\"}"), null);
        Assert.Equal(5.0, b.DiffMult);                    // clamped 0.5..5
        Assert.Equal(0.5, b.SparkGainMult);
        Assert.Equal(120, b.DurationSec);
        Assert.Equal(250.0 * 2 * 5, b.BaseXp);            // the cap, not the score
        Assert.Equal("Hard", b.Difficulty);
        Assert.True(b.Payout.CountsAsRun);

        var small = DtrhRunWire.Book(JObject.Parse("{\"score\":300,\"durationSec\":180}"), null);
        Assert.Equal(300, small.BaseXp);                  // under the cap: the score
        Assert.Equal(1.0, small.DiffMult);
        Assert.Equal("Gentle", small.Difficulty);
        Assert.Equal(180, small.ElapsedSec);              // absent elapsed reads as the configured length
    }

    [Fact]
    public void Book_ALeftDescentIsPaidForTheSecondsTheHostSawFall()
    {
        // The page claims 600 s of an endless 720 s descent; the host's clock saw 30.
        var b = DtrhRunWire.Book(JObject.Parse("{\"score\":100000,\"durationSec\":720,\"elapsedSec\":600,\"abandoned\":true}"), hostElapsedSec: 30);
        Assert.Equal(30 + DtrhRunPayoutRule.HostClockSlackSec, b.DurationSec);
        Assert.False(b.Payout.CountsAsRun);               // under a minute
        Assert.Equal(250.0 * (b.DurationSec / 60.0), b.BaseXp, 6);
        var input = b.RewardInput(JObject.Parse("{\"bestCombo\":7,\"defused\":3,\"trickleDrops\":2.5,\"dripFeedMaxed\":true}"));
        Assert.Equal(b.DurationSec, input.RunDurationSec);
        Assert.Equal(600, input.ElapsedSec);
        Assert.Equal((7, 3, 2.5, true), (input.BestCombo, input.Defused, input.TrickleDrops, input.DripFeedMaxed));
    }

    [Fact]
    public void PersistRunSetup_ClampsToTheUnlocks_AndLeavesAbsentFieldsAlone()
    {
        bool hadDuration = ChaosMeta.State.PurchasedUpgrades.Remove("custom_duration");
        bool hadEndless = ChaosMeta.State.PurchasedUpgrades.Remove("endless_mode");
        try
        {
            var s = new AppSettings { ChaosAccessoryKey1 = "Z", ChaosWaveCount = 4 };
            DtrhRunWire.PersistRunSetup(JObject.Parse("{\"durationSec\":7200,\"endless\":true,\"effectIntensity\":9,\"enabledVariants\":[\"flash\"],\"difficulty\":\"Hard\"}"), s);
            Assert.Equal(1200, s.ChaosRunDurationSec);    // no Hourglass: the preset ceiling
            Assert.False(s.ChaosEndless);                 // no Bottomless Fall: the toggle does not stick
            Assert.Equal(1.5, s.ChaosEffectIntensity);
            Assert.Equal(new[] { "flash" }, s.ChaosEnabledVariants);
            Assert.Equal("Hard", s.ChaosDifficulty);
            Assert.Equal("Z", s.ChaosAccessoryKey1);      // absent: untouched
            Assert.Equal(4, s.ChaosWaveCount);

            ChaosMeta.State.PurchasedUpgrades.Add("custom_duration");
            ChaosMeta.State.PurchasedUpgrades.Add("endless_mode");
            DtrhRunWire.PersistRunSetup(JObject.Parse("{\"durationSec\":7200,\"endless\":true,\"enabledVariants\":null,\"waveCount\":99}"), s);
            Assert.Equal(7200, s.ChaosRunDurationSec);
            Assert.True(s.ChaosEndless);
            Assert.Null(s.ChaosEnabledVariants);
            Assert.Equal(12, s.ChaosWaveCount);
        }
        finally
        {
            ChaosMeta.State.PurchasedUpgrades.Remove("custom_duration");
            ChaosMeta.State.PurchasedUpgrades.Remove("endless_mode");
            if (hadDuration) ChaosMeta.State.PurchasedUpgrades.Add("custom_duration");
            if (hadEndless) ChaosMeta.State.PurchasedUpgrades.Add("endless_mode");
        }
    }

    [Fact]
    public void RunSetup_CarriesTheSavedChoices()
    {
        var setup = JObject.FromObject(DtrhRunWire.RunSetup(new AppSettings { ChaosDifficulty = "Hard", ChaosAccessoryKey2 = "R" }));
        Assert.Equal("Hard", (string?)setup["difficulty"]);
        Assert.Equal("R", (string?)setup["key2"]);
        Assert.Equal("Easy", (string?)JObject.FromObject(DtrhRunWire.RunSetup(null))["difficulty"]);
    }

    [Fact]
    public void Deal_TheFirstDescentIsTheScriptedClassroom_ThenTheSavedSetup()
    {
        int runs = ChaosMeta.State.RunsCompleted;
        bool force = ChaosMeta.State.ForceScriptedRun;
        try
        {
            ChaosMeta.State.ForceScriptedRun = false;
            ChaosMeta.State.RunsCompleted = 0;
            var (first, scripted, spent) = DtrhRunWire.Deal(testMode: false);
            Assert.True(scripted);
            Assert.False(spent);
            Assert.True(first.ScriptedFirstRun);
            Assert.Equal(DtrhRunWire.FirstRunSpawnRateMult, first.SpawnRateMult);
            Assert.Equal(new[] { "flash", "subliminal" }, first.EnabledVariants);
            var wire = JObject.FromObject(DtrhRunWire.RunConfig(first, new AppSettings()));
            Assert.True((bool)wire["scriptedFirstRun"]!);
            Assert.Equal(0, (int)wire["runsCompleted"]!);

            Assert.False(DtrhRunWire.Deal(testMode: true).Scripted);   // the test door never deals the classroom
            ChaosMeta.State.RunsCompleted = 3;
            var (later, scriptedLater, _) = DtrhRunWire.Deal(testMode: false);
            Assert.False(scriptedLater);
            Assert.False(later.ScriptedFirstRun);
        }
        finally
        {
            ChaosMeta.State.RunsCompleted = runs;
            ChaosMeta.State.ForceScriptedRun = force;
        }
    }

    [Fact]
    public void Manifest_ActiveLibraryKeepsVideos_AndTheFrameIsHostMediaShaped()
    {
        var root = Directory.CreateTempSubdirectory("dtrh-manifest-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "images"));
            Directory.CreateDirectory(Path.Combine(root, "videos"));
            File.WriteAllBytes(Path.Combine(root, "images", "a b.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(root, "videos", "clip.webm"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(root, "videos", "old.wmv"), new byte[] { 1 });   // the browser cannot play it
            var frame = JObject.FromObject(GameMediaManifest.Frame(GameMediaManifest.Build(root, rel => "u/" + rel, null)));
            Assert.Equal("manifest", (string?)frame["type"]);
            Assert.Equal("a b.png", (string?)frame["images"]!.Single()["name"]);
            Assert.Equal("u/images/a b.png", (string?)frame["images"]!.Single()["url"]);
            Assert.Equal("u/videos/clip.webm", (string?)frame["videos"]!.Single()["url"]);
            Assert.Equal(1, (int)frame["skipped"]!);
            Assert.False((bool)frame["truncated"]!);
        }
        finally { Directory.Delete(root, true); }
    }
}
