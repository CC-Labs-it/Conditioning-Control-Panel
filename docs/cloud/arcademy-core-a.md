# arcademy-core-a: Arcademy meta store and cloud mirrors move to CCP.Core

Unit A of the trunk 'Arcademy' import. It covers Core rules only, with no Avalonia UI. Base: `avalonia-port/lag-fixes-a`.

## What changed

- **`git mv` into `CCP.Core/Services/Arcademy/`** (these show as renames under `git diff -M`). `ArcademyEconomy` and `ArcademyPunchCards` were already in Core, so after this unit the whole persisted Arcademy state and its two server mirrors sit in one shared copy:
  - `ArcademyMetaStore.cs`: the `arcademy_meta.json` blob. It covers attendance, streaks, tardy slips, punch cards, the wallet ledger, parked mints, the page's write guard, atomic writes and `.bak` recovery.
  - `ArcademySyncService.cs`: the punch-card mirror (`/v2/arcademy/cards`).
  - `ArcademyWalletSyncService.cs`: the wallet mirror (import, class-ended mint, prize buy, parked-mint replay).
- **`App.*` references replaced with existing Core seams. No new seam types.**
  - `App.Logger` became Serilog `Log`. WPF already sets `Log.Logger` to the same logger.
  - `App.UserDataPath` became `CorePaths.UserData`.
  - `App.Settings.Current` became `CoreSettings.Current`.
  - `UpdateService.AppVersion` became `CoreReleaseContent.AppVersion`.
- **Contract D (409 merged account).** `MergedAccountRecovery` is App-bound and stays in WPF. Both mirrors now call one delegate, `ArcademySyncService.MergedRecovery`. WPF `App` sets it to `MergedAccountRecovery.TryHandle` next to the existing `V2AuthService.MergedRecovery` line. If no head sets it, contract D is not handled, the same as the other Core clients.
- **The meta store's debounce no longer needs a dispatcher.** The `DispatcherTimer` armed through `Application.Current.Dispatcher` is now a 500 ms `System.Threading.Timer`. The pattern comes from trunk `3e9c33a23`. Because the timer runs on a thread-pool thread, it and `ShutdownFlush` on the UI thread could now flush at the same time. `FlushSave` therefore runs under a write lock, so two flushes never share the `.tmp` file. The store also gets an internal constructor that takes a path, for tests (also from trunk `3e9c33a23`). `WriteAtomic` creates the folder the file is in, not the global user-data root.
- **Tests.**
  - `ArcademyTardySlipTests` moved from `Tests/ConditioningControlPanel.Tests` to `Tests/CCP.Core.Tests` (pure `ArcademyEconomy`).
  - New `ArcademyMetaStoreTests` runs the code paths WPF could only check by scanning source:
    - two slips carry a streak over two missed nights;
    - a third missed night breaks the streak and spends nothing;
    - the page cannot write a host-owned key and gets the stored value back;
    - a write reaches disk on the debounce without a flush;
    - a truncated main file falls back to `.bak`.
- **Trunk credit.** `3e9c33a23` (feat(parity arcademy): meta store, wallet sync, card sync and presence services in Core) was used as the shape hint. The code was adapted from our own WPF files, not copied from trunk.

## Next units (not in this branch)

- **arcademy-core-b:** `ArcademyPresenceService` (settings `PropertyChanged` hook, poll timer), `ArcademyAvatarCache` (needs a Core image-encode seam in place of WPF `BitmapSource`), and the WebView-free rules inside `ArcademyHostService` (6k lines: init payload, class payout, till, panic ladder, media batch rules), split out of the WebView2 plumbing. Trunk hints: `da4ee83f2`, `5d8dd2620`, `0108c487c`, `f80072ded`.
- **arcademy-host-window:** the Avalonia host window and bridge on the shared WebView seam (`GameWindow.Arcademy*` on trunk).
- **arcademy-game-ui:** the door, Lab gating, panic and profile wiring in the Avalonia shell, plus the remaining WPF Arcademy tests (`ArcademyDoorOpenTests`, `ArcademyAnimatedWebpHintTests`). Their pure parts move to Core when the host rules do.

## Verification limits

- **Built, all passing:** CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.VR, and the Linux test projects listed in `build.yml`:
  - CCP.Core.Tests, CCP.Core.Settings.Tests and CCP.Core.Content.Tests;
  - CCP.Avalonia.Tests, plus the Consent, Language, CatalogueStartup, Scheduler, Report, GeneralSettings and Snapshot test projects.
- **WPF test project:** `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` succeeds on Linux. That build compiles the WPF head, including the new `App.xaml.cs` line. The WPF tests were not run, because they need Windows.
- **`bash .github/scripts/core-guards.sh`:** all Core guards passed.
- **`dotnet test Tests/CCP.Core.Tests` (`DiffEngine_Disabled=true`):** 1155 passed, 5 skipped, 2 failed. Both failures come from this container, not this change, and neither touches Arcademy:
  - `LoopbackOAuthTests ... host: "[::1]"`: the container has no IPv6 loopback ("No data available ([::1])").
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`: the container runs as root, so the "cannot delete" lock never takes.
- **Arcademy tests:** `--filter Arcademy` on CCP.Core.Tests passed 22 of 22. In CCP.Avalonia.Tests, `ExclusivesVault*` (the only class there that touches Arcademy on this base) passed 5 of 5.
- **Compiled but not run:** the WPF head (Windows only). That includes the live debounce, flush and teardown path inside `ArcademyHostService` and the contract D swap. The mirrors' HTTP paths were not exercised against the real proxy, because sandboxed tests never reach a server.
