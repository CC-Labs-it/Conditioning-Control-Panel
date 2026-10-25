# fin4-shell-ui-updates

Base: `5cdc31ee4412d4ff8a47de9dbfc42cbc39440107`.

## Reference audit and scope

The last `shell-ui-updates` row was `stub`. Audited the cited WPF
`MainWindow/MainWindow.UiUpdates.cs` at the pinned base, plus its startup caller
in `MainWindow.xaml.cs`, account choke points, ProfileSyncService and
SeasonRecapService. The initial missing live behavior was:

- Conditioning profile push every 15 minutes and after the final stop write;
  take-higher conditioning profile adoption and the season counter mirror.
- XP login overlay, account name/tier chip, and signed-out chip login action.
- Mod-aware labels and coalesced surface sweep, including deferred Presets refresh.
- Unlockables display refresh (effective Companion AI effects checkbox/panel).
- Intake Pass tile: available-only face, niche art/copy, alternating 8-second
  holds and five half-turns (105/115/130/150/560 ms), reduced-motion handling,
  CTA pulse, help content and popover, cancellation on hide/state change.
- Startup video after the WPF 200 ms delay, configured-file or random fallback,
  strict-mode propagation and silent empty-library handling.

Corrections to stale ledger claims:

- Audio output enumeration/refresh already lives in `AudioSettingsBinder`, used
  by Settings and the dashboard. Adding a second shell writer is unnecessary.
- `BtnPrevImage_Click` and `BtnNextImage_Click` have empty WPF bodies at the base
  (lines 2086–2094); there is no carousel behavior to port.
- `rg 'SetFeatureImageBlur\(' ConditioningControlPanel --glob '*.cs'` finds only
  the declaration at line 952. Its old callers were deleted with ProgressionTab.
  No blur effect is introduced into the Avalonia shell.

The row remains `stub`; the mod sweep, mid-session effective AI permission refresh and
Intake Pass ceremony are still missing. The Intake Pass reference alone spans
roughly 680 lines before adaptation/tests; together with the cross-tab sweep
and tests these exceed the unit's 1000-added-line budget. Existing Intake Pass
service and Play/Graded Intake gates are real; this is not a missing service.

## Changes

- Paint XP login opacity/overlay and the account chip from canonical account
  truth on login, level and entitlement refresh. Signed-out chip opens login;
  signed-in chip opens Settings > Account. Use Fluent badge icons and preserve
  the signed-out mod accent binding.
- Send conditioning minutes with profile sync, adopt only higher cloud minutes,
  push every 15 minutes and after the engine-stop remainder, and mirror local
  accrual into the existing season bucket with one settings save.
- Honor force-video-on-launch after 200 ms, using the configured existing file
  or a random clip; propagate strict mode and suppress empty-library guidance
  without consuming the later user-initiated guidance.
- Add focused regression coverage and numbered manual tester steps. Align the
  tracker shell test with the existing AppFonts/Inter test bootstrap; no
  assertions or tests were weakened, skipped or removed.
- No trunk code imported. Read-only `parity/trunk` fetched at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; adaptations use the pinned WPF base.
- No new window or overlay host: existing shell and video panic registrations
  remain the owners of their surfaces.

## Verification limits

- All 14 Linux build targets (Core, Avalonia and every Linux test project in
  `.github/workflows/build.yml`) built successfully, zero errors. Existing warnings remain.
- `dotnet test Tests/CCP.Core.Tests --no-build`: 4276 passed, 6 existing Vosk
  tests skipped, 0 failed. Focused conditioning/scheduler tests: 37 passed.
- Relevant headless filter (`UiUpdatesTests`, `ConditioningTimeTrackerShellTests`,
  `SyncPushTests`, `IconGuardTests`): 12 passed, 0 failed. The first combined run
  failed on glyph creation because the tracker test initialized Avalonia without
  AppFonts. Using the existing font bootstrap fixed the harness; the retry passed.
- Fail proof before fixes: conditioning payload/adoption 3 failed; season bucket
  initialization 1 failed; account overlay 1 failed against the original compiled
  shell. Initial overlapping builds also produced transient missing-member
  errors; final serial builds have zero errors.
- `bash .github/scripts/core-guards.sh`: passed both API and XAML guards.
  `git diff --check`: passed. `test-plan-coverage.sh` is absent on this base
  (exit 127); numbered manual steps are included anyway.
- Build/test commands used `DiffEngine_Disabled=true`; later builds bounded
  concurrency with `DOTNET_PROCESSOR_COUNT=2` and `-m:1`.
- Windows test project cross-build passed with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`:
  550 warnings, 0 errors. Windows desktop tests were compiled but not run.
- `dotnet run --project CCP.Avalonia --no-build -- --nav-check`: exit 1,
  two failures: Devices remained on Data, then the unknown-section assertion
  also failed. The Settings navigation implementation is unchanged by this
  unit; a base-run comparison was not performed, so these remain an unresolved
  verification limit rather than a claimed baseline pass.
- No live service/network playback or Windows device execution was tested.

## Assumptions

- Reuse canonical `CoreAccount` entitlement truth, including all providers,
  whitelist and grace, instead of checking only Patreon/Discord authentication.
- Retain the existing sync loaded-session, cooldown and offline safety gates.
- Season minute writes initialize an empty bucket from the shared season-key
  rule and never roll an existing bucket merely because the month changed.
