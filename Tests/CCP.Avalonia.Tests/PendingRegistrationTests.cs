using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
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

/// <summary>
/// DisplayNameDialog's welcome mode through its one WPF caller, MainWindow CheckPendingRegistrationAsync ->
/// AccountService.HandlePostAuthAsync -> PromptForRegistrationAsync, over the legacy V1 endpoints on a fake
/// proxy (never the real server). The provider is a real Core ProviderSubscription whose validate said
/// needs_registration.
/// </summary>
public sealed partial class AccountSeedTests
{
    private sealed class V1Wire : HttpMessageHandler
    {
        public readonly List<(string Path, JObject? Body, string? Bearer)> Seen = new();
        public readonly Dictionary<string, Queue<(HttpStatusCode, string)>> Routes = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var text = r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct);
            var path = r.RequestUri!.AbsolutePath;
            Seen.Add((path, text.Length == 0 ? null : JObject.Parse(text), r.Headers.Authorization?.Parameter));
            var (status, json) = Routes.TryGetValue(path, out var q) && q.Count > 0 ? q.Dequeue() : (HttpStatusCode.NotFound, "{}");
            return new HttpResponseMessage(status) { Content = new StringContent(json) };
        }
        public void Answer(string path, params (HttpStatusCode, string)[] replies) => Routes[path] = new(replies);
    }

    /// <summary>A Patreon sign-in from before V2 whose validate says the server record still has no name.</summary>
    private async Task<(V1Wire Wire, MainShellWindow Shell)> PendingPatron(bool needsRegistration = true, TimeSpan? launchDelay = null)
    {
        SetupHeadless();
        var s = CoreSettings.Current;
        (s.UnifiedId, s.OfflineMode) = (null, true);   // offline: no startup polls while the shell opens
        _secrets["patreon_auth"] = JsonConvert.SerializeObject(new PatreonTokenData
        { AccessToken = "p-access", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(10) });
        var wire = new V1Wire();
        wire.Answer("/patreon/validate", (HttpStatusCode.OK,
            "{\"is_active\":false,\"tier\":0,\"is_whitelisted\":false,\"needs_registration\":" + (needsRegistration ? "true" : "false") + "}"));
        Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings(), wire),
                                     p => new DiscordAccount(() => p, () => new AppSettings())));
        await AccountSeed.Patreon!.ValidateSubscriptionAsync(forceRefresh: true);
        Assert.Equal(needsRegistration, AccountSeed.Patreon.NeedsRegistration);
        wire.Seen.Clear();
        MainShellWindow.NewLegacyApi = () => new LegacyAccountApi(wire);
        MainShellWindow.PendingRegistrationDelay = launchDelay;   // null: the test drives the check, the launch check stays unarmed
        // An armed zero wait looks up during Show(), so its answer must already be on the wire.
        if (launchDelay is not null) wire.Answer("/auth/lookup", (HttpStatusCode.OK, "{\"exists\":false}"));
        var shell = new MainShellWindow();
        shell.Show();
        return (wire, shell);
    }

    private static void CloseAll(MainShellWindow shell)
    {
        foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
        shell.Close();
        MainShellWindow.NewLegacyApi = () => new LegacyAccountApi();
        MainShellWindow.PendingRegistrationDelay = TimeSpan.FromSeconds(2);
    }

    private static async Task<DisplayNameDialog> Welcome(MainShellWindow shell)
    {
        await Until(() => shell.OwnedWindows.OfType<DisplayNameDialog>().Any());
        return One<DisplayNameDialog>(shell);
    }

    private static void Enter(DisplayNameDialog dlg, string name)
    {
        dlg.FindControl<TextBox>("TxtDisplayName")!.Text = name;
        Dispatcher.UIThread.RunJobs();
        Click(dlg.FindControl<Button>("BtnConfirm")!);
    }

    [Fact]
    public Task PendingRegistration_AsksForAName_ShowsAServerRefusal_ThenRegisters() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, shell) = await PendingPatron();
            try
            {
                wire.Answer("/auth/lookup", (HttpStatusCode.OK, "{\"exists\":false,\"has_display_name\":false,\"needs_registration\":true}"));
                wire.Answer("/auth/register",
                    (HttpStatusCode.Conflict, "{\"success\":false,\"error\":\"Name taken\"}"),
                    (HttpStatusCode.OK, "{\"success\":true,\"unified_id\":\"u_new123\",\"display_name\":\"Bambi\"}"));
                var check = shell.CheckPendingRegistrationAsync(TimeSpan.Zero);

                // The welcome mode: its own title, the warning panel kept.
                var dlg = await Welcome(shell);
                Assert.Equal(Loc.Get("label_welcome_choose_your_display_name"), dlg.FindControl<TextBlock>("TxtTitle")!.Text);
                Assert.True(dlg.FindControl<Border>("WarningPanel")!.IsVisible);
                Enter(dlg, "  Nyx ");
                await Until(() => shell.OwnedWindows.OfType<MessageDialog>().Any());
                Assert.Contains("Name taken", Texts(One<MessageDialog>(shell)));
                One<MessageDialog>(shell).Close();

                // The loop asks again; the second name registers.
                dlg = await Welcome(shell);
                Enter(dlg, "Bambi");
                await Until(() => check.IsCompleted);

                Assert.Equal(new[] { "/auth/lookup", "/auth/register", "/auth/register" }, wire.Seen.Select(r => r.Path));
                Assert.All(wire.Seen, r => Assert.Equal("p-access", r.Bearer));
                Assert.Equal("patreon", (string?)wire.Seen[0].Body!["provider"]);
                Assert.Equal("Nyx", (string?)wire.Seen[1].Body!["display_name"]);
                Assert.Equal("Bambi", (string?)wire.Seen[2].Body!["display_name"]);
                Assert.Equal("u_new123", CoreAccount.UnifiedUserId);
                Assert.Equal("u_new123", AccountSeed.Patreon!.UnifiedUserId);
                Assert.Equal("Bambi", AccountSeed.Patreon.DisplayName);
                Assert.True(AccountSeed.Patreon.IsAuthenticated);
            }
            finally { CloseAll(shell); }
        });

    [Fact]
    public Task PendingRegistration_IsArmedByTheShellOpening() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, shell) = await PendingPatron(launchDelay: TimeSpan.Zero);   // armed, but no real timer (P08)
            try
            {
                await Welcome(shell);   // nobody called the check: Opened did
                Assert.Equal("/auth/lookup", wire.Seen.Single().Path);
            }
            finally { CloseAll(shell); }
        });

    [Fact]
    public Task PendingRegistration_Cancel_SignsTheProviderOut_AndRegistersNothing() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, shell) = await PendingPatron();
            try
            {
                wire.Answer("/auth/lookup", (HttpStatusCode.OK, "{\"exists\":false}"));
                var check = shell.CheckPendingRegistrationAsync(TimeSpan.Zero);
                Click((await Welcome(shell)).FindControl<Button>("BtnCancel")!);
                await Until(() => check.IsCompleted);

                Assert.Equal("/auth/lookup", wire.Seen.Single().Path);
                Assert.False(AccountSeed.Patreon!.IsAuthenticated);   // no cached token to auto-sync a nameless profile
                Assert.Null(CoreAccount.UnifiedUserId);
            }
            finally { CloseAll(shell); }
        });

    [Fact]
    public Task PendingRegistration_ReturningUser_AdoptsTheAccount_WithoutAPrompt() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, shell) = await PendingPatron();
            try
            {
                wire.Answer("/auth/lookup", (HttpStatusCode.OK,
                    "{\"exists\":true,\"unified_id\":\"u_old123\",\"display_name\":\"Old\",\"has_display_name\":true}"));
                await shell.CheckPendingRegistrationAsync(TimeSpan.Zero);

                Assert.Empty(shell.OwnedWindows);
                Assert.Equal("u_old123", CoreAccount.UnifiedUserId);
                Assert.Equal("Old", AccountSeed.Patreon!.DisplayName);
            }
            finally { CloseAll(shell); }
        });

    /// <summary>The 401 path's provider half (WPF ProfileSyncService.TryProviderRevalidateAsync), as Seed wires it.</summary>
    [Fact]
    public Task Seed_Wires401Recovery_ToTheSignedInProvidersRevalidate() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, shell) = await PendingPatron();
            try
            {
                wire.Answer("/patreon/validate", (HttpStatusCode.OK, "{\"is_active\":false,\"tier\":0}"));
                Assert.True(await AuthRecovery.ProviderRevalidate!());
                Assert.Equal("/patreon/validate", wire.Seen.Single().Path);

                AccountSeed.Patreon!.Logout();
                Assert.False(await AuthRecovery.ProviderRevalidate!());   // no provider to mint from
            }
            finally { CloseAll(shell); }
        });

    [Theory]
    [InlineData(true, "u_v2user")]   // a V2 user is never asked by the V1 path
    [InlineData(false, null)]        // nothing pending
    public Task PendingRegistration_Skips_AV2User_AndAUserWithNothingPending(bool needsRegistration, string? unifiedId) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, shell) = await PendingPatron(needsRegistration);
            try
            {
                CoreSettings.Current.UnifiedId = unifiedId;
                await shell.CheckPendingRegistrationAsync(TimeSpan.Zero);
                Assert.Empty(wire.Seen);
                Assert.Empty(shell.OwnedWindows);
            }
            finally
            {
                CoreSettings.Current.UnifiedId = null;
                CloseAll(shell);
            }
        });
}
