# fin5-shell-tab-fx-presets-quests-achievements

- Add the live Core receipt drawer and tab-owned Takeaway shelf: three pinned receipts, all receipts in the overflow tray, date/minute/age metadata, copy-link feedback, gated shop door and replay.
- Refresh on Presets arrival before the entrance calls, availability changes and shop/replay close. Preserve WPF's single in-flight read, 90-second cache, invalidation before refresh, empty-on-error and no local order persistence.
- Reuse existing chip hover and independent entrance zones, existing auth handoff, existing game-window singleton and panic registration. Add replay fullscreen controls and panel duck/restore; do not replace existing base implementations.
- Use Fluent glyphs for receipt, tray and copy icons. Add Core cache/failure/parser tests and headless shelf/action/replay tests. Promote only the target ledger row to wired, not verified; add a numbered tester step.
- Adapted import: trunk commit `16aa514192d1d0cfb31b1c5719494bf111838764`, read at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`: `PresetsTabView.Takeaway.cs`, `JustDropOrdersService.cs`, `TakeawayShelfTests.cs`. The base's `JustDropHandoff`, `GameWindow` and focal FX win; no trunk host-service abstraction was imported.

## Pinned WPF audit

Reference: base `f67d473d3c6f59b02a6628f3a64e9fd1c1705801`, `ConditioningControlPanel/MainWindow/MainWindow.TabFxPresetsQuestsAchievements.cs`.

1. Selected preset/session sheen, radii 17/10, selection/rebuild, focus/minimize/motion/performance/mod gates: already implemented in `PresetsTabView.Fx.cs` and the existing sheen adorner.
2. Preset 1.02 hover over 150 ms; session -2 lift, single parked row sweep and detach on rebuild/hide/mod change: already implemented in the base. Primary-button 0.97 press/reset over 80 ms also exists.
3. Data-before-FX arrival: disk sync, Recent re-sort and shelf refresh, then independently stagger visible border children in the three zones (40 ms slots capped at six; 220 ms fade, 260 ms rise). Disk/rack behavior exists; live shelf refresh was missing. This unit replaces the arrival empty-state seed with the real refresh and supplies receipt/door hover hooks.
4. Weekly fraction clamp, 0.6-second tween and immediate resize seating; daily cards owning their own fills: already implemented. No additional quest loop or redundant reroll squish is added.
5. Free achievement grid stagger; unlocked-only 0.8-degree directional badge tilt over 160 ms, 1.02 lift and separate 1.06 art pop: already implemented in `AchievementsTabView.Fx.cs`.
6. Incoming-tab filtering is conditional on the outgoing fade re-show. `rg -n 'FadeOutgoingTab|IsIncomingTab' CCP.Avalonia` finds only explanatory comments. The actual `ShowTab` still snaps visibility, so no outgoing arrival is emitted. Re-evaluate when `shell-chrome-fx` adds that transition; do not add dead filtering now.

The only unconditional missing path on this base was the live receipt shelf and its FX callers. The Core availability/auth/credit ledger and windowed shop host already exist; the old blocker comments claiming those seams are absent were stale. This unit ports the missing order service and replay caller too. WPF receipt behavior is live (`RefreshTakeawayShelf` at reference lines 286 and `PreparePresetCardFx` calls in `MainWindow.Takeaway.cs` at 244/499); P122/dead-code promotion is not used.

## Verification limits

- SDK 10.0.402 installed via the requested installer. Initial channel probing received 403 responses; the SDK payload download succeeded.
- All 14 required projects built in Release with exit 0 / `0 Error(s)`: CCP.Core, CCP.Avalonia, every test csproj named in `.github/workflows/build.yml`, including ConditioningControlPanel.Tests using `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Existing warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.` The first sandboxed attempt could not create temporary files; the normal-permission rerun passed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: final full run exit 0, 2813 passed / 0 failed / 5 optional Vosk skipped (2818 total). Initial run before the added tests saw the previously documented intermittent `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` failure; the final full run passed without changing that test.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~JustDropOrdersTests`: exit 0, 2 passed. Proves cache reuse, invalidation-before-event, failure/signed-out empty results, parsing and size mapping without network.
- With `DiffEngine_Disabled=true`, focused `CCP.Avalonia.Tests` filter `TakeawayShelfTests|PresetsTakeawayEmptyStateTests|PresetsFocalFxTests|JustDropWindowTests`: attempted once, exit 1, 5 passed / 10 failed. Every failure is glyphTypeface creation for missing Segoe UI Black / Inter before window behavior. This is the documented cloud font limitation; window tests require GitHub validation. They were not weakened or skipped. Final source compiles with zero errors.
- Early overlapping builds encountered a PDB lock and intermediate adaptation errors; the subsequent sequential build of every project succeeded.
- `test-plan-coverage.sh` is absent on this pinned base (attempt exits 127). The new numbered tester step supplies future coverage. `git diff --check` passes.
- WPF app and tests compiled, never run on Linux. No real desktop, credential files, live receipt server, native WebKit/WebView2 playback or authenticated account was exercised. No render-all proof is claimed.
- Native browser focus/Esc routing, page fullscreen requests and platform window ownership require desktop testing. The shared WebHost has no fullscreen-element signal; Chromium no-throttle flags have no WebKitGTK equivalent. The separate native-web row keeps the host's fullscreen button outside browser content. These native-host limits are not evidence of verified parity.

## Assumptions

- Keep the smallest adaptation: reuse GameWindow's existing singleton/panic path and Core handoff instead of importing trunk's additional host-service delegate layer.
- WPF's arrival refresh starts an async load rather than waiting for network before staggering; preserve that ordering. Cached receipts can complete synchronously; slow network receipts arrive when fetched.
- The incoming guard becomes necessary only when the shell actually re-shows outgoing tabs. No shell-chrome transition is introduced by this unit.
