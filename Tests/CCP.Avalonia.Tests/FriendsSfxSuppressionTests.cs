using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsSfxSuppressionTests
{
    // WPF Services/Friends/FriendsSfx.cs:41-73. Real engine state, without opening native media.
    [Theory]
    [InlineData("output", false)]
    [InlineData("video", false)]
    [InlineData("emi", false)]
    [InlineData("output", true)]
    [InlineData("video", true)]
    [InlineData("emi", true)]
    public Task SuppressionLeavesCuesSilentAndTheGateUnused(string reason, bool afterClose) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var cue = Path.GetTempFileName();
        var played = new List<string>();
        var oldVideo = CoreEngine.Video;
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var oldArt = CoreModArt.AudioOverridePathProvider;
        var oldSuppressed = CoreAudio.IsOutputSuppressedProvider;
        var oldGate = FriendsLanding.SfxGate;
        var heldBy = typeof(EmiLineEngine).GetField("_heldBy", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var holdUntil = typeof(EmiLineEngine).GetField("_holdUntilUtc", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var emi = EmiLineEngine.Instance;
        var oldHold = heldBy.GetValue(emi);
        var oldUntil = holdUntil.GetValue(emi);
        var video = new MandatoryVideoScheduler(new QuietVideoHost());
        var playing = typeof(MandatoryVideoScheduler).GetField("_playing", BindingFlags.NonPublic | BindingFlags.Instance)!;
        bool blocked = false;
        void Block(bool on)
        {
            blocked = on;
            playing.SetValue(video, on && reason == "video");
            heldBy.SetValue(emi, on && reason == "emi" ? "friends-sfx-test" : null);
        }
        FriendsDrawer? drawer = null;
        try
        {
            CoreSettings.Current.MasterVolume = 80;
            CoreEngine.Video = video;
            holdUntil.SetValue(emi, DateTime.MinValue);
            Block(false);
            CoreAudio.IsOutputSuppressedProvider = () => blocked && reason == "output";
            CoreModArt.AudioOverridePathProvider = _ => cue;
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => played.Add(tag);
            Action[] cues = { FriendsLanding.SfxClick, FriendsLanding.SfxAccepted,
                FriendsLanding.SfxDismiss, FriendsLanding.SfxDenied };
            if (afterClose)
            {
                var (service, wire) = FriendsDrawerTests.Service();
                wire.HoldActions = new TaskCompletionSource();
                await service.RefreshAsync();
                drawer = new FriendsDrawer(service);
                drawer.OnOpened();
                var answer = drawer.AnswerRequestAsync(service.Snapshot.Incoming.First(), "accept");
                Assert.False(answer.IsCompleted);
                drawer.OnClosed();
                played.Clear();
                FriendsLanding.SfxGate = new FriendsSfxGate();
                Block(true);
                wire.HoldActions.SetResult();
                Assert.Equal(ActResult.Done, await answer);
                Assert.Empty(played);
                // This historical timestamp can pass only if suppression never consumed the gate.
                Assert.True(FriendsLanding.SfxGate.TryPass(new DateTime(2000, 1, 1), false));
            }
            else
            {
                foreach (var play in cues)
                {
                    FriendsLanding.SfxGate = new FriendsSfxGate();
                    Block(true);
                    play();
                    Assert.Empty(played);
                    Assert.True(FriendsLanding.SfxGate.TryPass(new DateTime(2000, 1, 1), false));
                }
            }
            Block(false);
            FriendsLanding.SfxAccepted(); // Positive control: existing file, volume and player work.
            Assert.Equal("friends-accepted", Assert.Single(played));
        }
        finally
        {
            drawer?.OnClosed();
            CoreEngine.Video = oldVideo;
            CoreAudio.PlayOneShotProvider = oldAudio;
            CoreAudio.IsOutputSuppressedProvider = oldSuppressed;
            CoreModArt.AudioOverridePathProvider = oldArt;
            FriendsLanding.SfxGate = oldGate;
            heldBy.SetValue(emi, oldHold);
            holdUntil.SetValue(emi, oldUntil);
            File.Delete(cue);
        }
    });

    private sealed class QuietVideoHost : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) => throw new InvalidOperationException("No native playback in this test.");
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }
}
