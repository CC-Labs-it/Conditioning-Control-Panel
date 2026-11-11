using System;
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
        MergedAccountRecoveryReviewTests.Setup();
        var settings = new AppSettings { UnifiedId = "u_tombstone1", PlayerLevel = 7, PlayerXP = 42 };
        using var saved = new MergedRecoveryState();
        string? token = "old";
        CoreSecrets.RetrieveProvider = _ => token;
        CoreSecrets.StoreProvider = (_, value) => token = value;
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-merge-" + Guid.NewGuid());
        System.IO.Directory.CreateDirectory(dir);
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
            using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
            { Content = new StringContent("{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}") };
            Assert.True(V2AuthService.MergedRecovery!(response, null).GetAwaiter().GetResult());
            MergedAccountRecovery.LastSwap.GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("u_canon123", settings.UnifiedId);
            Assert.Null(CoreAccount.UnifiedUserId);
            Assert.Null(settings.AuthToken);
            Assert.False(sync.Loaded);
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
    private static readonly Newtonsoft.Json.JsonSerializerSettings Json = new()
        { ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace };
    private readonly AppSettings _settings = CoreSettings.Current;
    private readonly string _settingsJson = Newtonsoft.Json.JsonConvert.SerializeObject(CoreSettings.Current, Json);
    private readonly Func<string, string?>? _get = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _set = CoreSecrets.StoreProvider;
    private readonly Func<V2AuthService> _newV2 = AccountSeed.NewV2;
    private readonly SyncPush? _sync = AccountSeed.Sync;
    private readonly QuestService? _quests = App.Quests;
    private readonly Func<ConditioningControlPanel.Avalonia.Views.Dialogs.WelcomeBackSheetContent, Task> _present = WelcomeBackOffer.Present;
    private readonly Func<bool>? _loggedIn = CoreAccount.IsLoggedInProvider, _premium = CoreAccount.HasPremiumAccessProvider,
        _lab = CoreAccount.HasLabAccessProvider, _whitelisted = CoreAccount.IsWhitelistedProvider,
        _entPremium = CoreEntitlement.HasPremiumProvider, _entLab = CoreEntitlement.HasLabProvider,
        _programPremium = CoreProgram.HasPremiumProvider, _session = CoreSession.IsSessionRunningProvider;
    private readonly Func<string?>? _name = CoreAccount.DisplayNameProvider;
    private readonly string? _unifiedUserId = CoreAccount.UnifiedUserId;
    private readonly bool _pendingSignIn = MergedAccountRecoveryHead.PendingSignIn;
    private readonly Func<Task<bool>>? _revalidate = AuthRecovery.ProviderRevalidate;
    private readonly Action? _recovered = AuthRecovery.Recovered, _stopSync = MergedAccountRecovery.StopSync;
    private readonly Action<string>? _stamp = MergedAccountRecovery.StampQuests;
    private readonly Func<Task<bool>>? _reauth = MergedAccountRecovery.Reauthenticate;
    private readonly Func<Task>? _reload = MergedAccountRecovery.ReloadProfile;
    private readonly Action<string, bool>? _finished = MergedAccountRecovery.Finished;
    private readonly Func<HttpResponseMessage, string?, Task<bool>>? _v2Hook = V2AuthService.MergedRecovery;
    private readonly Func<HttpResponseMessage, Task>? _aiHook = AiService.MergedAccountHook;
    private readonly Func<int, string, bool>? _backRoomHook = ConditioningControlPanel.Services.BackRoom.BackRoomApi.MergedReply;

    public void Dispose()
    {
        MergedAccountRecovery.ResetForTest();
        MergedAccountRecoveryHead.PendingSignIn = _pendingSignIn;
        (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, AccountSeed.NewV2, AccountSeed.Sync) = (_get, _set, _newV2, _sync);
        (App.Quests, WelcomeBackOffer.Present) = (_quests, _present);
        (CoreAccount.IsLoggedInProvider, CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider) = (_loggedIn, _premium, _lab);
        (CoreAccount.IsWhitelistedProvider, CoreAccount.DisplayNameProvider, CoreAccount.UnifiedUserId) = (_whitelisted, _name, _unifiedUserId);
        (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider) = (_entPremium, _entLab);
        (CoreProgram.HasPremiumProvider, CoreSession.IsSessionRunningProvider) = (_programPremium, _session);
        (AuthRecovery.ProviderRevalidate, AuthRecovery.Recovered) = (_revalidate, _recovered);
        (MergedAccountRecovery.StopSync, MergedAccountRecovery.StampQuests, MergedAccountRecovery.Reauthenticate) = (_stopSync, _stamp, _reauth);
        (MergedAccountRecovery.ReloadProfile, MergedAccountRecovery.Finished) = (_reload, _finished);
        (V2AuthService.MergedRecovery, AiService.MergedAccountHook) = (_v2Hook, _aiHook);
        ConditioningControlPanel.Services.BackRoom.BackRoomApi.MergedReply = _backRoomHook;
        Newtonsoft.Json.JsonConvert.PopulateObject(_settingsJson, _settings, Json);
    }
}
