# account-core-a: Core move, unit A (Services/Account: merged-account recovery)

Base: `avalonia-port/lang-italian`. Branch: `avalonia-port/cloud-account-core-a`.

## Inventory: ConditioningControlPanel/Services/Account vs CCP.Core

Already in `CCP.Core/Services/Account` before this unit (WPF routes to these, no second copy):
`DiscordAccount`, `EntitlementTierRule`, `LoopbackOAuth`, `PatreonGrantHealth`, `PatreonReconnectRule`,
`ProfileAdopt`, `ProgressionClear`, `ProviderLinkResponseRules`, `ProviderSubscription`, `SyncBody`,
`SyncPush`, `V2AuthService`, `V2DeviceCodeService`.

Still WPF-only before this unit: `AccountService`, `DiscordRichPresenceService`, `DiscordService`,
`EntitlementTierSync`, `MergedAccountRecovery`, `PatreonService`, `V2AuthServiceHead`.

## What changed

- **Moved** `ConditioningControlPanel/Services/Account/MergedAccountRecovery.cs` ->
  `CCP.Core/Services/Account/MergedAccountRecovery.cs` (`git mv`, shows as a rename). Contract D
  (409 `merged` -> adopt the canonical unified id once, drop the tombstone token, re-sign in) now
  has one copy, shared by both heads. `MergedAccountResponse` and `MergedSwapPolicy` are unchanged.
- The runner's `App.*` calls became Core seams, following trunk `bb302d777`
  ("feat(parity account): sign-in recovery", CodeBambi), adapted to our tree:
  `StopSync`, `StampQuests`, `Reauthenticate`, `ReloadProfile`, `Finished`. Settings and identity go
  through `CoreSettings` / `CoreAccount.UnifiedUserId`, and logging through Serilog's static `Log`.
  Unseeded seams are skipped, so the identity still moves.
  The unused `PeekDecision` test seam was dropped (nothing called it).
- **WPF** keeps the same behaviour. The new `Services/Account/MergedAccountRecoveryHead.cs` seeds the
  seams from `App` startup, next to the existing `V2AuthService.MergedRecovery` seed. Its seams cover
  the timer stop and loaded-profile reset, the quest restamp, `App.ReauthenticateAfterMergedSwapAsync`,
  the profile reload and heartbeat, and the account-UI repaint and Inbox sign-in offer, which keep the
  WPF text verbatim. All WPF callers (`MergedAccountRecovery.TryHandle(Async)`) keep their names and namespace.
  One ordering difference: the loaded-profile reset (`_hasLoadedProfile = false`, prize feed clear)
  now runs with the timer stop, before the identity write instead of after it, matching trunk's
  shape. Both are local resets with the timers already stopped, so the outcome is the same.
- **Tests** `Tests/ConditioningControlPanel.Tests/MergedAccountRecoveryTests.cs` ->
  `Tests/CCP.Core.Tests/MergedAccountRecoveryTests.cs` (rename). Added `MergedSwapRunTests`: the
  swap through the seams (order, once per canonical), the no-provider path (signed out, sign-in
  offered, no reload), and throwing seams that still never stop the identity swap. The first two
  are ported from trunk `bb302d777`'s `AuthRecoveryTests`.
- Avalonia: only the stale `FriendsHead` ponytail comment was reworded (no UI, no seeding).
- No embedded resources moved, so no core-guards baseline entry was needed.

## Next units (not in this branch)

1. **account-core-b**: seed `MergedAccountRecovery` (and `V2AuthService.MergedRecovery`,
   `FriendsApi`/`AiService` hooks) on the Avalonia head, which needs a re-auth seam and a shell sign-in
   offer (trunk `bb302d777` `CCP.Avalonia/Platform/AccountSeed.cs`, `MainShellWindow.Login.cs`).
2. **account-core-c**: `EntitlementTierSync` (126 lines). The rule is already Core
   `EntitlementTierRule`. Move the live half, with the WPF Dispatcher going to `CoreDispatch` and
   `App.Patreon.NotifyEntitlementRaised` going to a seam.
3. **account-core-d**: `AuthRecovery` (401 path, from WPF `ProfileSyncService`, trunk `bb302d777`).
4. Later: pure rules inside `PatreonService`, `DiscordService`, `DiscordRichPresenceService`
   (Discord IPC stays head-side behind a seam) and `AccountService` (takes a WPF `Window` owner: only
   its decisions can move). `V2AuthServiceHead` stays: it is the WPF call-site shim over Core
   `ProfileAdopt`.

## Verification limits

Run on Linux (Ubuntu 24.04, .NET SDK 10.0.112 from the Ubuntu archive; `builds.dotnet.microsoft.com`
is blocked by this sandbox's egress policy), with `DiffEngine_Disabled=true`:

- `dotnet build` succeeded for CCP.Core, CCP.Avalonia, CCP.Core.Tests, CCP.Core.Settings.Tests,
  CCP.Core.Content.Tests, CCP.Avalonia.Tests, and the Consent, Language, CatalogueStartup, Scheduler,
  Report, GeneralSettings and Snapshot test projects.
- The WPF test project (`Tests/ConditioningControlPanel.Tests`, which builds the WPF head) also built with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1198 passed, 5 skipped, 2 failed. Both failures also fail on the
  untouched base (`avalonia-port/lang-italian`, re-run with this change stashed) and come from the sandbox:
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...` fails because the tests run as root
  and root can delete the folder, and `LoopbackOAuthTests.GoodState_OnEitherLoopback_...("[::1]")` fails
  because the kernel has no IPv6. The moved and new tests (`MergedAccountRecoveryTests`,
  `MergedSwapRunTests`) and `V2AuthServiceWireTests` passed: 52/52.
- `dotnet test Tests/CCP.Avalonia.Tests --filter AccountSeedTests|Friends|CloudReadOnlyTests`: 61/61 passed.

Compiled but not run: the WPF head, including `MergedAccountRecoveryHead` and the `App` startup seed
(Windows-only, it cannot run here). The WPF unit tests were not run. A real merged 409 against the live
server was not exercised; the swap is covered only through the seams.
