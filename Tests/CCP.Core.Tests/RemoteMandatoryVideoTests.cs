using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class RemoteMandatoryVideoTests
{
    private const string Url = "https://cdn.scrolller.com/example.mp4";

    [Fact]
    public void Empty_pool_picks_remote_only_with_consent_and_falls_back_when_dry()
    {
        var s = CoreSettings.Current;
        var saved = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented);
        try
        {
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented) = ("online", true, false);
            int picks = 0;
            var empty = new MandatoryVideoScheduler(new Host(), library: Array.Empty<string>)
                { RemotePick = () => { picks++; return Url; } };
            Assert.Equal(Url, empty.PickNext());
            var local = new MandatoryVideoScheduler(new Host(), library: () => new[] { "/tmp/local.mp4" })
                { RemotePick = () => { picks++; return Url; } };
            Assert.Equal("/tmp/local.mp4", local.PickNext());
            Assert.Equal(1, picks);
            empty.RemotePick = () => null;
            Assert.Null(empty.PickNext());
            empty.RemotePick = () => { picks++; return Url; };
            s.RemoteMediaConsented = false;
            Assert.Null(empty.PickNext());
            s.RemoteMediaConsented = true;
            s.MediaSource = "local";
            Assert.Null(empty.PickNext());
            Assert.Equal(1, picks);
        }
        finally { (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented) = saved; }
    }

    [Fact]
    public void Trigger_shows_the_ready_clip_after_preroll()
    {
        var s = CoreSettings.Current;
        var saved = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.AttentionChecksEnabled);
        var post = CoreDispatch.PostProvider;
        var freeze = CoreSubliminal.BambiFreezeProvider;
        var clock = new PopQuizSchedulerTests.FakeClock();
        var host = new Host();
        var video = new MandatoryVideoScheduler(host, clock, Array.Empty<string>) { RemotePick = () => Url };
        try
        {
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.AttentionChecksEnabled) = ("online", true, false, false);
            CoreDispatch.PostProvider = a => a();
            CoreSubliminal.BambiFreezeProvider = null;
            Assert.True(video.Trigger(userInitiated: true));
            Assert.Null(host.Shown);
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(Url, host.Shown);
        }
        finally
        {
            video.ForceCleanup();
            CoreDispatch.PostProvider = post;
            CoreSubliminal.BambiFreezeProvider = freeze;
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.AttentionChecksEnabled) = saved;
        }
    }

    [Fact]
    public async Task Pool_downloads_before_a_pick_and_honours_policy_and_consent()
    {
        var s = CoreSettings.Current;
        var saved = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, ScrolllerPolicy.Enabled, ScrolllerPolicy.MaxClipShortSide);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cached = new HashSet<string>();
        var pool = new RemoteVideoPool
        {
            Cached = cached.Contains,
            CanFetchForTests = _ => true,
            FetchForTests = () => Task.CompletedTask,
            DownloadForTests = async url => { started.SetResult(); await release.Task; cached.Add(url); return "/cache/clip.mp4"; }
        };
        try
        {
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented) = ("online", true, false);
            ScrolllerPolicy.Apply(true, null);
            pool.SeedForTests(Url);
            Assert.Null(pool.Take());
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(pool.Take());
            release.SetResult();
            await pool.PendingForTests!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Url, pool.Take());
            await pool.PendingForTests!.WaitAsync(TimeSpan.FromSeconds(5));
            pool.SeedForTests(Url);
            s.RemoteMediaConsented = false;
            Assert.Null(pool.Take());
            s.RemoteMediaConsented = true;
            ScrolllerPolicy.Apply(false, null);
            Assert.Null(pool.Take());
            Assert.Equal(0, pool.CountForTests);
        }
        finally
        {
            release.TrySetResult();
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented) = (saved.MediaSource, saved.RemoteMediaConsented, saved.FypOnlineConsented);
            ScrolllerPolicy.Apply(saved.Enabled, saved.MaxClipShortSide);
        }
    }

    private sealed class Host : IMandatoryVideoHost
    {
        public bool BubbleCountActive => false;
        public bool IsFloating(bool strict) => false;
        public void Freeze() { }
        public void FinishFreeze(bool cancelled) { }
        public string? Shown;
        public void Show(string path, bool strict) => Shown = path;
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }
}
