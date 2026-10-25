using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    private static readonly DateTimeOffset LoginClockStart = DateTimeOffset.UtcNow;
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = LoginClockStart;
        private readonly List<T> _timers = new();
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = new T(this, () => cb(state));
            _timers.Add(t);
            t.Change(due, period);
            return t;
        }
        public void Advance(TimeSpan by)
        {
            var end = _now + by;
            for (T? next; (next = _timers.Find(t => t.Due <= end)) != null;)
            {
                _now = next.Due!.Value;
                next.Due = null;
                next.Fire();
            }
            _now = end;
        }
        private sealed class T(Clock clock, Action fire) : ITimer
        {
            public DateTimeOffset? Due;
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period) { Due = due == Timeout.InfiniteTimeSpan ? null : clock._now + due; return true; }
            public void Dispose() => Due = null;
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }


    private sealed class LoginProfileWire : HttpMessageHandler
    {
        public readonly TaskCompletionSource<HttpResponseMessage> Reply = new();
        public readonly TaskCompletionSource Requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public bool Suppressed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Suppressed = AvApp.Achievements?.SuppressPopups == true;
            Requested.TrySetResult();
            return Reply.Task;
        }
    }

    [Fact]
    public async Task PlainLogoutStampsQuestOwnershipAndKeepsLocalProgress()
    {
        var old = CoreSettings.ServiceProvider;
        var qp = typeof(AvApp).GetProperty("Quests", BindingFlags.Static | BindingFlags.NonPublic)!;
        var saved = qp.GetValue(null);
        var dir = Directory.CreateTempSubdirectory("ccp-login-quests-").FullName;
        var settings = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
        var oldSync = AccountSeed.Sync;
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.UnifiedId = "quest-owner";
            CoreAccount.UnifiedUserId = "quest-owner";
            using var quests = new QuestService(null, dir);
            quests.Progress.TotalDailyQuestsCompleted = 17;
            qp.SetValue(null, quests);
            AccountSeed.Sync = null;
            await AccountSeed.Logout();
            Assert.Equal("quest-owner", quests.Progress.OwnerUnifiedId);
            Assert.Equal(17, quests.Progress.TotalDailyQuestsCompleted);
            quests.EnsureOwnedBy("quest-owner");
            Assert.Equal(17, quests.Progress.TotalDailyQuestsCompleted);
            quests.EnsureOwnedBy("another-account");
            Assert.Equal(0, quests.Progress.TotalDailyQuestsCompleted);
        }
        finally
        {
            settings.SealForReset();
            qp.SetValue(null, saved);
            CoreSettings.ServiceProvider = old;
            AccountSeed.Sync = oldSync;
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LoginSettlesThenRestoresSilentlyAndRejectsAnAccountChangedDuringLoad(bool changeAccount, bool failLoad)
    {
        var old = (CoreSettings.ServiceProvider, AccountSeed.NewV2, LoginDialog.Clock, AccountSeed.Sync);
        var ap = typeof(AvApp).GetProperty("Achievements", BindingFlags.Static | BindingFlags.NonPublic)!;
        var qp = typeof(AvApp).GetProperty("Quests", BindingFlags.Static | BindingFlags.NonPublic)!;
        var savedA = ap.GetValue(null);
        var savedQ = qp.GetValue(null);
        var dir = Directory.CreateTempSubdirectory("ccp-login-cloud-").FullName;
        var settings = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
        var clock = new Clock();
        var wire = new LoginProfileWire();
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.UnifiedId = "first";
            settings.Current.OfflineMode = false;
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            ap.SetValue(null, engine);
            using var quests = new QuestService(null, dir);
            quests.StampOwner("old-account");
            qp.SetValue(null, quests);
            AccountSeed.Sync = null;
            LoginDialog.Clock = clock;
            AccountSeed.NewV2 = () => new V2AuthService(() => settings.Current, wire);
            var task = AccountSeed.LoadAfterLoginAsync();
            clock.Advance(TimeSpan.FromMilliseconds(499));
            Assert.Equal(0, wire.Calls);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await wire.Requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, wire.Calls);
            Assert.True(wire.Suppressed);
            Assert.Equal("first", quests.Progress.OwnerUnifiedId);
            if (changeAccount) settings.Current.UnifiedId = "second";
            wire.Reply.SetResult(new HttpResponseMessage(failLoad ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) {
                Content = new StringContent("""{"success":true,"user":{"unified_id":"first","achievements":["future-cloud-id"],"level":1,"xp":0}}""") });
            await task;
            Assert.Equal(!changeAccount && !failLoad, engine.Progress.IsUnlocked("future-cloud-id"));
            Assert.False(engine.SuppressPopups);
        }
        finally
        {
            settings.SealForReset();
            ap.SetValue(null, savedA); qp.SetValue(null, savedQ);
            (CoreSettings.ServiceProvider, AccountSeed.NewV2, LoginDialog.Clock, AccountSeed.Sync) = old;
            Directory.Delete(dir, true);
        }
    }
}
