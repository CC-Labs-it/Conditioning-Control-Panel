# Sync 8: settings hand-back (PR-body draft; no PR opened)

- Settle Takeover setting pulses before the session ledger save and snapshot; retire the pink pulse's delayed restore without cancelling unrelated bubbles or stopping overlays.
- On exit, restore a session even when shell close was bypassed, then settle pulses and release the active remote controller's strict lock before the final save. Preserve a strict lock the subject owned. Cancel queued remote work before releasing holdings.
- Reuse the existing pulse restore and remote safety release; keep all panic surfaces registered and unchanged.
- Source credit: WPF bafe52cfc49c458365f57583453c1234fe8ebce6, a307c32687785c64b7ab56de13d7f2c2a902364f, 36370021a, 8fe2a80a012a444a77513e86643b95830c53c357, 9f2065296fa866fa7df2fda045f01e7de9379592, all reachable from the pinned base. Read-only parity/trunk fetched at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038; no files imported from that tip.
- Separate first `compat-scratch` commit repairs missing Core safety/timing helpers and three WPF `App` references from the merge. Core helper names are prefixed to avoid collisions with the frozen WPF copies in its test assembly. Integration may drop that commit in favor of the compatibility lane.

## WPF disposition

- bafe52cfc = ported: `CCP.Core/Services/RemoteControl/RemoteRelay.cs:306`, `CCP.Avalonia/App.axaml.cs:1153`; tests `RemoteRelayTests.Exit_releases_controller_holdings_but_preserves_subject_lock`, `TakeoverTests.ExitWithoutShellCloseRestoresSessionBeforeSaving`. AI opacity already covered by `CCP.Core/Services/Commands/PinkCommand.cs:24` and `SpiralCommand.cs:24`; pulse ownership already covered by `CCP.Core/Services/Autonomy/AutonomyScheduler.cs:61` and `CCP.Avalonia/Views/Overlays/PinkFilterOverlay.cs:174` (releasing PulseHold retains an engine/session-owned tint). Panic preserves switches through `MainShellWindow.StartStop.cs:89`. Remote Easy and Brain Drain holdings have no active driver on this head (`CCP.Core/Services/RemoteControl/RemoteCommands.cs:41`).
- a307c3268 = ported: behavioral regressions in `Tests/CCP.Core.Tests/RemoteRelayTests.cs:200`, `Tests/CCP.Core.Tests/SessionRunnerTests.cs:70`, `Tests/CCP.Avalonia.Tests/TakeoverTests.cs:184` and `:374`; existing `TakeoverTests.ExitDuringPinkPulseSavesTheUsersTint` covers pulse-before-save.
- 36370021a = n/a: the standalone intensity-ramp timer is not ported (`CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:5`); the ramp card stores configuration only. Session ramp pink opacity is transient (`CCP.Core/Services/Session/SessionRunner.cs:52`), not the six saved fields changed by WPF's standalone ramp. No unused ramp journal imported.
- 8fe2a80a0 = ported: `CCP.Core/Services/Session/SessionRunner.cs:113`, `CCP.Avalonia/App.axaml.cs:722`, `CCP.Avalonia/Views/Windows/MainShellWindow.Autonomy.cs:167`; tests `SessionRunnerTests.SettingPulseIsSettledBeforeSessionSnapshot` and `TakeoverTests.SessionSettleRetiresOnlySettingPulseAndItsDelayedRestore`. Spiral pulses have no Avalonia driver.
- 9f2065296 = already-covered: `CCP.Avalonia/Views/Windows/PanicSurfaces.cs:75` invokes the engine stop even when idle; `CCP.Core/CoreEngine.cs:77` always invokes its stop hook; `CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:102` synchronously cancels Takeover pulses. No duplicate cancellation added.

## Verification limits

- SDK: .NET 10.0.100. Channel discovery failed with HTTP 403; pinned SDK installation succeeded.
- Red proof: with only the new API seams and no implementation, the two new Core tests failed (2 failed, 0 passed): opacity remained 45 instead of 10; controller strict lock remained true. After implementation the focused SessionRunner/RemoteRelay/remote-media run passed all 40 tests.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build` with `DiffEngine_Disabled=true`: exit 0, 3955 passed, 5 existing Vosk skips. Initial full run had one unrelated `DtrhRemoteMediaTests` failure; isolated retry and full rerun passed. No tests weakened or disabled.
- `dotnet build -c Release`: Core, Avalonia and all Linux test projects in `.github/workflows/build.yml` succeeded, zero errors: Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.Tests, and Avalonia CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler and Snapshot test projects.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. Initial sandbox-only invocation could not create temporary files; rerun with write permission passed. `git diff --check`: exit 0.
- Relevant headless attempt (`TakeoverTests|PanicSurfacesTests`, `DiffEngine_Disabled=true`): exit 1, 5 passed / 12 failed at shell construction due to the known missing Segoe UI Black glyphTypeface. UI behavior assertions and their runtime red/green proof could not run here; supervisor rcheck is required. The final added shutdown test is compiled; no further font-blocked UI runs attempted.
- Final `dotnet build Tests/CCP.Avalonia.Tests -c Release --no-restore` after scratch renaming: exit 0, 266 existing warnings, 0 errors. Core tests were also rerun afterward: exit 0, 3955 passed / 5 existing skips.
- Windows runtime behavior cannot be run on Linux. `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false --no-restore`: exit 0, 604 warnings, 0 errors. Initial attempt exposed duplicate helper names; fixed only in the scratch commit. WPF tests compiled but were not run.

## Assumptions

- Preserve the existing head's feature scope: no new standalone ramp, Spiral pulse, remote Easy or remote Brain Drain implementation for this unit.
- A nullable per-runner callback is the smallest seam for settling head-owned pulses; startup wires it once. Session settle does not refresh or stop overlays, matching WPF's narrow hand-back.
