using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF GamificationBridge.cs:114-136,299-358: restore historical chat credit once,
/// using the actual saved conversation and relationship records, without a second celebration.</summary>
public sealed class CompanionChatBackfillTests
{
    [Fact]
    public void RestoredUserTurnsSetFloorSilentlyAndOnlyOnce()
    {
        using var f = new Fixture();
        f.SaveTurns(TurnKind.UserChat, TurnKind.AssistantChat, TurnKind.UserChat, TurnKind.AssistantChat);
        f.SaveRelationships(("one", 1));
        using var brain = f.OpenBrain();
        brain.Session.Append(CompanionTurn.Create(TurnKind.BarkEcho, "not a user message"));
        var engine = f.Engine();
        var popups = 0;
        engine.Unlocked += (_, _) => popups++;

        AvApp.BackfillCompanionChatCount(engine, brain);

        Assert.Equal(2, engine.Progress.CompanionMessages);
        Assert.True(engine.Progress.CompanionChatBackfilled);
        Assert.True(engine.Progress.IsUnlocked("pleased_to_meet_you"));
        Assert.False(engine.Progress.IsUnlocked("pillow_talk"));
        Assert.Equal(0, popups);
        Assert.False(engine.SuppressPopups);
        var saved = f.Engine().Progress;
        Assert.True(saved.CompanionChatBackfilled);
        Assert.Equal(2, saved.CompanionMessages);
        Assert.True(saved.IsUnlocked("pleased_to_meet_you"));

        brain.Session.Append(CompanionTurn.Create(TurnKind.UserChat, "later"));
        AvApp.BackfillCompanionChatCount(engine, brain);
        Assert.Equal(2, engine.Progress.CompanionMessages);
    }

