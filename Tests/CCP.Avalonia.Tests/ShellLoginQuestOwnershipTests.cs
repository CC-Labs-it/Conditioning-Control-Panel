using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LogoutAndNextProfileLoad_PreserveOnlyTheOwnersQuests(bool sameAccount) => WithFreshInstall(async () =>
    {
        var oldQuests = AvApp.Quests;
        var dir = Path.Combine(Path.GetTempPath(), "ccp-quest-login-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var quests = new QuestService(null, dir);
        try
        {
            AvApp.Quests = quests;
            var (wire, _, _) = SignIn("a", profile: null);
            quests.Progress.TotalDailyQuestsCompleted = 17;
            quests.Progress.DailyQuests[0].CurrentProgress = 2;
            await AccountSeed.Logout();
            Assert.Equal("a", quests.Progress.OwnerUnifiedId);
            Assert.Equal(17, quests.Progress.TotalDailyQuestsCompleted);
            Assert.Equal(2, quests.Progress.DailyQuests[0].CurrentProgress);
            Assert.Contains("\"OwnerUnifiedId\": \"a\"", File.ReadAllText(Path.Combine(dir, "quests.json")));

            var next = sameAccount ? "a" : "b";
            SignIn(next, profile: null, wire);
            // Even a failed GET must isolate the quest file before any cloud work.
            Assert.False(await AccountSeed.LoadProfileAsync());
            Assert.Equal(next, quests.Progress.OwnerUnifiedId);
            Assert.Equal(sameAccount ? 17 : 0, quests.Progress.TotalDailyQuestsCompleted);
            if (sameAccount) Assert.Equal(2, quests.Progress.DailyQuests[0].CurrentProgress);
            else Assert.Empty(quests.Progress.DailyQuests);
        }
        finally { AvApp.Quests = oldQuests; quests.Dispose(); Directory.Delete(dir, true); }
    });

    [Fact]
    public void DeletionWipesTheQuestFileInsteadOfPreservingIt() => WithFreshInstall(async () =>
    {
        var oldQuests = AvApp.Quests;
        var dir = Path.Combine(Path.GetTempPath(), "ccp-quest-delete-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var quests = new QuestService(null, dir);
        try
        {
            AvApp.Quests = quests;
            SignIn("a", profile: null);
            quests.StampOwner("a");
            quests.Progress.TotalDailyQuestsCompleted = 17;
            await AccountSeed.Logout(accountDeleted: true);
            Assert.Null(quests.Progress.OwnerUnifiedId);
            Assert.Equal(0, quests.Progress.TotalDailyQuestsCompleted);
        }
        finally { AvApp.Quests = oldQuests; quests.Dispose(); Directory.Delete(dir, true); }
    });

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void SwitchClearsBeforeReadAndGeneratesAfterwardOnlyForTheSameSession(int replacement, bool succeeds) => WithFreshInstall(async () =>
    {
        var oldQuests = AvApp.Quests;
        var dir = Path.Combine(Path.GetTempPath(), "ccp-quest-switch-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var quests = new QuestService(null, dir);
        try
        {
            AvApp.Quests = quests;
            var (wire, _, _) = SignIn("a", profile: null);
            quests.Progress.TotalDailyQuestsCompleted = 17; // unstamped older file
            wire.Profile = succeeds ? L40Profile("b") : null;
            wire.ProfileHold = new TaskCompletionSource();
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (global::Avalonia.Application.Current is null)
                    AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var dialog = new LoginDialog();
                dialog.Succeed(new V2AuthService.V2User { UnifiedId = "b" }, "tok", null, false);
                Assert.False(dialog.ProfileLoad.IsCompleted);
                Assert.Equal("b", quests.Progress.OwnerUnifiedId);
                Assert.Equal(0, quests.Progress.TotalDailyQuestsCompleted);
                Assert.Empty(quests.Progress.DailyQuests);
                if (replacement == 1) AccountSeed.Sync!.Reset(); // same id, new session
                if (replacement == 2) CoreSettings.Current.UnifiedId = "c";
                wire.ProfileHold.SetResult();
                await dialog.ProfileLoad;
                if (replacement != 0) Assert.Empty(quests.Progress.DailyQuests);
                else Assert.NotEmpty(quests.Progress.DailyQuests);
            });
        }
        finally { AvApp.Quests = oldQuests; quests.Dispose(); Directory.Delete(dir, true); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LogoutThenLogin_PreservesSameOwnerOrGeneratesForDifferentOwner(bool same) => WithFreshInstall(async () =>
    {
        AccountSeed.StartupUnifiedId = AccountSeed.LastKnownUnifiedId = null;
        var oldQuests = AvApp.Quests;
        var dir = Path.Combine(Path.GetTempPath(), "ccp-quest-relogin-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var quests = new QuestService(null, dir);
        try
        {
            AvApp.Quests = quests;
            var (wire, _, _) = SignIn("a", profile: null);
            quests.Progress.TotalDailyQuestsCompleted = 17;
            quests.Progress.DailyQuests[0].CurrentProgress = 2;
            await AccountSeed.Logout();
            wire.ProfileHold = new TaskCompletionSource();
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (global::Avalonia.Application.Current is null)
                    AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var dialog = new LoginDialog();
                dialog.Succeed(new V2AuthService.V2User { UnifiedId = same ? "a" : "b" }, "tok", null, false);
                Assert.False(dialog.ProfileLoad.IsCompleted);
                if (same) Assert.Equal(2, quests.Progress.DailyQuests[0].CurrentProgress);
                else Assert.Empty(quests.Progress.DailyQuests);
                wire.ProfileHold.SetResult();
                await dialog.ProfileLoad; // failed GET still generates for a proven switch
                Assert.NotEmpty(quests.Progress.DailyQuests);
                Assert.Equal(same ? 17 : 0, quests.Progress.TotalDailyQuestsCompleted);
                if (same) Assert.Equal(2, quests.Progress.DailyQuests[0].CurrentProgress);
            });
        }
        finally { AvApp.Quests = oldQuests; quests.Dispose(); Directory.Delete(dir, true); }
    });

    [Theory]
    [InlineData("a", null, "a", false)]
    [InlineData("a", null, "b", true)]
    [InlineData(null, null, "b", false)]
    [InlineData("a", "b", "b", false)]
    public void ExpiredStartupIdentity_OnlyClearsAProvenDifferentAccount(string? startup, string? last, string next, bool clears) => WithFreshInstall(async () =>
    {
        AccountSeed.StartupUnifiedId = AccountSeed.LastKnownUnifiedId = null;
        var (wire, _, _) = SignIn(startup ?? "temporary", profile: null);
        CoreSettings.Current.UnifiedId = startup;
        AccountSeed.RestoreSession();
        AccountSeed.LastKnownUnifiedId = last;
        CoreAccount.UnifiedUserId = null;
        CoreSettings.Current.UnifiedId = null; // session validation expired before the dialog opened
        CoreSettings.Current.PlayerLevel = 23;
        wire.ProfileHold = new TaskCompletionSource();
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (global::Avalonia.Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dialog = new LoginDialog();
            // A provider can alter live state during authentication; the decision uses the earlier snapshot.
            CoreSettings.Current.UnifiedId = "provider-temporary";
            dialog.Succeed(new V2AuthService.V2User { UnifiedId = next }, "tok", null, false);
            Assert.Equal(clears ? 1 : 23, CoreSettings.Current.PlayerLevel);
            wire.ProfileHold.SetResult();
            await dialog.ProfileLoad;
        });
    });

}
