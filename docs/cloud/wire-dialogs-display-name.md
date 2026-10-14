# wire-dialogs-display-name: DisplayNameDialog stub -> wired

Ledger row `dialogs-display-name` goes from stub to **wired**. Both items the row listed as missing are now ported.

- **Welcome mode now has its caller.** New `CCP.Avalonia/Views/Windows/MainShellWindow.PendingRegistration.cs` ports WPF `MainWindow.CheckPendingRegistrationAsync` together with `AccountService.HandlePostAuthAsync` and `PromptForRegistrationAsync`.
  - The shell's `Opened` arms it, as WPF arms it at the end of `MainWindow_Loaded`. It waits 2 s, and closing the shell cancels the wait.
  - It is skipped when the user already has a V2 `UnifiedId`. Otherwise, for a provider that still needs registration (Patreon first, then Discord), it links to the other signed-in provider's account or refuses a different one; a returning user gets no prompt; a same-email hint auto-links or asks before creating a new account; then comes the name loop (the server's refusal is shown, a claim is offered, Cancel signs the provider out).
  - WPF `MessageBox` becomes `MessageDialog`. `LoginDialog.ConfirmCreateNewDespiteExistingAccount` is now `internal static` so the login dialog and the shell share it, as they do in WPF.
- **New `CCP.Core/Services/Account/LegacyAccountApi.cs`.** It holds the legacy V1 endpoints `/auth/lookup`, `/auth/register`, `/auth/link-provider` and `/auth/claim`, moved out of WPF `AccountService`. WPF `AccountService` now calls this Core copy, and its private V1 HTTP methods and result classes are deleted.
- **New `CCP.Core/Services/Account/AuthRecovery.cs`, the 401 path.** It is imported and adapted from parity/trunk **bb302d777** ("sign-in recovery - the 401 path and the merged-account swap"); only the AuthRecovery part was taken, and the unused status-code-only overload was dropped.
  - `V2AuthService.ChangeDisplayNameAsync` now recovers the session on a 401 but does not retry, as in WPF (`ProfileSyncService.cs:4159`).
  - `V2AuthService.DeleteAccountAsync` retries once, and only after a real recovery left a token in hand (WPF `:4207`, #879).
  - `AccountSeed.Seed` sets up two hooks: a Patreon-then-Discord forced re-validate, and a heartbeat restart after a recovery.
  - WPF `ProfileSyncService.HandleUnauthorizedAsync` now delegates to Core `AuthRecovery`, and about 200 lines of WPF copy are removed. WPF `App.xaml.cs` sets up the same two hooks and switches back to the UI thread for both.
- Panic: WPF's panic closes none of these dialogs, so nothing new is registered in PanicSurfaces. Ledger: only row `dialogs-display-name` was updated.

## Verification limits

- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- Builds succeeded with no errors: CCP.Core, CCP.Avalonia, every Linux test csproj in build.yml, and the WPF app plus `Tests/ConditioningControlPanel.Tests` (with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`).
- `dotnet test Tests/CCP.Core.Tests`: 2784 passed, 2 failed, 6 skipped. The 2 failures are `LoopbackOAuthTests.GoodState_OnEitherLoopback...([::1])` (no IPv6 loopback in this container) and `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (it runs as root, so the folder can be deleted). Both fail the same way on the untouched base.
- `dotnet test Tests/CCP.Avalonia.Tests --filter AccountSeedTests|LoginDialog|DisplayName|PanicSurface`: 50 passed, 0 failed. This includes the new `PendingRegistrationTests` and the existing `NameDialogsTests`.
- Tests checked to fail before the change:
  - `AuthRecoveryTests` (Core): `Delete401_RecoversThenRetriesOnceWithTheRecoveredToken`, `Delete401_WithoutARecovery_DoesNotRetryAndShowsTheServersText` and `Rename401_RecoversTheSessionButDoesNotRetry` all fail with the V2AuthService change reverted.
  - `PendingRegistration_IsArmedByTheShellOpening` fails with the `Opened` hook removed.
  - `Seed_Wires401Recovery_ToTheSignedInProvidersRevalidate` fails with the AccountSeed re-validate seed removed.
- **Compiled but not run:** the WPF head's switch to Core `LegacyAccountApi` and `AuthRecovery`, including the `App.xaml.cs` hooks and their switch back to the dispatcher. It builds on Linux but only runs on Windows.
- **Not exercised against the real server:** the V1 endpoints and restore-session. All tests use fake handlers.
- No visual change: the dialog's markup is unchanged and it was already in `--render-all`, so no render was produced.
