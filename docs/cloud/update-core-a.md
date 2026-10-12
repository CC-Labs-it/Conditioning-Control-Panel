# update-core-a: Core move, unit A (`ConditioningControlPanel/Services/Update`)

Base: `avalonia-port/lang-italian`. Branch: `avalonia-port/cloud-update-core-a`.

## Inventory of `ConditioningControlPanel/Services/Update` before this unit

| WPF file | State on the base |
|---|---|
| `UpdateService.cs` - release parsing, Setup-asset pick, skip/attempt markers, helper-script text | already moved to `CCP.Core/Services/Update/ReleaseFeed.cs`; WPF routes to it |
| `UpdateService.cs` - `IsTransientNetworkError`, `NeedsElevationToInstall`, `GetRunningInstallDir` | **two copies**: WPF and `CCP.Avalonia/Platform/AppUpdater.cs` (Avalonia's elevation probe had drifted: no logging, catch-all) |
| `NativeBundleGuard.cs` - extraction-root rule, cooldown stamp, repair helper `.cmd` text | WPF only, not in Core |
| `UpdateService.cs` - registry reads, download, WebView2 cleanup, exit, .NET temp-cache sweep | WPF only |

Neither file is pure as a whole (registry, `Process`, `MessageBox`, `App.*`), so nothing was `git mv`'d
whole; the pure rules were moved out and both heads routed to the single Core copy. The one whole-file
move is the pure test class (`git mv`, shows as a rename under `git diff -M`).

## What changed

- `CCP.Core/Services/Update/ReleaseFeed.cs`: gains `IsTransientNetworkError`, `InnoInstallDirOf(exePath)`
  and `NeedsElevationToInstall` (the WPF rule, Serilog `Log` instead of `App.Logger`).
- `ConditioningControlPanel/Services/Update/UpdateService.cs`: its three private copies deleted, call sites
  route to `ReleaseFeed`; the unused `DidUpdateSucceed` / `UnknownExitCode` wrappers (only the moved test
  used them) dropped. The exe path passed is still `Process.GetCurrentProcess().MainModule?.FileName`, so
  WPF behaviour is unchanged.
- `CCP.Avalonia/Platform/AppUpdater.cs`: its copies (`IsTransientNetworkError`, `NeedsElevation`,
  `RunningInstallDir`) deleted, routed to `ReleaseFeed`. Behaviour change on Avalonia only: the elevation
  probe now logs a warning on an unexpected exception, as WPF does.
- New `CCP.Core/Services/Update/NativeBundleRepair.cs`: the rules half of `NativeBundleGuard` -
  `ExtractionRoot` (entry-assembly location passed in by the head, since Core may not call
  `Assembly.GetEntryAssembly`), the per-version cooldown stamp (`RepairedRecently` / `WriteStamp` /
  `ClearStamp`, user-data folder passed in like the `ReleaseFeed` markers) and `BuildRepairHelperScript`.
  `NativeBundleGuard` keeps the Skia probe, `Process.Start` of the helper and the give-up `MessageBox`.
- No seams were needed beyond parameters: every moved rule takes its user-data path / exe path / version
  as an argument, the same shape `ReleaseFeed` already uses. No embedded resources moved, so no
  core-guards baseline entry.
- Tests:
  - `Tests/ConditioningControlPanel.Tests/UpdateAttemptOutcomeTests.cs` -> `Tests/CCP.Core.Tests/` (rename),
    now against `ReleaseFeed.DidUpdateSucceed`.
  - `AppUpdaterTests.TransientErrorsFollowWpfRules` moved from `Tests/CCP.Avalonia.Tests` into
    `ReleaseFeedTests` (plus a `TaskCanceledException` case).
  - New `ReleaseFeedTests`: Inno install dir needs `unins000.exe` next to the exe; missing/writable dir
    needs no elevation and the probe leaves no file; read-only dir needs elevation.
  - New `NativeBundleRepairTests`: helper script byte-identical to a golden printed by the PRE-MOVE
    `NativeBundleGuard` line array (`Fixtures/Update/native_repair_spaces_premove.cmd.txt`, CRLF, covered
    by the existing `*.cmd.txt -text` attribute); stamp cooldown is per-version and expires; an
    unreadable stamp never blocks; extraction root only for a single-file bundle with an existing cache,
    env var wins over temp.
- Trunk (`parity/trunk`): nothing imported. Its only delta in this area (`AppUpdater` server-banner
  fallback in `ManualCheckAsync`, `AppIdentity.InstalledExeName`) is Avalonia UI behaviour, out of scope
  for a Core move - listed below. No trunk commits to credit.

## Next units

- **update-core-b**: the .NET single-file temp-cache sweep from `UpdateService` (`CleanupOldPackages`,
  parked `.stale-` folders, live-extraction-folder resolution, `GetDirectorySize`) into Core, with
  `OtherInstancesRunning` as a head seam.
- **update-core-c**: `NativeBundleGuard` for the Avalonia head (Skia probe + `NativeBundleRepair` + an
  Avalonia give-up dialog), Windows-only behind `OperatingSystem.IsWindows()`.
- **update-avalonia-banner**: import trunk's server-banner fallback in `AppUpdater.ManualCheckAsync` and
  `AppIdentity.InstalledExeName` (trunk merge `0c6d2cba6`).
- Registry install path/version (`GetInstalledPath`/`GetInstalledVersion`) stays per head: Windows-only
  API, Core guard forbids `Registry`.

## Verification limits

- SDK: .NET 10.0.112 from the Ubuntu 24.04 archive (`builds.dotnet.microsoft.com` is blocked by this
  sandbox's egress policy, so `dotnet-install.sh` could not run).
- `dotnet build`: CCP.Core, CCP.Avalonia, Tests/CCP.Avalonia.Tests and every Linux test csproj in
  `build.yml` (CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot,
  Core.Content, Core.Settings, Core) - all 0 errors.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false` - 0 errors, so the WPF head and its tests
  compile with the routing changes. **Compiled, not run**: WPF tests, `NativeBundleGuard`, the WPF and
  Avalonia Windows install path (`InstallAsync` -> helper `.cmd`), the repair helper itself.
- `bash .github/scripts/core-guards.sh` - All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests` - 1176 passed, 5 skipped, 2 failed. Both failures reproduce
  identically on the untouched base in this sandbox and are environmental:
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (runs as root, read-only bits do not
  bind) and `LoopbackOAuthTests.GoodState_OnEitherLoopback...([::1])` (no IPv6 loopback).
  Focused run (`ReleaseFeedTests|NativeBundleRepairTests|UpdateAttemptOutcomeTests`): 38/38 passed.
- `ReleaseFeedTests.ReadOnlyInstallDirNeedsElevation` returns early under root or on Windows (mode
  bits do not bind there), so here it passed vacuously; it asserts on the non-root Linux CI runner.
- `dotnet test Tests/CCP.Avalonia.Tests --filter AppUpdaterTests` - 8/8 passed (headless).
- Pure move/routing: no behaviour fix, so there is no fails-before test; the helper-script golden is
  byte-compared against output of the pre-move code.
