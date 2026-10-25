- Added real display-confirmation hooks from FlashOverlay.Paid and SubliminalOverlay.Show; the shell subscribes once and removes the handler on close. Scheduler attempts and companion memory counters remain separate.
- Ported WPF's flash wobble (90/220/340/480ms keys; 2500ms throttle) and subliminal shimmer (.55 opacity at 300ms, back at 600ms; 4000ms throttle). UI dispatch precedes throttle/visual writes. Motion-off gates both. Finite shared frame-clock tracks stop on completion and close.
- Replaced the approximate tier hover easing with WPF BackEaseOut amplitude .45 over the existing 260ms duration.
- Added focused stepped-clock tests for keys, throttle, restart, motion-off and close teardown, plus numeric easing tests. Updated tester steps and replaced the historical ledger accumulation with one honest stub row.
- Source: WPF at base 85d30ccc4b6cb241d136e8299a0d2e844b88585a. Read-only parity/trunk fetched at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038; no new code imported from trunk. Existing tier/BeatLoop adaptations retain their original credits.

## Gap analysis and assumptions

At the pinned base the current follow-up gaps were real flash/subliminal hooks and reactions, exact tier growth easing, and the service-backed spiral summary/menu/door. Older ledger text about missing bust, badge enlargement, burst and browser fallback was superseded by the existing implementation/tests.

The first two gaps are implemented. The row remains stub because the spiral row is still hidden with an empty click handler. WPF MainWindow.ProfileSpiral.cs requires DescentService.Current/BlockChanged and DescentMigrationService.SpiralWithheld; the base has readers and persisted gates, but no live block/migration host. SpiralTabView.Refresh explicitly passes hasBlock:false. WPF's old map window and ceremony are retired: the current second door navigates to the spiral tab; current migration auto-restores the server offer. A fabricated block or bypassed withhold would violate WPF's rollout gate.

Remaining scope is the shared Descent refresh/reset lifecycle (357-line WPF service), live migration offer/auto-restore integration (448-line WPF service), sync/account wiring and their focused tests, then the profile summary/glyph/second door. Porting only the menu would expose a door with no live host. Alongside this unit and its tests, that complete host work exceeds the 1000-added-line budget; it remains the shell-profile-spiral dependency. No pure-dependency NOOP is claimed because this branch implements the other visible gaps.

Assumptions: preserve existing faces, account actions, hover/menu behavior and level/achievement celebrations; use the existing BeatLoop clock for deterministic finite reactions. No new surface was introduced, so existing shell/overlay panic registrations remain sufficient.

## Verification limits

- SDK: 10.0.100. Channel installation hit a 403 at ci.dot.net; the pinned SDK installed successfully.
- All 15 required project builds exited 0 with 0 errors: CCP.Core, CCP.Avalonia, all 12 Linux test projects listed in build.yml, and ConditioningControlPanel.Tests with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false. Release configuration throughout. The initial overlapping Avalonia build hit a PDB file lock; subsequent serial builds and the final test build passed.
- core-guards.sh: exit 0, all guards passed. test-plan-coverage.sh: exit 127, script absent on this base. Numbered WPF/Rows tester steps were added manually.
- DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build: final exit 0, 3743 passed / 5 skipped / 3748 total. An earlier run exited 1 with one intermittent DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls failure (Loops expected, Clips actual); that test passed in the second full run. No Core sources changed.
- ProfileBubbleEasingTests: exit 0, 3 passed. Baseline curve replay: temporarily used the base's stock BackEaseOut in the same assertions, rebuilt successfully, then observed exit 1 (midpoint 1.375 rather than 1.1; 2 passed / 1 failed). Restored final source, rebuilt with 0 errors, and reran: exit 0, 3 passed.
- ProfileBubbleParityTests.DisplayReactionsFollowWpfKeysThrottleAndDetach: flash and subliminal were each run separately; each reported its one timing/throttle/restart/teardown case PASSED, but each process exited 1 because Tests/Shared/AvaloniaTestDispatcher.cs:87 threw VerifyAccess from MainLoop. An unchanged base test (EquippedPresetWinsOverInitialsAndInvalidPresetFallsBack) also passed its assertion then exited 1 with that same fatal dispatcher error. These are NOT clean green UI runs.
- Full --filter FullyQualifiedName~ProfileBubble run hung and was terminated (exit 137). Motion-off cases compiled, but the complete UI suite and reaction fail-before proof remain unverified; the supervisor must run them in a working test host. No tests were skipped or weakened.
- Headless --nav-check in a temporary profile: exit 1, two assertions failed (OpenDeviceSettings reveals Devices; unknown section keeps Devices). These paths were not changed.
- git diff --check: exit 0. Windows runtime popup ordering, WPF visual comparison and WPF tests were not run on Linux. No Vosk model was downloaded. No new surface or Windows-specific code was added.
