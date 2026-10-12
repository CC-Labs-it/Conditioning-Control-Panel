# session-core-a: WPF SessionEngine runs the Core session rules

Unit A of the `ConditioningControlPanel/Services/Session` Core move. Base: `avalonia-port/first-show`.

## Inventory

`ConditioningControlPanel/Services/Session/` holds one file, `SessionEngine.cs` (2518 lines). It cannot
move: it is pinned to the WPF head by `MainWindow`, `DispatcherTimer`, WPF `Window`s, Win32
P/Invoke and about 40 `App.*` services. Every other Session file was already moved or ported in
`CCP.Core/Services/Session/` by earlier units (`SessionFileService`, `SessionLogService`,
`SessionManager`, `SessionRackQuery`, `SessionClockLabel`, `SessionSummaryPresentation`,
`SessionSettingsSnapshot`, `PhrasePoolCustody`, `SessionRules`, `SessionRunner`).

Core already had ported copies of the engine's pure rules ("duplication deliberate", decisions log
2026-09-28). The brief forbids two copies of one rule, so this unit routes WPF to the Core copies
instead of moving any file. No `git mv` was needed, and no embedded resource or core-guards
baseline entry.

## What changed

- `SessionEngine.ElapsedTime` calls `SessionClock.Elapsed` (the #369 clock-jump guard).
- The completed-session XP calls `SessionXp.Compute`, and `XPPenalty` uses `SessionXp.PausePenalty`.
- `CheckPhaseTransition` calls `SessionTimeline.PhaseIndexAt`.
- The #483 deferred feature starts use Core `DeferredStartQueue`. `FireDue` gains an optional
  `started(name, minute)` callback, which runs only after a successful start. WPF uses it for EMI
  `sessionFeatureArrived`, as before.
- The mid-session phrase edit fold calls `PhrasePoolCustody.FoldUserPoolEdit`; the WPF copy is deleted.
- `RandomizeStartTimes` calls `SessionRunner.RandomizedStart` (±3 min on a delayed start).
- `ProgressionService.GetSessionXPMultiplier` returns `SessionXp.Multiplier`. This also covers its other
  callers: JustDrop, MainWindow.StartStop/Presets/SessionIO.
- `ponytail:` shortcuts:
  - The engine no longer falls back to a 1.0 multiplier when `App.Progression` is null. It is set at
    startup, before any session can run.
  - The "Session completed" Information line drops its `base` field. Bonus and penalty are still
    logged at Debug.
- New tests in `Tests/CCP.Core.Tests/SessionRulesTests.cs`:
  - `DeferredQueue_StartedCallback_RunsOnlyAfterASuccessfulStart_WithTheTargetMinute`
  - `WpfSessionEngine_RoutesToTheCoreRules_InsteadOfCopyingThem`: a source scan; it fails on the base
    engine with `Not found: "SessionClock.Elapsed("`.
- `docs/avalonia-decisions.md` gets a new row that supersedes the 2026-09-28 row for these rules.
- Trunk: `parity/trunk` has no equivalent routing (its `SessionEngine` uses none of the Core rules),
  so nothing was imported and no trunk commits are credited.

## Next units

- **session-core-b:** settings save/restore. Route `SaveCurrentSettings`/`RestoreSettings` to
  `SessionSettingsSnapshot`. This needs an owner decision: Core restores `SubliminalDuration`, while
  WPF leaks it (decisions log 2026-09-29). Routing would change WPF behaviour.
- **session-core-c:** ramp math. Move the `UpdateRampingValues` interpolation and `Lerp` into Core
  `SessionRules`, then route both heads to it. The Avalonia runner does not ramp yet.
- **session-core-d:** bubble bursts. Move `ScheduleBubbleBursts` and `HandleIntermittentBubbles`
  scheduling into Core.
- **session-core-e:** corner GIF admission. Move the `CanRaiseCornerGif` policy to Core. Import
  trunk `SessionRunner.CornerGif.cs` (7bb4351d8, 7fdf53167) adapted to our tree.
- **session-core-f:** phase/feature EMI moments on the Avalonia runner (trunk 218c98423, 49df966c6).
- Pool reapply/restore stays split on purpose (D1 differences, decisions log).

## Verification limits

- `bash .github/scripts/core-guards.sh`: "All Core guards passed."
- `dotnet build` succeeded for:
  - CCP.Core and CCP.Avalonia
  - Tests: CCP.Core, CCP.Core.Settings, CCP.Core.Content, CCP.Avalonia, CCP.Avalonia.Consent,
    CCP.Avalonia.Language, CCP.Avalonia.CatalogueStartup, CCP.Avalonia.Scheduler,
    CCP.Avalonia.Report, CCP.Avalonia.GeneralSettings, CCP.Avalonia.Snapshot
- `ConditioningControlPanel.csproj` and `Tests/ConditioningControlPanel.Tests` were built with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Both
  compiled with no new warnings.
- `dotnet test Tests/CCP.Core.Tests`: 1160 passed, 5 skipped, 2 failed. Both failures also fail on the
  unchanged base in this container:
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_*`: runs as root, so the folder is
    deletable.
  - `LoopbackOAuthTests.GoodState_OnEitherLoopback_*([::1])`: no IPv6 loopback.
- `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~Session`: 52/52 passed.
- Not run: the WPF head and its tests. They are Windows only; they were compiled here, never executed.
  The rerouted engine paths (clock, XP award, phase change, deferred starts, phrase fold, randomized
  start) are covered only through the Core rule tests and the source scan.
