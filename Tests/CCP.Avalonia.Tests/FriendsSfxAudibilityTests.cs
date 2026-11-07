using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsSfxAudibilityTests
{
    // WPF Services/Friends/FriendsSfx.cs:47-54,74-77: a silent cue spends neither sound gap.
    [Theory]
    [InlineData("suppressed")]
    [InlineData("video")]
    [InlineData("hold")]
    [InlineData("muted")]
    public void SilentCuesDoNotPlayOrConsumeTheGate(string condition)
    {
        var (audio, art, suppressed, video, gate, volume) = (CoreAudio.PlayOneShotProvider,
            CoreModArt.AudioOverridePathProvider, CoreAudio.IsOutputSuppressedProvider,
            CoreEngine.Video, FriendsLanding.SfxGate, CoreSettings.Current.MasterVolume);
        var engine = EmiLineEngine.Instance;
        var heldBy = typeof(EmiLineEngine).GetField("_heldBy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var holdUntil = typeof(EmiLineEngine).GetField("_holdUntilUtc", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var (oldHeldBy, oldHoldUntil) = (heldBy.GetValue(engine), holdUntil.GetValue(engine));
        var scheduler = new MandatoryVideoScheduler(new SilentHost());
        // Set only the active states, with no video windows, timers or live Emi holds to leak.
        var playing = typeof(MandatoryVideoScheduler).GetField("_playing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var path = Path.GetTempFileName();
        var played = new List<string>();
        bool silent = true;
        void State()
        {
            CoreSettings.Current.MasterVolume = silent && condition == "muted" ? 0 : 50;
            playing.SetValue(scheduler, silent && condition == "video");
            heldBy.SetValue(engine, silent && condition == "hold" ? "friends-sfx-test" : null);
            holdUntil.SetValue(engine, DateTime.MinValue);
        }
        try
        {
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => played.Add(tag);
            CoreModArt.AudioOverridePathProvider = _ => path;
            CoreAudio.IsOutputSuppressedProvider = () => silent && condition == "suppressed";
            CoreEngine.Video = scheduler;
            foreach (var (cue, tag) in new (Action, string)[] {
                (FriendsSfx.Click, "friends-click"), (FriendsSfx.Sent, "friends-sent"),
                (FriendsSfx.Denied, "friends-denied"),
                (() => FriendsLanding.Sfx("chaos/dling.mp3", 0.18f, "friends-knock", incoming: true), "friends-knock") })
            {
                FriendsLanding.SfxGate = new();
                silent = true;
                State();
                cue();
                Assert.Empty(played);
                // An earlier timestamp passes only if neither gate was consumed by the silent cue.
                Assert.True(FriendsLanding.SfxGate.TryPass(DateTime.UtcNow.AddDays(-1), incoming: true));
                silent = false;
                State();
                cue();
                Assert.Equal(tag, Assert.Single(played));
                played.Clear();
            }
        }
        finally
        {
            (CoreAudio.PlayOneShotProvider, CoreModArt.AudioOverridePathProvider,
                CoreAudio.IsOutputSuppressedProvider, CoreEngine.Video, FriendsLanding.SfxGate,
                CoreSettings.Current.MasterVolume) = (audio, art, suppressed, video, gate, volume);
            heldBy.SetValue(engine, oldHeldBy);
            holdUntil.SetValue(engine, oldHoldUntil);
            File.Delete(path);
        }
    }

    private sealed class SilentHost : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }
}
