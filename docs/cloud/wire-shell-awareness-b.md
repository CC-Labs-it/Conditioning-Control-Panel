# wire-shell-awareness-b

- Restore WPF's Awareness status pulse (`MainWindow.Awareness.cs:375` -> `MainWindow.TabFxTakeoverLabStatus.cs:329`).
  `AwarenessTabView.UpdateStatusIndicator` calls the shell's one pulse registry, `SetAwarenessStatusPulse`. The 8px dot
  stays steady. A sibling Border's BoxShadow breathes from 0.22 to 0.95 over 2.8 s (SineEase InOut, auto-reverse) on the
  window's shared beat. There is no Effect on the dot and no infinite Animation (P82).
- The gate matches WPF's: window focus, not minimised, `AllowAmbientLoops` (motion level + tier), `AllowGlow`, and blur
  capped at `MaxGlowBlurRadius`. It re-arms on `MotionGateChanged` and retints on a mod change. Static subscriptions are
  removed on window close.
- The ledger row stays **stub**. Follow-up **wire-shell-awareness-c** covers the pulse feed/action chips, recently
  focused apps, the OCR/keyboard lifecycle, highlight/capture affinity and the v2 observer/ledger runtime.
- This is an in-tab decoration, not a panic-owned surface. It adds no detector, capture source or topmost window.

## Verification limits

- Fail-proof (remote, P110): with the caller broken (`SetAwarenessStatusPulse(false)` at
  `AwarenessTabView.axaml.cs:178`), `bin/rcheck . "FullyQualifiedName~AwarenessStatusPulseTests"` failed in
  `LiveStatusBreathesBehindTheSteadyDotAndParksWithItsHost` with `Expected: 0.22, Actual: 0`
  (run 39701917417, log `~/ccp-port/evidence/rcheck/wire-shell-awareness-b-024756.log.failed`). The caller was then
  restored and the test left unchanged.
- The full gate is `bin/rgate` (GitHub Actions build.yml). No local build or test was run, because the workspace is
  remote-only.
- `AwarenessAppListEditorTests` and `AwarenessCooldownLadderTests` switched to the shipped `Program.BuildAvaloniaApp`
  font setup. This fixed order-dependent Inter Bold failures; their assertions are unchanged.
- Not exercised: native Wayland/desktop title capture, the Windows runtime, OCR and physical keyboard hooks. A live
  Keincheck look at the breathing dot is queued for the supervisor batch (P92).
- rgate run 39702291623 (snapshot dd757a337): windows, core-guards, every avalonia-windows shard, avalonia-linux 0/1/3/4
  and both coverage jobs are green. Two Linux failures are known flakes from `flakes.md` and unrelated to this diff:
  core-linux `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` (P108) and avalonia-linux (2)
  `RaceTrackPlayerTests.DecodeTurnsAStereoFileIntoMono16k`. Because the Core test step failed, core-linux skipped its
  follow-on steps: LinuxSmoke, `--smoke`, `--render`, the tarball smoke, VR smoke, and the isolated
  Consent/Language/Catalogue/Scheduler/Report/GeneralSettings/Snapshot/settings/content suites. None of those were run
  for this commit.