    [Theory]
    [InlineData(0, 105, false)]
    [InlineData(150, 150, true)]
    public void RelationshipSumOrExistingHigherCountWins(int current, int expected, bool alreadySuppressed)
    {
        using var f = new Fixture();
        f.SaveTurns(TurnKind.UserChat, TurnKind.AssistantChat);
        f.SaveRelationships(("one", 60), ("two", 45));
        using var brain = f.OpenBrain();
        var engine = f.Engine();
        engine.Progress.CompanionMessages = current;
        engine.SuppressPopups = alreadySuppressed;
        var popups = 0;
        engine.Unlocked += (_, _) => popups++;

        AvApp.BackfillCompanionChatCount(engine, brain);

        Assert.Equal(expected, engine.Progress.CompanionMessages);
        Assert.True(engine.Progress.IsUnlocked("pleased_to_meet_you"));
        Assert.True(engine.Progress.IsUnlocked("pillow_talk"));
        Assert.Equal(0, popups);
        Assert.Equal(alreadySuppressed, engine.SuppressPopups);
        Assert.Equal(expected, f.Engine().Progress.CompanionMessages);
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    public void ExistingCountAwardsPillowTalkAtTheWpfThreshold(int count, bool unlocked)
    {
        using var f = new Fixture();
        using var brain = f.OpenBrain();
        var engine = f.Engine();
        engine.Progress.CompanionMessages = count;

        AvApp.BackfillCompanionChatCount(engine, brain);

        Assert.Equal(count, engine.Progress.CompanionMessages);
        Assert.Equal(unlocked, engine.Progress.IsUnlocked("pillow_talk"));
    }

    [Fact]
    public void MissingBrainLeavesLatchForRetryAndEmptyEvidenceStillPersists()
    {
        using var f = new Fixture();
        var engine = f.Engine();
        AvApp.BackfillCompanionChatCount(engine, null);
        Assert.False(engine.Progress.CompanionChatBackfilled);
        Assert.False(engine.IsDirty);

        using var brain = f.OpenBrain();
        AvApp.BackfillCompanionChatCount(engine, brain);
        Assert.True(engine.Progress.CompanionChatBackfilled);
        Assert.Equal(0, engine.Progress.CompanionMessages);
        Assert.False(engine.Progress.IsUnlocked("pleased_to_meet_you"));
        Assert.True(engine.IsDirty);
        engine.Save(); // the app flushes dirty achievement state on exit
        Assert.True(f.Engine().Progress.CompanionChatBackfilled);
    }

    [Fact]
    public void EmptyStartupBackfillWaitsForRealProgressBeforeSaving()
    {
        using var f = new Fixture();
        using var brain = f.OpenBrain();
        var engine = f.Engine();
        var wasDirty = engine.IsDirty;
        AvApp.BackfillCompanionChatCount(engine, brain);
        AvApp.DeferEmptyBackfillSave(engine, wasDirty);
        AvApp.SaveNonEmptyChatBackfill(engine, wasBackfilled: false);

        Assert.True(engine.Progress.CompanionChatBackfilled);
        Assert.False(engine.IsDirty);
        if (engine.IsDirty) engine.Save(); // the desktop exit path
        Assert.False(f.HasAchievementFile);

        engine.TrackFlashImage();
        Assert.True(engine.IsDirty);
        engine.Save();
        var saved = f.Engine().Progress;
        Assert.Equal(1, saved.TotalFlashImages);
        Assert.True(saved.CompanionChatBackfilled);
    }

    [Fact]
    public void EmptyBackfillDoesNotClearEarlierDirtyProgress()
    {
        using var f = new Fixture();
        using var brain = f.OpenBrain();
        var engine = f.Engine();
        engine.TrackFlashImage();
        var wasDirty = engine.IsDirty;
        AvApp.BackfillCompanionChatCount(engine, brain);
        AvApp.DeferEmptyBackfillSave(engine, wasDirty);

        Assert.True(engine.IsDirty);
        engine.Save();
        var saved = f.Engine().Progress;
        Assert.Equal(1, saved.TotalFlashImages);
        Assert.True(saved.CompanionChatBackfilled);
    }

    [Fact]
    public void RecoveredChatCountRemainsDirtyWhenItsAchievementWasAlreadyUnlocked()
    {
        using var f = new Fixture();
        f.SaveTurns(TurnKind.UserChat);
        using var brain = f.OpenBrain();
        var engine = f.Engine();
        engine.Progress.Unlock("pleased_to_meet_you");
        var wasDirty = engine.IsDirty;
        AvApp.BackfillCompanionChatCount(engine, brain);
        AvApp.DeferEmptyBackfillSave(engine, wasDirty);

        Assert.Equal(1, engine.Progress.CompanionMessages);
        Assert.True(engine.IsDirty);
        engine.Save();
        Assert.Equal(1, f.Engine().Progress.CompanionMessages);
    }

    [Fact]
    public void StartupSavesRecoveredCountBeforeChatHistoryIsCleared()
    {
        using var f = new Fixture();
        f.SaveTurns(TurnKind.UserChat, TurnKind.AssistantChat);
        Assert.True(f.Engine().TryUnlock("pleased_to_meet_you"));
        var engine = f.Engine(); // first-chat achievement already persisted, count still zero
        using var brain = f.OpenBrain();
        var wasBackfilled = engine.Progress.CompanionChatBackfilled;
        var wasDirty = engine.IsDirty;

        AvApp.BackfillCompanionChatCount(engine, brain);
        AvApp.DeferEmptyBackfillSave(engine, wasDirty);
        Assert.True(engine.IsDirty);
        Assert.Equal(0, f.Engine().Progress.CompanionMessages);
        AvApp.SaveNonEmptyChatBackfill(engine, wasBackfilled);

        Assert.False(engine.IsDirty);
        brain.Session.Clear();
        f.ForgetSavedTurns();
        var saved = f.Engine().Progress; // read from disk before any desktop exit
        Assert.Equal(1, saved.CompanionMessages);
        Assert.True(saved.CompanionChatBackfilled);
        Assert.True(saved.IsUnlocked("pleased_to_meet_you"));
    }

    [Fact]
    public void FailedStartupBackfillSaveStaysDirtyForTheExitRetry()
    {
        using var f = new Fixture();
        f.SaveTurns(TurnKind.UserChat);
        using var brain = f.OpenBrain();
        var blocker = f.PathInProfile("blocked");
        File.WriteAllText(blocker, "block directory creation");
        var path = Path.Combine(blocker, "achievements.json");
        var engine = new AchievementEngine(new AchievementStore(path));
        engine.Progress.Unlock("pleased_to_meet_you");
        var wasBackfilled = engine.Progress.CompanionChatBackfilled;
        var wasDirty = engine.IsDirty;

        AvApp.BackfillCompanionChatCount(engine, brain);
        AvApp.DeferEmptyBackfillSave(engine, wasDirty);
        AvApp.SaveNonEmptyChatBackfill(engine, wasBackfilled);
        Assert.True(engine.IsDirty);
        Assert.False(File.Exists(path));

        File.Delete(blocker);
        if (engine.IsDirty) engine.Save(); // the existing desktop exit retry
        Assert.False(engine.IsDirty);
        var saved = new AchievementStore(path).Load();
        Assert.Equal(1, saved.CompanionMessages);
        Assert.True(saved.CompanionChatBackfilled);
    }

    [Fact]
    public void PreviouslyBackfilledProgressIsNotWrittenByTheStartupSave()
    {
        using var f = new Fixture();
        var engine = f.Engine();
        engine.Progress.CompanionChatBackfilled = true;
        engine.Progress.CompanionMessages = 1;
        engine.TrackFlashImage();

        AvApp.SaveNonEmptyChatBackfill(engine, wasBackfilled: true);

        Assert.True(engine.IsDirty);
        Assert.False(f.HasAchievementFile);
    }

    [Fact]
    public void DesktopStartupCallsBackfillAfterBrainAndAchievementsAreReady()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "CCP.Avalonia", "App.axaml.cs"));
        var brain = source.IndexOf("Brain = new ConditioningControlPanel.Services.Companion.Brain.CompanionBrain(Ai)", StringComparison.Ordinal);
        var engine = source.IndexOf("Achievements = new AchievementEngine(", StringComparison.Ordinal);
        var call = source.IndexOf("BackfillCompanionChatCount(Achievements, Brain);", StringComparison.Ordinal);
        var save = source.IndexOf("SaveNonEmptyChatBackfill(Achievements, chatBackfillWasComplete);", StringComparison.Ordinal);
        // The one autosave timer (WPF AchievementService.cs:98-105) and the exit save retry a failed write.
        var autosave = source.IndexOf("StartAchievementAutosave(Achievements, CheckSeasonActivity);", StringComparison.Ordinal);
        var flush = source.IndexOf("SaveAchievementsOnExit(_achievementSaveTimer, Achievements);", StringComparison.Ordinal);
        Assert.True(brain >= 0 && engine >= 0 && call > brain && call > engine);
        Assert.True(save > call);
        Assert.True(autosave >= 0 && autosave < call && flush > save);
    }

    private static string Here([CallerFilePath] string path = "") => path;

    private sealed class Fixture : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("ccp-chat-backfill-").FullName;
        private readonly Func<SettingsService?>? _oldSettings = CoreSettings.ServiceProvider;
        private readonly AiService _ai = new("http://127.0.0.1:9");
        private MemoryStore? _memory;
        private string MemoryPath => Path.Combine(_dir, "memory.json");
        private CompanionSessionStore Store() => new(Path.Combine(_dir, "session.json"), Path.Combine(_dir, "legacy.json"));

        internal Fixture()
        {
            var settings = new SettingsService();
            settings.Current.CompanionPrompt.ChatMemoryEnabled = true;
            CoreSettings.ServiceProvider = () => settings;
        }

        internal bool HasAchievementFile => File.Exists(Path.Combine(_dir, "achievements.json"));
        internal string PathInProfile(string name) => Path.Combine(_dir, name);
        internal void ForgetSavedTurns() => Store().Wipe();
        internal AchievementEngine Engine() => new(new AchievementStore(Path.Combine(_dir, "achievements.json")));

        internal void SaveTurns(params TurnKind[] kinds) => Store().Save(
            kinds.Select((kind, i) => CompanionTurn.Create(kind, "turn " + i)).ToArray());

        internal void SaveRelationships(params (string Mod, int Count)[] counts)
        {
            using var memory = new MemoryStore(MemoryPath, chatMemoryEnabled: () => true);
            foreach (var (mod, count) in counts)
                for (var i = 0; i < count; i++) memory.NoteChatTurn(mod);
            memory.SaveNow();
        }

        internal CompanionBrain OpenBrain()
        {
            _memory = new MemoryStore(MemoryPath, chatMemoryEnabled: () => true);
            return new CompanionBrain(_ai, memory: _memory, store: Store());
        }

        public void Dispose()
        {
            _memory?.Dispose();
            _ai.Dispose();
            CoreSettings.ServiceProvider = _oldSettings;
            Directory.Delete(_dir, true);
        }
    }
}
