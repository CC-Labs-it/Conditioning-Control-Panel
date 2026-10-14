// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:3837-3891 (CheckPendingRegistrationAsync)
// and Services/Account/AccountService.cs:33-226 + 758-880 (HandlePostAuthAsync, PromptForRegistrationAsync):
// the only caller of DisplayNameDialog's welcome mode. A provider signed in before V2 whose server record still
// has no display name gets the name picker once the shell is up; the legacy V1 endpoints are Core LegacyAccountApi.
// Deviations: MessageBox becomes MessageDialog (ShowAsync for OK, ConfirmAsync for Yes/No); the provider
// services are AccountSeed's (WPF App.Patreon / App.Discord), and Patreon.DisplayName / Discord.CustomDisplayName
// are the same properties.

using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Tests only: the V1 client every pending-registration call uses.</summary>
        internal static Func<LegacyAccountApi> NewLegacyApi = () => new LegacyAccountApi();

        /// <summary>Tests only: the wait before the launch check; null arms nothing (a test drives the check itself).</summary>
        internal static TimeSpan? PendingRegistrationDelay = TimeSpan.FromSeconds(2);

        /// <summary>WPF MainWindow_Loaded's last line. The wait ends early when the shell closes.</summary>
        private void ArmPendingRegistration()
        {
            if (PendingRegistrationDelay is not { } delay) return;
            var closed = new CancellationTokenSource();
            Closed += (_, _) => closed.Cancel();
            _ = CheckPendingRegistrationAsync(delay, closed.Token);
        }

        /// <summary>
        /// WPF CheckPendingRegistrationAsync: after a wait for background authentication (2 s), a user with no
        /// unified id whose Patreon (preferred) or Discord validate said needs_registration is asked for a name.
        /// </summary>
        internal async Task CheckPendingRegistrationAsync(TimeSpan delay, CancellationToken closed = default)
        {
            try
            {
                await Task.Delay(delay, closed);

                // A V2 user already has a UnifiedId; the old validate endpoint does not know about V2 users.
                if (!string.IsNullOrEmpty(CoreSettings.Current.UnifiedId))
                {
                    Log.Debug("User already has UnifiedId, skipping pending registration check");
                    return;
                }

                var patreonNeedsReg = AccountSeed.Patreon is { IsAuthenticated: true, NeedsRegistration: true };
                var discordNeedsReg = AccountSeed.Discord is { IsAuthenticated: true, NeedsRegistration: true };
                if (!patreonNeedsReg && !discordNeedsReg) return;

                Log.Information("User needs to complete registration: Patreon={Patreon}, Discord={Discord}", patreonNeedsReg, discordNeedsReg);
                if (await HandlePostAuthAsync(patreonNeedsReg ? "patreon" : "discord"))
                {
                    Log.Information("Pending registration completed successfully");
                    await AccountSeed.LoadProfileAsync();
                    UpdateQuickLoginUI();
                }
                else Log.Warning("Pending registration failed or was cancelled");
            }
            catch (OperationCanceledException) { /* the shell closed during the wait */ }
            catch (Exception ex) { Log.Error(ex, "Error checking pending registration"); }
        }

        /// <summary>WPF AccountService.HandlePostAuthAsync: lookup -> link / returning user / auto-link -> name picker.</summary>
        private async Task<bool> HandlePostAuthAsync(string provider)
        {
            try
            {
                Log.Information("AccountService: Handling post-auth for {Provider}", provider);
                var (patreon, discord) = (AccountSeed.Patreon, AccountSeed.Discord);
                var accessToken = provider == "patreon" ? patreon?.GetAccessToken() : discord?.GetAccessToken();
                if (string.IsNullOrEmpty(accessToken))
                {
                    Log.Warning("AccountService: No access token available for {Provider}", provider);
                    return false;
                }

                var otherProvider = provider == "patreon" ? "discord" : "patreon";
                var otherProviderIsAuthenticated = provider == "patreon" ? discord?.IsAuthenticated == true : patreon?.IsAuthenticated == true;
                var existingUnifiedId = otherProviderIsAuthenticated ? CoreAccount.UnifiedUserId : null;
                var existingDisplayName = otherProviderIsAuthenticated ? CoreAccount.DisplayName : null;

                void Adopt(string? unifiedId, string? displayName)
                {
                    if (provider == "patreon" && patreon != null) (patreon.UnifiedUserId, patreon.DisplayName) = (unifiedId, displayName);
                    else if (provider == "discord" && discord != null) (discord.UnifiedUserId, discord.CustomDisplayName) = (unifiedId, displayName);
                }

                var api = NewLegacyApi();
                var lookup = await api.LookupAsync(provider, accessToken);
                if (lookup == null)
                {
                    Log.Warning("AccountService: Lookup failed for {Provider}", provider);
                    return false;
                }

                // Already signed in with the other provider: link, or refuse a different account.
                if (!string.IsNullOrEmpty(existingUnifiedId))
                {
                    if (lookup.Exists && lookup.UnifiedId == existingUnifiedId)
                    {
                        Adopt(existingUnifiedId, existingDisplayName);
                        return true;
                    }
                    if (lookup.Exists)
                    {
                        Log.Warning("AccountService: Conflict! {Provider} is linked to another account than {OtherProvider}'s. Rejecting.", provider, otherProvider);
                        await MessageDialog.ShowAsync(this, Loc.Get("account_cannot_link_title"),
                            Loc.GetF("account_conflict_different_profile", provider, lookup.DisplayName, existingDisplayName, otherProvider));
                        AccountSeed.LogoutProvider(provider);
                        return false;
                    }
                    var link = await api.LinkAsync(provider, accessToken, existingUnifiedId);
                    if (link.Success)
                    {
                        Adopt(existingUnifiedId, existingDisplayName);
                        Log.Information("AccountService: Successfully linked {Provider} to {UnifiedId}", provider, existingUnifiedId);
                        return true;
                    }
                }

                // Returning user: no prompt.
                if (lookup.Exists && lookup.HasDisplayName)
                {
                    CoreAccount.UnifiedUserId = lookup.UnifiedId;
                    Adopt(lookup.UnifiedId, lookup.DisplayName);
                    Log.Information("AccountService: Returning user found ({UnifiedId})", lookup.UnifiedId);
                    await AccountSeed.LoadProfileAsync();
                    return true;
                }

                // Same email as an existing account: link to it, or ask before minting a twin.
                if (lookup.CanAutoLink && !string.IsNullOrEmpty(lookup.AutoLinkUnifiedId))
                {
                    var link = await api.LinkAsync(provider, accessToken, lookup.AutoLinkUnifiedId);
                    if (link.Success)
                    {
                        CoreAccount.UnifiedUserId = link.UnifiedId;
                        Adopt(link.UnifiedId, lookup.AutoLinkDisplayName);
                        Log.Information("AccountService: Auto-linked {Provider} to {UnifiedId}", provider, link.UnifiedId);
                        await AccountSeed.LoadProfileAsync();
                        return true;
                    }
                    Log.Warning("AccountService: Auto-link {Provider} -> {UnifiedId} failed ({Error}); asking before minting a new account",
                        provider, lookup.AutoLinkUnifiedId, link.Error);
                    if (!await LoginDialog.ConfirmCreateNewDespiteExistingAccount(this, provider, lookup.AutoLinkDisplayName))
                    {
                        AccountSeed.LogoutProvider(provider);
                        return false;
                    }
                }

                return await PromptForRegistrationAsync(api, provider, accessToken, Adopt);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AccountService: HandlePostAuthAsync failed for {Provider}", provider);
                return false;
            }
        }

        /// <summary>
        /// WPF AccountService.PromptForRegistrationAsync: the welcome-mode DisplayNameDialog until a name registers
        /// (or is claimed). Cancel MUST sign the provider out, or its cached token would auto-sync a nameless
        /// profile on the next launch.
        /// </summary>
        private async Task<bool> PromptForRegistrationAsync(LegacyAccountApi api, string provider, string accessToken, Action<string?, string?> adopt)
        {
            while (true)
            {
                var dialog = new DisplayNameDialog();
                if (!await dialog.ShowDialogSafe<bool>(this) || string.IsNullOrWhiteSpace(dialog.DisplayName))
                {
                    Log.Information("AccountService: User cancelled registration, logging out {Provider}", provider);
                    AccountSeed.LogoutProvider(provider);
                    return false;
                }

                var name = dialog.DisplayName.Trim();
                var result = await api.RegisterAsync(provider, accessToken, name);
                if (result.Success)
                {
                    CoreAccount.UnifiedUserId = result.UnifiedId;
                    adopt(result.UnifiedId, result.DisplayName);
                    Log.Information("AccountService: Registered new user ({UnifiedId})", result.UnifiedId);
                    await AccountSeed.LoadProfileAsync();
                    return true;
                }

                if (!result.CanClaim)
                {
                    await MessageDialog.ShowAsync(this, Loc.Get("account_name_unavailable_title"), result.Error ?? Loc.Get("account_name_taken"));
                    continue;
                }

                // The name belongs to another provider account: offer to claim (link) it.
                if (!await MessageDialog.ConfirmAsync(this, Loc.Get("account_claim_title"), Loc.GetF("account_claim_prompt", name)))
                    continue;
                var claim = await api.ClaimAsync(provider, accessToken, name);
                if (!claim.Success)
                {
                    await MessageDialog.ShowAsync(this, Loc.Get("account_link_failed_title"), claim.Error ?? Loc.Get("account_link_failed_email_hint"));
                    continue;
                }
                CoreAccount.UnifiedUserId = claim.UnifiedId;
                adopt(claim.UnifiedId, name);
                Log.Information("AccountService: Claimed account ({UnifiedId})", claim.UnifiedId);
                await MessageDialog.ShowAsync(this, Loc.Get("account_success_title"), Loc.Get("account_accounts_linked"));
                await AccountSeed.LoadProfileAsync();
                return true;
            }
        }
    }
}
