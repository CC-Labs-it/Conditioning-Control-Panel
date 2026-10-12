# descent-core-a: first Descent rules move to CCP.Core

Unit A of the `ConditioningControlPanel/Services/Descent` → `CCP.Core/Services/Descent` move.
Base: `avalonia-port/feature-day-log-core`.

## Already in Core before this unit (no second copy, nothing to do)

DescentBarkPolicy, DescentCountdownService, DescentEpochs, DescentFuseCopy, DescentFuseHandoff,
DescentFusePhase, DescentFuseSequence, DescentMigrationAck, DescentMigrationChoices (incl.
DescentCycleXp), DescentModels, DescentPostZeroRetry, DescentReader, DescentReceipt,
DescentStageCopy, SpiralFirstLightTimeline, SpiralRoom, VatFillCoordinator. None of the 11 files
left in the WPF folder defined a type that also existed in Core.

## What changed

- `git mv` of the four pure rules (same four that parity/trunk moved in `01debcf61` and
  `9262a7520`; imported adapted, trunk's later additions to DescentMigration such as
  `SpiralWithheldFor`/`ApplyRestore` are NOT taken here):
  - `VatFaucetHold.cs` (the faucet hold arithmetic): unchanged, it was already pure.
  - `DescentRefreshGate.cs` (refresh coalescing gate): unchanged except one cref to the
    WPF-only `DescentService` turned into `<c>`.
  - `VatPourLedger.cs`: `AppSettingsVatPourLedger` now reads/writes through `CoreSettings.Current`
    / `CoreSettings.Save()` and logs through `Serilog.Log` instead of `App.Settings` / `App.Logger`.
    WPF already seeds `CoreSettings.ServiceProvider = () => Settings` and `Log.Logger` in App
    startup, so the shipping ledger hits the same settings object and the same logger.
  - `DescentMigration.cs` (offer, relevel result, `Resolve`, cycle bonus): `ActiveCycleXpBonus`
    reads `CoreSettings.Current`; `Resolve` calls `XpCurve.DeriveLevelFromLifetimeXp` (the Core
    function `ProgressionService.DeriveLevelFromLifetimeXp` already forwarded to).
- Namespaces are unchanged (`ConditioningControlPanel.Services.Descent`) and WPF references
  CCP.Core (InternalsVisibleTo covers the `internal` gate), so WPF callers
  (MainWindow.ProfileFaucet/ProfileVat/ProfileCard, ProgressionService, ProfileSyncService) need no
  edits. No embedded resources moved, so no core-guards baseline entry.
- Tests moved to `Tests/CCP.Core.Tests`: `VatFaucetHoldTests`, `DescentRefreshGateTests`,
  `DescentMigrationChoiceTests` (its `ProgressionService.CumulativeXpToReachLevel` / `CurveEpoch*`
  aliases retargeted to the identical `XpCurve` members).
- New `DescentCoreSeamTests`: the CoreSettings-backed pour ledger (account- and UTC-day-scoped
  watermark) and `ActiveCycleXpBonus` against a seeded SettingsService in the sandboxed profile.
- `CCP.Avalonia/Views/Windows/MainShellWindow.ProfileFaucet.cs` header: VatFaucetHold is no
  longer a blocker (MotionFx still is).

## Next units

- **descent-core-b**: `DescentService` (needs a dispatcher seam + `App.ProfileSync` provider;
  trunk `01debcf61` has a Core version) together with `DescentMigrationService`
  (`App.ProfileSync`, `App.AvatarWindow`, `App.Descent`, WPF Dispatcher).
- **descent-core-c**: `DescentHeartbeat` and `DescentRoomSfx` (`App.Audio` → CoreAudio,
  `App.DescentCountdown`, `App.Settings` → CoreSettings), `DescentBarkWatcher` (`App.Bark`,
  `App.UserDataPath` → CorePaths).
- **descent-core-d**: `DescentShowDirector` (DispatcherTimer → shared clock seam, `App.*` hub).
- Stays in WPF: `DescentFuseChrome` (System.Windows.Media brushes; head UI).
- Avalonia UI after those: the faucet port in `MainShellWindow.ProfileFaucet.cs` (still blocked
  on a MotionFx gate).

## Verification limits

- `dotnet build -c Release` succeeded for CCP.Core, CCP.Avalonia, Tests/CCP.Avalonia.Tests and
  every Linux test project in `.github/workflows/build.yml` (Core.Settings, Core.Content,
  Avalonia.Consent/Language/CatalogueStartup/Scheduler/Report/GeneralSettings/Snapshot).
- `Tests/ConditioningControlPanel.Tests` (which builds the WPF head) also built, with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1197 passed, 5 skipped, 2 failed. The 2 failures
  (`ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_…`,
  `LoopbackOAuthTests.GoodState_OnEitherLoopback_…("[::1]")`) fail identically on the base
  branch in this container (runs as root, so a read-only folder still deletes; no IPv6 loopback).
  The moved/new Descent tests: 60/60 passed.
- Compiled but not run: the WPF head and `Tests/ConditioningControlPanel.Tests` (Windows runtime).
  WPF callers were not edited; they resolve the moved types from CCP.Core by the same namespace.
- No Avalonia UI changed (one header comment), so no Avalonia test classes were run.
