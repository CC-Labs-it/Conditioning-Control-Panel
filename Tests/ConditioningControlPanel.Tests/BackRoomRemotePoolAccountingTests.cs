using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Fyp;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class BackRoomRemotePoolAccountingTests
{
    [Fact]
    public async Task PublishingAReadyClipTransfersItsReservationBeforeSignallingReaders()
    {
        // BackRoomRemotePool.cs:353 counts ready + in-flight under _gate. Publication must
        // transfer that reservation in the same lock, or the coordinator can stop at 23 of 24.
        var root = Directory.CreateTempSubdirectory("ccp-pool-accounting-").FullName;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var path = Path.Combine(root, "clip.mp4");
        var pool = new BackRoomRemotePool(
            () => new AppSettings { BackRoomMediaSource = "online", RemoteMediaConsented = true, FypOnlineConsented = true },
            _ => Task.FromResult((new List<FypAssetManifest.Entry>
            {
                new() { Id = "scrolller/sub/clip", Url = "https://cdn.example.com/clip.mp4", Type = "video", Origin = "online" }
            }, (string?)null)),
            async (_, _) =>
            {
                entered.SetResult();
                await release.Task;
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                return path;
            }, _ => { }, () => root, () => null);
        var warm = pool.WarmAsync(CancellationToken.None);
        Task? batch = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            batch = pool.WarmInFlight();
            // Replace only the notification with an inline observer, while materialization is
            // gated. This deterministically inspects the publication point inside _gate, before
            // the worker's finally can hide the double count. No scheduler timing or sleeps.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var publication = new TaskCompletionSource<bool>();
            typeof(BackRoomRemotePool).GetField("_firstLanded", flags)!.SetValue(pool, publication);
            int ready = -1, reserved = -1;
            var observed = publication.Task.ContinueWith(_ =>
            {
                ready = pool.Ready().Count;
                reserved = (int)typeof(BackRoomRemotePool).GetField("_inFlight", flags)!.GetValue(pool)!;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            release.SetResult();
            await observed.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, ready);
            Assert.Equal(0, reserved);
        }
        finally
        {
            release.TrySetResult();
            await (batch ?? pool.WarmInFlight() ?? warm).WaitAsync(TimeSpan.FromSeconds(10));
            await warm;
            Directory.Delete(root, true);
        }
    }
}
