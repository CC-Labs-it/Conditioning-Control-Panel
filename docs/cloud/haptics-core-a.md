# haptics-core-a: Services/Haptics Core move, unit A (DtrhHapticDirector)

Base: `avalonia-port/possession-core-a`. Branch: `avalonia-port/cloud-haptics-core-a`.

## What was already in Core

Most of `ConditioningControlPanel/Services/Haptics` had moved before this unit and is shared by both heads:
`ButtplugProviderV2`, `ButtplugUrl`, `FunScript`, `FunScriptService`, `HapticService`, `IHapticProvider`,
`LockdownService`, `LockdownStrictHold`, `LovensePatterns`, `LovenseProvider`, `LovenseProviderV2`,
`LovenseToyEventsClient`, `ToyInputService`, and `Core/` (`HapticContracts`, `HapticDeviceManager`,
`HapticMixer`, `HapticPatterns`, `HapticTemperament`, `MockProviderV2`). The seams this unit needs
(`CoreHaptics.Service`, `CoreSettings.ServiceProvider`) were already seeded in WPF `App` startup
(`App.xaml.cs:310`, `:3066`), so WPF needs no edit.

## What changed

- `git mv ConditioningControlPanel/Services/Haptics/DtrhHapticDirector.cs -> CCP.Core/Services/Haptics/`.
  The DtRH (Down the Rabbit Hole) haptic director: accent table, tiers, coalescing, gaps, density, ambient
  depth-gauge curve and its 0.06..1.0 clamp, the stop-on-disable rule. Only changes: `App.Settings` ->
  `CoreSettings.Service`, `App.Haptics` -> `CoreHaptics.Service`, `App.Logger` -> Serilog `Log`; the
  ambient curve became `AmbientTarget` (same arithmetic), plus three internal test readers.
  Adapted from parity/trunk `800f7722d` (which added a Core copy beside the WPF one); here it is a move,
  so there is one copy. WPF's `DtrhHostService` calls it unchanged (same namespace, Core has
  `InternalsVisibleTo("ConditioningControlPanel")`).
- `git mv Tests/ConditioningControlPanel.Tests/DtrhPayloadAccentTests.cs -> Tests/CCP.Core.Tests/` (namespace only).
- New `Tests/CCP.Core.Tests/DtrhHapticDirectorTests.cs` from parity/trunk `800f7722d`: the ambient curve
  (incl. the Lovense <=0.05 cutoff clamp and the 1.0 cap) and that a head with no device is a quiet no-op
  through the whole lifecycle. No device or network I/O: `CoreHaptics.Service = null`.
- No WPF source-scan test read this file by path, so no `SourceRoots` change was needed. No embedded
  resources, no core-guards baseline entry.

## Next units (not in this branch)

- **haptics-core-b**: `BackRoomHapticDirector` (its `Limiter`, `PriorityFor`, `DurationFor` and the
  pulse rules) - needs `BackRoomBridge` / `BackRoomHaptic` (`Services/BackRoom`) in Core first.
- **haptics-core-c**: `LockdownDoseKeeper` - split the pure dose rules (`DoseFeature` table,
  `WantedFor`, `KeysOn`, `DoseIsEmpty`, `CountsAsOffWallDose`, recovery-file shape) into Core and keep
  the MainWindow/Dispatcher enforcement in the head behind a seam (cf. parity/trunk `4b79a2007`).
  Its WPF source-scan tests (`LockdownDoseKeeperTests`) must switch to `SourceRoots`.
- **haptics-avalonia-dtrh**: wire the Avalonia DtRH host to `DtrhHapticDirector`
  (parity/trunk `800f7722d`, `GameWindow.Dtrh.*` + `DtrhHapticWireTests`).
- Stays in WPF: `Core/MockToast` (WPF window). `ButtplugProvider` (v1) and `MockHapticProvider` have no
  callers on this base (V2 providers replaced them); candidates for deletion, not for a move.

## Verification limits

- `dotnet build` (Release) succeeded: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.VR, Tests/CCP.Core.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests,
  CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests,
  CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests.
- Tests/ConditioningControlPanel.Tests (which builds the WPF app) compiled with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: Build succeeded.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1237 passed, 5 skipped, 2 failed. Both failures are environmental
  and fail identically on the base branch (checked in a separate worktree):
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...` (the container runs as root, so a
  read-only folder is still deletable) and `LoopbackOAuthTests.GoodState_OnEitherLoopback_...("[::1]")`
  (no IPv6 loopback in the container). `--filter Dtrh`: 15/15 passed.
- Not run: the WPF app and `Tests/ConditioningControlPanel.Tests` (Windows-only testhost); compiled only. No Avalonia UI changed, so no Avalonia
  tests were run beyond building.
