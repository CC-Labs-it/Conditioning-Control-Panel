# tracking-core-a: gaze target scoring moves to CCP.Core

Unit A of the `ConditioningControlPanel/Services/Tracking` Core move.

## Inventory (before this unit)

None of the six files in `Services/Tracking` was mirrored or moved in CCP.Core on
`avalonia-port/feature-day-log-core`. None is pure as a whole either, so no file can be `git mv`ed:

| File | Lines | Why it stays in WPF (for now) |
|------|------:|------|
| ActivityTracker.cs | 93 | Win32 `GetLastInputInfo` + `DispatcherTimer`; the rule part is ~10 lines |
| GazeContentScreenPolicy.cs | 68 | Precedence rule over `System.Windows.Forms.Screen` and `App.*` screen helpers |
| GazeDebugCursorService.cs | 554 | WPF window (UI) |
| GazeDriftCorrectionService.cs | 649 | Rules mixed with the low-level mouse hook, `App.Webcam` wiring and persistence |
| GazeFocusService.cs | 1314 | Dwell engine mixed with FlashWindow/Bubble/IAttentionTarget, cursor lock, refine panel |
| GazeRefineOverlay.cs | 762 | WPF window (UI) |

`parity/trunk` has a Core `GazeFocusEngine` (28e9ffe03, 02207991a), but WPF was never routed to it,
so it is a second copy of the dwell rules; it is not imported here (see unit C).

## What changed

- **New `CCP.Core/Services/Tracking/GazeTargetScoring.cs`**: the predictive target-scoring model
  (Gaussian falloff from the rect edge, bubble/flash sigmas, sticky and flash-type bonuses,
  acquisition threshold) and the two-stage refine **ambiguity rule** (`AmbiguousLeader`) and
  **choice set** (`RefineChoices`) with their thresholds, moved out of WPF `GazeFocusService`. The
  design comments moved with them. The rect-tuple, numbers-in/numbers-out shape of `GaussianScore` /
  `DistanceFromRectEdge` follows trunk 28e9ffe03's `GazeFocusEngine`, so a later import of that engine
  can sit on this file instead of carrying its own copy.
- **WPF `GazeFocusService` routed to Core** (`using static GazeTargetScoring`): its private constants,
  `GaussianScore`, `DistanceFromRectEdge`, the ambiguity loop in `EvaluateAmbiguity` and the
  sort/blob filter in `TryRaiseRefine` are deleted; it keeps the sticky-winner clause, the
  `RefineEnabled` / suppression gates, the `Rect.IsEmpty` skip and the chip cap at
  `MaxRefineCandidates` built chips, so behaviour is unchanged. One copy of each rule remains.
- **Tests**: `Tests/CCP.Core.Tests/Tracking/GazeTargetScoringTests.cs` (5 tests): edge distance,
  base scores and the ≈390-dip bubble reach, every clause of the ambiguity rule including the
  inclusive 0.85 ratio and 280-dip spread boundaries, and the refine choice set's order/filters.
- No embedded resources moved, so no core-guards baseline entry. No Avalonia UI in this unit.

## Next units

- **tracking-core-b**: `GazeDriftCorrectionService` rules into Core: click-residual fold (freshness,
  fixation spread, min/max residual, gain, ±300 clamp, cooldown, persist throttle) and the
  head-reposition fast-mode state machine (pose trigger/settle/arm timeout, stale-pose exit,
  convergence streak, 90 s per 5 min budget, slow baseline re-anchor), clock injected. WPF keeps the
  mouse hook, `App.Webcam` wiring and saving.
- **tracking-core-c**: the dwell engine: import trunk `GazeFocusEngine` (28e9ffe03, 02207991a) adapted
  onto `GazeTargetScoring`, add the refine-session timers (550 ms chip dwell, 6 s timeout, 700 ms
  look-away, 1.2 s face-lost grace, 4 s suppress), and route WPF `GazeFocusService` OnTick / HandleBlink /
  Advance*Dwell through it.
- **tracking-core-d**: `ActivityTracker` idle rule (180 s threshold, 10 s cadence, tick-count
  wrap-around) behind an idle-seconds seam (Win32 stays WPF); `GazeContentScreenPolicy` precedence
  over a generic screen list.
- **tracking-avalonia**: Avalonia head for the debug cursor and refine overlay (UI; GazeDebugCursorService
  and GazeRefineOverlay are WPF windows and stay head-side).

## Verification limits

- Built Release (Linux, .NET SDK 10.0.112): CCP.Core, CCP.Avalonia, CCP.Core.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent/Language/
  CatalogueStartup/Scheduler/Report/GeneralSettings/Snapshot.Tests: all `Build succeeded`.
- `Tests/ConditioningControlPanel.Tests` (which compiles the WPF head, including the edited
  `GazeFocusService.cs`) built with `-p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: `Build succeeded`. **Compiled, not run:**
  the WPF tests and the WPF gaze path need Windows (and a webcam for the real feature).
- `bash .github/scripts/core-guards.sh`: `All Core guards passed.`
- `dotnet test Tests/CCP.Core.Tests -c Release`: 1142 passed, 5 skipped, 2 failed. The 2 failures
  also fail on the base branch with this change stashed and are environmental (container runs as
  root, so a folder cannot be made undeletable; no IPv6 loopback):
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_KeepsThePackInstalledButInactive`,
  `LoopbackOAuthTests.GoodState_OnEitherLoopback_ServesSuccessPage_AndReturnsCode(host: "[::1]")`.
  `GazeTargetScoringTests`: 5/5 passed.
- No Avalonia code changed, so no Avalonia test classes were run.
