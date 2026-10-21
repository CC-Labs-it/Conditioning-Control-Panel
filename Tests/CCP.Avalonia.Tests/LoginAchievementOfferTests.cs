using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LoginAchievementOfferTests
{
    private static Task Run(Func<MainShellWindow, AppSettings, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldSet = CoreSecrets.StoreProvider;
        var oldV2 = AccountSeed.NewV2;
        var oldId = CoreAccount.UnifiedUserId;
        var oldRegistry = PanicSurfaces.All;
        var oldSync = AccountSeed.Sync;
        var oldNotRemembered = SecretStore.NotRemembered;
        SecretStore.NotRemembered = false;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        foreach (var key in FeatureIntros.All.Keys) if (!s.SeenFeatureIntros.Contains(key)) s.SeenFeatureIntros.Add(key);
        s.DiscordShareAchievements = false;
        s.UnifiedId = null;
        CoreAccount.UnifiedUserId = null;
        var secrets = new Dictionary<string, string?>
        {
            ["discord_auth"] = JsonConvert.SerializeObject(new DiscordTokenData
                { AccessToken = "fake", ExpiresAt = DateTime.UtcNow.AddHours(1) })
        };
        CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        AccountSeed.NewV2 = () => new V2AuthService(() => s, new FailedProfile());
        AccountSeed.Sync = null;
        AccountSeed.Seed(p => new ProviderSubscription(p, () => s), p => new DiscordAccount(() => p, () => s));
        StartupLadder.ResetForTests();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            await body(shell, s);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            StartupLadder.ResetForTests();
            service.SealForReset(); // this fixture's debounce cannot write into the next case
            PanicSurfaces.All = oldRegistry;
            AccountSeed.Sync = oldSync;
            SecretStore.NotRemembered = oldNotRemembered;
            AccountSeed.NewV2 = oldV2;
            CoreSettings.ServiceProvider = oldSettings;
            CoreSecrets.RetrieveProvider = oldGet;
            CoreSecrets.StoreProvider = oldSet;
            AccountSeed.Seed();
            CoreAccount.UnifiedUserId = oldId;
        }
    });

    private sealed class FailedProfile : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private static async Task WaitFor(Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Assert.True(done(), "Expected sign-in continuation did not arrive");
    }

    private static Task SignIn(MainShellWindow shell)
    {
        var task = shell.OpenUnifiedLoginDialog();
        shell.OwnedWindows.OfType<LoginDialog>().Single().Succeed(
            new V2AuthService.V2User { UnifiedId = "test-account", DisplayName = "Test" }, "fake", "discord", false);
        return task;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task DiscordSignInOffersExplicitOptInWithPanelAsOwner(bool accept) => Run(async (shell, s) =>
    {
        var signIn = SignIn(shell);
        await WaitFor(() => signIn.IsCompleted || shell.OwnedWindows.OfType<MessageDialog>().Any());
        var offer = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
        Assert.Same(shell, offer.Owner);
        Assert.Equal(Loc.Get("title_discord_linked"), offer.Title);
        Assert.Equal(Loc.Get("msg_discord_share_achievements_prompt"), offer.FindControl<TextBlock>("TxtMessage")!.Text);
        Assert.False(s.DiscordShareAchievements); // never enabled merely by linking
        offer.FindControl<Button>(accept ? "BtnOk" : "BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await signIn;
        Assert.Equal(accept, s.DiscordShareAchievements);
        if (accept)
        {
            var sharing = shell.ProfilePage!.PrivacyPanel.FindControl<CheckBox>("ChkDiscordTabShareAchievements")!;
            Assert.True(sharing.IsChecked);
            bool Saved(bool expected)
            {
                var path = Path.Combine(CorePaths.UserData, "settings.json");
                try
                {
                    // Let the atomic writer replace the file while this cross-platform test reads it.
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    return (bool?)JObject.Parse(reader.ReadToEnd())["DiscordShareAchievements"] == expected;
                }
                catch (IOException) { return false; }
            }
            await WaitFor(() => Saved(true));
            // The question promises this switch can turn sharing off again; prove the whole path.
            sharing.IsChecked = false;
            Assert.False(s.DiscordShareAchievements);
            await WaitFor(() => Saved(false));
        }
    });

    [Fact]
    public Task GameParksOneOfferAndInboxClickAsksEvenWhileGameRemains() => Run(async (shell, s) =>
    {
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("test-game", _ => { }, () => true) };
        await SignIn(shell);
        Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
        var row = Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "discord-share-achievements");
        StartupLadder.Inbox.Open(row);
        await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().Any());
        Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key == row.Key);
        var offer = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
        offer.Close(false);
        Dispatcher.UIThread.RunJobs();
        Assert.False(s.DiscordShareAchievements);
    });


    private sealed class HeldProfile(Task release) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await release;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }

    [Fact]
    public Task SlowCloudProfileDoesNotDelayTheSharingQuestion() => Run(async (shell, s) =>
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AccountSeed.NewV2 = () => new V2AuthService(() => s, new HeldProfile(release.Task));
        var signIn = SignIn(shell);
        try
        {
            await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().Any());
            Assert.False(release.Task.IsCompleted);
            Assert.Single(shell.OwnedWindows.OfType<MessageDialog>()).Close(false);
        }
        finally
        {
            release.TrySetResult();
            await WaitFor(() => signIn.IsCompleted || shell.OwnedWindows.OfType<MessageDialog>().Any());
            foreach (var offer in shell.OwnedWindows.OfType<MessageDialog>().ToArray()) offer.Close(false);
            await signIn;
        }
    });

    [Fact]
    public Task HiddenPanelParksOnlyOneRowAndDoesNotEnableSharing() => Run(async (shell, s) =>
    {
        shell.Hide();
        await shell.OfferAchievementSharingAfterDiscordLink();
        await shell.OfferAchievementSharingAfterDiscordLink();
        Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
        Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "discord-share-achievements");
        Assert.False(s.DiscordShareAchievements);
    });

    [Fact]
    public Task SignInWithoutAuthenticatedDiscordDoesNotAsk() => Run(async (shell, s) =>
    {
        AccountSeed.Discord!.Logout();
        await SignIn(shell);
        Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
        Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key == "discord-share-achievements");
        Assert.False(s.DiscordShareAchievements);
    });

    [Fact]
    public Task ClosingTheOfferDoesNotOptIn() => Run(async (shell, s) =>
    {
        var signIn = SignIn(shell);
        await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().Any());
        Assert.Single(shell.OwnedWindows.OfType<MessageDialog>()).Close();
        await signIn;
        Assert.False(s.DiscordShareAchievements);
    });

    [Fact]
    public Task AlreadySharingDoesNotOfferAgain() => Run(async (shell, s) =>
    {
        s.DiscordShareAchievements = true;
        await SignIn(shell);
        Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
        Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key == "discord-share-achievements");
        Assert.True(s.DiscordShareAchievements);
    });
}
