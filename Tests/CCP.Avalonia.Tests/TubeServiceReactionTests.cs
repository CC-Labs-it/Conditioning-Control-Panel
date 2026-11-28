using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class TubeServiceReactionTests
{
    private sealed class VideoHost : IMandatoryVideoHost
    {
        public int FloatingLimit => 1;
        public int FloatingUp => 0;
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    private sealed class GameHost : IBubbleCountHost
    {
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) { }
        public void ShowMessage(string text, int ms, Action then) { }
        public void CloseAll() { }
        public double LastVideoDurationSeconds => 60;
    }

    [Fact]
    public Task GameResultsReactAndPanicRetiresPostedReaction() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var game = new BubbleCountScheduler(new GameHost());
        var lines = new List<string>();
        var phrase = new CustomCompanionPhrase { Id = "test-game-failed", Category = "GameFailed", Text = "Try again" };
        CoreSettings.Current.CustomCompanionPhrases.Add(phrase);
        var tube = new AvatarTubeWindow(null) { GameReactionSink = lines.Add };
        try
        {
            tube.Show();
            tube.AttachServiceReactions(null, game: game);
            Action Handler(string name) => (Action)typeof(BubbleCountScheduler)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
            Handler("GameCompleted")();
            Assert.Equal(Loc.Get("avatar_bubble_count_win"), Assert.Single(lines));
            Handler("GameFailed")();
            Assert.Equal(2, lines.Count);
            Task.Run(Handler("GameCompleted")).GetAwaiter().GetResult();
            game.ForceCleanup();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, lines.Count);
            AvatarTubeWindow.TestLive = tube;
            Task.Run(Handler("GameCompleted")).GetAwaiter().GetResult();
            PanicSurfaces.All.Single(x => x.Id == "tube-speech").Stop(null!);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, lines.Count);
            tube.Close();
            Assert.Null(typeof(BubbleCountScheduler).GetField("GameCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game));
            Assert.Null(typeof(BubbleCountScheduler).GetField("GameFailed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game));
        }
        finally { AvatarTubeWindow.TestLive = null; tube.Close(); CoreSettings.Current.CustomCompanionPhrases.Remove(phrase); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task GameReactionStopsWithEngineCleanup(bool queued) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var game = new BubbleCountScheduler(new GameHost());
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.AttachServiceReactions(null, game: game);
            if (queued) tube.Giggle("ordinary line");
            var completed = (Action)typeof(BubbleCountScheduler).GetField("GameCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
            completed();
            Assert.True(tube.HasBubbleUp);
            Assert.Equal(queued ? 1 : 0, tube.QueuedSpeechCount);
            game.ForceCleanup();
            Assert.Equal(0, tube.QueuedSpeechCount);
            Assert.Equal(queued, tube.HasBubbleUp);
        }
        finally { tube.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task VideoAnnouncementShowsAndStopClearsIt(bool panic) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var freeze = CoreSubliminal.BambiFreezeProvider;
        CoreSubliminal.BambiFreezeProvider = null;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/video.mp4" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.AttachServiceReactions(null, video);
            AvatarTubeWindow.TestLive = tube;
            Assert.True(video.Trigger(path: "/video.mp4"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(tube.HasBubbleUp);
            if (panic) PanicSurfaces.All.Single(x => x.Id == "tube-speech").Stop(null!);
            else video.ForceCleanup();
            Assert.False(tube.HasBubbleUp);
            video.ForceCleanup();
            Dispatcher.UIThread.RunJobs();
            Assert.False(tube.HasBubbleUp);
            tube.Close();
            Assert.True(video.Trigger(path: "/video.mp4"));
            Dispatcher.UIThread.RunJobs();
            Assert.False(tube.HasBubbleUp);
        }
        finally { video.ForceCleanup(); tube.Close(); AvatarTubeWindow.TestLive = null; CoreSubliminal.BambiFreezeProvider = freeze; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NaturalEndKeepsAnnouncementUntilForcedStop(bool queued) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var freeze = CoreSubliminal.BambiFreezeProvider;
        var attention = CoreSettings.Current.AttentionChecksEnabled;
        CoreSubliminal.BambiFreezeProvider = null;
        CoreSettings.Current.AttentionChecksEnabled = false;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/video.mp4" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.AttachServiceReactions(null, video);
            if (queued) tube.Giggle("ordinary line");
            Assert.True(video.Trigger(path: "/video.mp4"));
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            Assert.Equal(queued ? 1 : 0, tube.QueuedSpeechCount);
            video.Ended();
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            Assert.Equal(queued ? 1 : 0, tube.QueuedSpeechCount);
            video.ForceCleanup(); // also cancels an announcement after the video has ended
            Assert.Equal(0, tube.QueuedSpeechCount);
            Assert.Equal(queued, tube.FindControl<Border>("SpeechBubble")!.IsVisible);
        }
        finally { video.ForceCleanup(); tube.Close(); CoreSubliminal.BambiFreezeProvider = freeze; CoreSettings.Current.AttentionChecksEnabled = attention; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task QueuedVideoAnnouncementDropsEventAudio() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var freeze = CoreSubliminal.BambiFreezeProvider;
        var oldMods = (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider);
        var s = CoreSettings.Current;
        var audioSettings = (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted);
        CoreSubliminal.BambiFreezeProvider = null;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/video.mp4" });
        var tube = new AvatarTubeWindow(null);
        var root = Path.Combine(Path.GetTempPath(), "ccp-video-queue-" + Guid.NewGuid());
        var folder = Path.Combine(root, "resources", "sounds", "event_audio");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "ooh pretty spir rals.mp3"), "audio");
        Action? pending = null;
        string? played = null;
        var calls = 0;
        tube.SpeechDelayScheduler = (_, fire) => { pending = fire; return () => { }; };
        tube.SpeechAudioPlayer = (_, path, _, done) => { played = path; calls++; done?.Invoke(); };
        try
        {
            CoreMods.ActiveModIdProvider = () => "test";
            CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "test" }, root, false);
            (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted) = (false, 80, false);
            tube.Show(); tube.AttachServiceReactions(null, video);
            tube.Giggle("ordinary line");
            Assert.True(video.Trigger(path: "/video.mp4"));
            Assert.Equal(1, tube.QueuedSpeechCount);
            tube.EndSpeechBubble();
            Assert.NotNull(pending);
            calls = 0; played = "not called";
            pending!();
            Assert.Equal(1, calls);
            Assert.Null(played);
        }
        finally
        {
            video.ForceCleanup(); tube.Close(); CoreSubliminal.BambiFreezeProvider = freeze;
            (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider) = oldMods;
            (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted) = audioSettings;
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VideoEndRemovesOnlyTheQueuedAnnouncement() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var freeze = CoreSubliminal.BambiFreezeProvider;
        CoreSubliminal.BambiFreezeProvider = null;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/video.mp4" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.AttachServiceReactions(null, video);
            tube.Giggle("ordinary line");
            tube.Giggle("next ordinary line");
            Assert.True(tube.HasBubbleUp);
            Assert.True(video.Trigger(path: "/video.mp4"));
            Assert.Equal(2, tube.QueuedSpeechCount);
            video.ForceCleanup();
            Assert.True(tube.HasBubbleUp);
            Assert.Equal(1, tube.QueuedSpeechCount);
        }
        finally { video.ForceCleanup(); tube.Close(); CoreSubliminal.BambiFreezeProvider = freeze; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VideoEndPreservesSpeechQueuedBehindTheAnnouncement() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var freeze = CoreSubliminal.BambiFreezeProvider;
        CoreSubliminal.BambiFreezeProvider = null;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/video.mp4" });
        var tube = new AvatarTubeWindow(null);
        var pending = new List<Action>();
        tube.SpeechDelayScheduler = (_, fire) => { pending.Add(fire); return () => { }; };
        try
        {
            tube.Show();
            tube.AttachServiceReactions(null, video);
            Assert.True(video.Trigger(path: "/video.mp4"));
            Assert.True(tube.HasBubbleUp);
            tube.Giggle("next ordinary line");
            Assert.Equal(1, tube.QueuedSpeechCount);
            video.ForceCleanup();
            Assert.False(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            Assert.Single(pending);
            pending[0]();
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
        }
        finally { video.ForceCleanup(); tube.Close(); CoreSubliminal.BambiFreezeProvider = freeze; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VideoEndCancelsDelayedAnnouncementAndPreservesFollowingSpeech() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var freeze = CoreSubliminal.BambiFreezeProvider;
        CoreSubliminal.BambiFreezeProvider = null;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/video.mp4" });
        var tube = new AvatarTubeWindow(null);
        var pending = new List<(Action Fire, Func<bool> Cancelled)>();
        tube.SpeechDelayScheduler = (_, fire) => { bool cancelled = false; pending.Add((fire, () => cancelled)); return () => cancelled = true; };
        try
        {
            tube.Show();
            tube.AttachServiceReactions(null, video);
            tube.Giggle("ordinary line");
            tube.EndSpeechBubble();
            Assert.True(video.Trigger(path: "/video.mp4"));
            Assert.Single(pending);
            tube.Giggle("next ordinary line");
            video.ForceCleanup();
            Assert.True(pending[0].Cancelled());
            Assert.Equal(2, pending.Count);
            pending[0].Fire(); // A dispatcher callback already queued before cancellation stays retired.
            Assert.False(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            pending[1].Fire();
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
        }
        finally { video.ForceCleanup(); tube.Close(); CoreSubliminal.BambiFreezeProvider = freeze; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VideoAnnouncementUsesTheSelectedLanguage() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var freeze = CoreSubliminal.BambiFreezeProvider;
        var loc = LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        CoreSubliminal.BambiFreezeProvider = null;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/video.mp4" });
        var tube = new AvatarTubeWindow(null);
        var oldMods = (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider);
        var settings = CoreSettings.Current;
        var oldAudio = (settings.AvatarMuted, settings.MasterVolume, settings.CompanionVoiceLinesMuted);
        var root = Path.Combine(Path.GetTempPath(), "ccp-video-event-" + Guid.NewGuid());
        var folder = Path.Combine(root, "resources", "sounds", "event_audio");
        Directory.CreateDirectory(folder);
        var clip = Path.Combine(folder, "ooh pretty spir rals.mp3");
        File.WriteAllText(clip, "audio");
        string? played = null;
        tube.SpeechAudioPlayer = (_, path, _, done) => { played = path; done?.Invoke(); };
        try
        {
            CoreMods.ActiveModIdProvider = () => "test";
            CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "test" }, root, false);
            (settings.AvatarMuted, settings.MasterVolume, settings.CompanionVoiceLinesMuted) = (false, 80, false);
            loc.SetLanguage("es");
            var expected = loc.Get("avatar_video_announcement");
            Assert.NotEqual("avatar_video_announcement", expected);
            tube.Show();
            tube.AttachServiceReactions(null, video);
            Assert.True(video.Trigger(path: "/video.mp4"));
            Assert.True(tube.HasBubbleUp);
            var length = (int)typeof(AvatarTubeWindow).GetField("_currentSpeechLength", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tube)!;
            Assert.Equal(expected.Length, length);
            Assert.Equal(clip, played);
        }
        finally
        {
            video.ForceCleanup(); tube.Close(); loc.SetLanguage(language); CoreSubliminal.BambiFreezeProvider = freeze;
            (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider) = oldMods;
            (settings.AvatarMuted, settings.MasterVolume, settings.CompanionVoiceLinesMuted) = oldAudio;
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VideoEndDoesNotCloseTheListeningBubble() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var freeze = CoreSubliminal.BambiFreezeProvider;
        CoreSubliminal.BambiFreezeProvider = null;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/video.mp4" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.AttachServiceReactions(null, video);
            Assert.True(video.Trigger(path: "/video.mp4"));
            tube.ShowListeningBubble("listening");
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            video.ForceCleanup();
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
        }
        finally { video.ForceCleanup(); tube.Close(); CoreSubliminal.BambiFreezeProvider = freeze; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VideoEndDoesNotSilenceLaterSpeech() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var freeze = CoreSubliminal.BambiFreezeProvider;
        CoreSubliminal.BambiFreezeProvider = null;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/video.mp4" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.AttachServiceReactions(null, video);
            Assert.True(video.Trigger(path: "/video.mp4"));
            Assert.True(tube.HasBubbleUp);
            tube.EndSpeechBubble();
            tube.GigglePriority("another line", aiGenerated: false);
            Assert.True(tube.HasBubbleUp);
            video.ForceCleanup();
            Assert.True(tube.HasBubbleUp);
        }
        finally { video.ForceCleanup(); tube.Close(); CoreSubliminal.BambiFreezeProvider = freeze; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AchievementReactionUsesWpfLineAndStopsOnClose() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        var lines = new List<string>();
        var tube = new AvatarTubeWindow(null) { AchievementReactionSink = lines.Add };
        try
        {
            tube.Show();
            tube.AttachServiceReactions(engine);
            Assert.True(engine.TryUnlock("pop_the_thought"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { $"Achievement unlocked: {Achievement.All["pop_the_thought"].Name}! *giggles*" }, lines);
            tube.Close();
            var handlers = (Delegate?)typeof(AchievementEngine).GetField("Unlocked", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine);
            Assert.Null(handlers);
            Assert.True(engine.TryUnlock("relapse"));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(lines);
        }
        finally
        {
            tube.Close();
            Directory.Delete(dir, recursive: true);
        }
        return Task.CompletedTask;
    });
}
