# sync7-after-brain-drain-audio-ports

- Ported the remaining Brain Drain live sample gain behavior from read-only
  `parity/trunk` commit `f601b791781fc5820904bfcb600584fd28c5319a`.
  The running Core player observes master and own volume writes directly, follows
  settings replacement, and removes subscriptions on Stop. Zero master gain mutes
  the current clip without restarting it.
- Reused the base host's linear gain, generation-safe voice publication, LibVLC
  setter bridge (`CCP.Avalonia/App.axaml.cs:702-711`), and registered
  `brain-drain-audio` panic surface (`PanicSurfaces.cs:46`). No new surface.
- Added direct-write/mute and settings-replacement/restart regression tests in
  `BrainDrainAudioTests`. Both failed against the unchanged base player: mute left
  gain at 0.32; replacing settings left gain at 1 instead of 0.2. Both pass with
  the fix. Existing cadence/discovery/teardown tests remain unchanged.
- Expanded the existing numbered Brain Drain tester step; the parity row remains
  `stub`, with visual-host limitations retained.

## Commit disposition

| WPF commit | Status | Evidence |
| --- | --- | --- |
| `f601b791781fc5820904bfcb600584fd28c5319a` | ported (remaining Brain Drain part) | `BrainDrainPlayer.WatchVolumeSettings`, `OnSettingsReplaced`, Start/Stop subscription lifecycle; `SettingsWritesChangeThePlayingClipWithoutASliderHandler` and `ReplacedSettingsRefreshTheClipAndDetachTheOldObject` |

The base already had start-time master × own gain and explicit live refresh
(`BrainDrainPlayer.RefreshVolume`, `Tick`; existing
`PlayingClipIsNotDisplacedAndStopIsUnconditional` test). It lacked observation of
non-slider setting writes. No missing host blocks this remaining part.

## Verification limits

- .NET SDK 10.0.402; Release builds of CCP.Core, CCP.Avalonia and all 12
  Linux-compatible test projects named in `.github/workflows/build.yml`: exit 0,
  zero errors. Projects: Core.Tests, Core.Settings.Tests, Core.Content.Tests,
  Avalonia.Tests, CatalogueStartup, Consent, GeneralSettings, Language, Migration,
  Report, Scheduler and Snapshot test suites.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~BrainDrainAudioTests`:
  exit 0, 9 passed. Each new regression was run before the fix and exited 1 at its
  gain assertion; after the fix both pass.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: final exit 0,
  3,962 passed, 5 skipped, 0 failed. First run exited 1 with an unrelated
  DtrhRemoteMediaTests Loops/Clips assertion; that test passed in isolation and
  the full rerun passed. Vosk integration was not required/downloaded in this
  runtime; the existing five speech tests report skips.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter
  'FullyQualifiedName~BrainDrainAudioPanicTests.RegisteredPanicStopsAudioEvenWithoutShell|FullyQualifiedName~BrainDrainAudioPanicTests.WatchdogFallbackStopsBrainDrain'`:
  exit 0, 2 passed. All tests used `DiffEngine_Disabled=true` and the assemblies'
  existing CCP_USERDATA_DIR sandboxes.
- UI-opening Avalonia tests compiled but were not run because of the stated
  runtime font limitation. Actual LibVLC output and Windows desktop behavior
  were not exercised here.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
  `test-plan-coverage.sh` is absent on this base. `git diff --check`: exit 0.
- Infrastructure retries: source checkout was read-only, so work used the
  authorized fallback clone at the exact requested base. Initial builds exhausted
  disk space copying runtime assets; sequential builds with generated output
  cleanup succeeded. One transient MSBuild child-node failure passed on a
  single-node retry. No repository build settings were changed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings,
  zero errors. WPF compiled only; Windows tests were not run on Linux.

## Assumptions

- The unit is the explicitly scoped remaining Brain Drain part of `f601b7917`.
  Mind Wipe, PlaySound and the AI audio-folder message are outside this branch.
- A player-owned subscription is the smallest adaptation of WPF's master watch
  for this consumer. Existing head wiring already forwards volume to its live
  LibVLC voice, so no additional head bridge is needed.
- Own-volume setting writes use the same observer, preserving the existing WPF
  live-refresh behavior even before the separate own-volume UI row is ported.
