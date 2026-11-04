using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
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
        if (Application.Current is null)
            AppBuilder.Configure<App>().WithInterFont().With(AppFonts.Options())
                .ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new AppSettings { UnifiedId = "u_tombstone1", PlayerLevel = 7, PlayerXP = 42 };
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldSet = CoreSecrets.StoreProvider;
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
            MergedAccountRecovery.ResetForTest();
            MergedAccountRecovery.SettingsForTest = () => settings;
            CoreSession.IsSessionRunningProvider = () => true;
            StartupLadder.ResetForTests();
            MergedAccountRecoveryHead.Seed();
            using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
            { Content = new StringContent("{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}") };
            Assert.True(V2AuthService.MergedRecovery!(response, null).GetAwaiter().GetResult());
            MergedAccountRecovery.LastSwap.GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("u_canon123", settings.UnifiedId);
            Assert.Equal("u_canon123", CoreAccount.UnifiedUserId);
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
            MergedAccountRecovery.ResetForTest();
            MergedAccountRecovery.StopSync = null;
            MergedAccountRecovery.StampQuests = null;
            MergedAccountRecovery.Finished = null;
            V2AuthService.MergedRecovery = null;
            App.Quests = null;
            AccountSeed.Sync = null;
            CoreSecrets.RetrieveProvider = oldGet;
            CoreSecrets.StoreProvider = oldSet;
            StartupLadder.ResetForTests();
            System.IO.Directory.Delete(dir, true);
        }
    });
}
