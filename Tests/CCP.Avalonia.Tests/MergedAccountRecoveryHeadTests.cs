using System;
using System.Linq;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class MergedAccountRecoveryHeadTests
{
    [Fact]
    public void MergedResponsePreservesProgressAndDefersSignIn() => AvaloniaTestDispatcher.Run(() =>
    {
        using var saved = new MergedRecoveryState();
        MergedAccountRecoveryReviewTests.Setup();
        var settings = new AppSettings { UnifiedId = "u_tombstone1", PlayerLevel = 7, PlayerXP = 42 };
        string? token = "old";
        CoreSecrets.RetrieveProvider = _ => token;
        CoreSecrets.StoreProvider = (_, value) => token = value;
        var dir = System.IO.Directory.CreateTempSubdirectory("ccp-merge-").FullName;
        using var quests = new QuestService(null, dir);
        var sync = new SyncPush(() => null, () => false);
        try
        {
            quests.Progress.OwnerUnifiedId = "u_tombstone1";
            quests.Progress.DailyQuestsCompletedToday = 3;
            App.Quests = quests;
            AccountSeed.Sync = sync;
            sync.MarkLoaded(null);
            MergedAccountRecovery.SettingsForTest = () => settings;
            MergedAccountRecoveryHead.Seed();
            using var response = new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}") };
            Assert.True(V2AuthService.MergedRecovery!(response, null).GetAwaiter().GetResult());
            MergedAccountRecovery.LastSwap.GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("u_canon123", settings.UnifiedId);
            Assert.Equal("u_canon123", CoreAccount.UnifiedUserId);
            Assert.Null(settings.AuthToken);
            Assert.False(sync.Loaded || sync.PushAsync("test").GetAwaiter().GetResult());
            Assert.Equal("u_canon123", quests.Progress.OwnerUnifiedId);
            Assert.Equal(3, quests.Progress.DailyQuestsCompletedToday);
            Assert.Equal(7, settings.PlayerLevel);
            Assert.Equal(42, settings.PlayerXP);
            Assert.True(StartupLadder.Inbox.Contains("merged-account-signin:u_canon123"));
        }
        finally
        {
            StartupLadder.ResetForTests();
            System.IO.Directory.Delete(dir, true);
        }
    });
}

/// <summary>Every static and settings value the merged-recovery tests set, put back on Dispose.</summary>
internal sealed class MergedRecoveryState : IDisposable
{
    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly ProcessStateSnapshot _snapshot = ProcessStateSnapshot.Take();
    // Seed mutates these statics through methods, so the suite's assignment scanner cannot find them.
    private readonly (FieldInfo Field, object? Value)[] _statics = new[]
        { typeof(AccountSeed), typeof(MergedAccountRecovery), typeof(V2AuthService), typeof(AiService),
          typeof(ConditioningControlPanel.Services.BackRoom.BackRoomApi), typeof(AuthRecovery) }
        .SelectMany(t => t.GetFields(StaticFields)).Where(f => !f.IsInitOnly && !f.IsLiteral)
        .Select(f => (f, f.GetValue(null))).ToArray();
    private readonly (FieldInfo Field, object? Value)[] _intake = App.IntakePass.GetType().GetFields(InstanceFields)
        .Where(f => f.Name is "_entitlementHooked" or "_entitlementHandler" or "_detachEntitlement")
        .Select(f => (f, f.GetValue(App.IntakePass))).ToArray();
    private readonly FieldInfo _tierEvent = typeof(ProviderSubscription).GetField("TierChanged", InstanceFields)!;
    private readonly ProviderSubscription? _patreon = AccountSeed.Patreon, _substar = AccountSeed.SubscribeStar;
    private readonly object? _patreonHandlers = AccountSeed.Patreon is { } p ? typeof(ProviderSubscription).GetField("TierChanged", InstanceFields)!.GetValue(p) : null;
    private readonly object? _substarHandlers = AccountSeed.SubscribeStar is { } p ? typeof(ProviderSubscription).GetField("TierChanged", InstanceFields)!.GetValue(p) : null;

    public void Dispose()
    {
        var handler = (EventHandler<PatreonTier>?)_intake.Single(p => p.Field.Name == "_entitlementHandler").Field.GetValue(App.IntakePass);
        var detach = (Action<EventHandler<PatreonTier>>?)_intake.Single(p => p.Field.Name == "_detachEntitlement").Field.GetValue(App.IntakePass);
        if (handler != null) detach?.Invoke(handler);
        foreach (var (field, value) in _intake) field.SetValue(App.IntakePass, value);
        if (_patreon != null) _tierEvent.SetValue(_patreon, _patreonHandlers);
        if (_substar != null) _tierEvent.SetValue(_substar, _substarHandlers);
        _snapshot.Restore();
        foreach (var (field, value) in _statics) field.SetValue(null, value);
    }
}
