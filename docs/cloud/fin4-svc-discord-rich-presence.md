# fin4-svc-discord-rich-presence

- Dependency-only audit; no production or test code changed. The existing single
  `svc-discord-rich-presence` ledger row remains `stub` with its Missing list.
- Base verified clean at `f890d76545f5ec7308f6d0a2b8c6a8ac610c2dcf`.
- Read-only trunk inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.
  No trunk code imported; this hash credits the dependency size comparison only.

## Remaining WPF behavior

All references below are pinned to the base commit above. The account shell
(`MainWindow/MainWindow.AccountShell.cs:279`) gates enablement on a linked account,
updates both surviving switches and the setting, and enables/disables RPC. Startup
(`App.xaml.cs:3126`) seeds dispatch and CoreDiscord before conditional enablement.
These paths and the shared service already exist on this head. Existing callers
cover Flash, Bubbles, Mind Wipe (including session mode), Video and Sessions;
level updates, offline handling, sandbox suppression and exit disposal are already
recorded in the row. The remaining live caller gaps are:

1. **Brain Drain**: `ConditioningControlPanel/Services/LockCard/BrainDrainService.cs`
   Start checks enabled/running, reloads clips, updates settings and interval,
   starts its timer, then calls `SetBrainDrainActivity` (:240). Stop always tears
   down audio, returns without changing activity if already stopped, and otherwise
   cancels/stops the timer and calls `SetIdleActivity` (:270). The shared activity
   setter exists, but there is no CoreBrainDrain or Avalonia BrainDrainPlayer on
   this base, and the feature control's enable/settings handlers are explicitly
   inert. A settings-only presence caller would claim activity without a running
   feature. The missing host includes audio discovery/playback, engine/session
   lifecycle, panic stop and feature-control wiring.
2. **Goon Game**: `ConditioningControlPanel/Services/GoonGame/GoonHostService.cs`
   accepts only lobby/live/recap/off in `OnRichPresenceState` (:1641-1659), drops
   frames unless GoonRichPresence is enabled and dispatches to the UI thread.
   Turning the preference off retracts immediately (:1305), since later off frames
   are dropped. DisposeAll retracts unconditionally (:2138), even after abrupt
   window closure. Shared `SetGoonActivity` already implements fixed strings/art,
   connection ownership, no level suffix and restoration of the prior activity.
   This head has game infrastructure and Goon rules, but no GoonHostService or
   GameWindow.Goon and no Goon entry in GameWindow.Games. The missing user path
   needs the real host, init/proxy/bridge routing, settings, window lifecycle and
   panic teardown; a disconnected rp-state handler would not meet the requirement.

Reproduction: `rg -n 'SetBrainDrainActivity|SetGoonActivity'
ConditioningControlPanel CCP.Core CCP.Avalonia` identifies both live WPF callers
and the shared setters, but no matching Avalonia host callers. These are live
WPF behaviors, so P122's dead-caller exception does not apply.

## Why this unit is dependency-only

The WPF Brain Drain service is 496 lines and the Goon host is 2,167 lines.
The adapted trunk host is smaller, but even just
`CCP.Core/Services/GoonGame/GoonHostService.cs` (694 lines) plus
`CCP.Avalonia/Views/Games/GameWindow.Goon.cs` (480 lines) totals **1,174 lines**,
before the additional GoonShell/GoonMedia/GoonTransfer files, absent dependencies,
startup/launch routing or focused tests. Trunk BrainDrainPlayer is another 132
lines plus CoreBrainDrain and its engine/session/UI integrations. Reusing the
existing head files still requires merging these behaviors rather than replacing
those files. A complete tested port of both hosts cannot fit this unit's 1,000
added-line cap without dropping WPF behavior. Per the explicit pure-dependency
rule, no partial code port or ledger promotion is made.

Follow-up: port the Brain Drain audio host and the Goon Game host in bounded
host units, then wire/test their existing shared presence methods from real user
paths. Include normal stop, repeated stop, preference-off and abrupt/panic close.

## Verification limits

Builds used .NET SDK 10.0.402, Release configuration. Commands/results:

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `dotnet build <project> -c Release` for every Linux test project named in
  `.github/workflows/build.yml`: all exit 0, 0 errors. Projects: CCP.Core.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests,
  CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests,
  CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests,
  CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests,
  CCP.Avalonia.Snapshot.Tests (each under Tests with its matching csproj).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release
  --filter FullyQualifiedName~DiscordRichPresenceTests`: deliberately not run,
  per the supplied environment limit. This base's test setup uses UseSkia and
  UseHeadless without the cloud font setup; the assembly compiled successfully.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release
  --no-restore`: exit 1; 2,817 passed, 1 failed, 5 skipped (2,823 total).
  Failure: `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch`,
  line 80, Assert.NotNull returned null. Production/test sources are identical to
  the pinned base. The five Vosk tests were skipped by their existing model checks;
  no tests were edited, disabled or removed in this unit.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release
  --no-build --no-restore --filter FullyQualifiedName~DiscordActivityCallersTests`:
  exit 0; 6 passed, 0 failed, 0 skipped.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0,
  512 warnings, 0 errors; compiled only, not run on Linux.
- Isolated rerun of the failing Core clock test with `--no-build --no-restore
  --filter FullyQualifiedName~SyncedEventAndScheduledFireBothHopThroughCoreDispatch`:
  exit 0, 1 passed. The full-suite failure did not reproduce in isolation.
- Full Core rerun, `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests
  -c Release`: exit 0; 2,818 passed, 0 failed, 5 Vosk skips, 2,823 total,
  duration 47 seconds. Initial suite failure is retained above as a verification limit.
- `git diff --check`: exit 0.

 No Windows runtime,
real Discord client, live game server, audio device or desktop was exercised.
No new surface was introduced, so no panic registration or tester step was added.
The base has no `.github/scripts/test-plan-coverage.sh` (attempt exits 127).
Core guards pass (exit 0); their first sandboxed attempt could not create temporary
files, so the successful run used writable execution permissions.

## Assumptions

The explicit dependency-only instruction takes precedence over doing a partial
host port: both named Missing callers depend on unported hosts, and the Goon
host alone exceeds the unit cap in the adapted reference. Existing shared
presence methods do not make absent callers wired.

NOOP: Brain Drain audio host and Goon Game host are unported on the required base.
