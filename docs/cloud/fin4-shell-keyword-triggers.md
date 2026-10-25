# fin4 shell keyword triggers

- Base verified at `5c4535ecbee055572f96f59523858eea00e85358`; local snapshot branch was `work`.
- Port the WPF eight-app, newest-first, case-insensitive foreground ring into Core. Sample at most once per two seconds in unscoped Everywhere mode; scoped input still resolves each press. Never remember own or unknown processes; keep the ring in memory only.
- Populate the existing Awareness app chips, omit already-listed processes with WPF optional-.exe matching, and save additions through the existing app-list path.
- Connect master status changes to the existing shared 30-fps BeatLoop glow (no new animation loop or surface).
- Reconcile the keyword runtime during entitlement refresh: stop input/audio after lapse and re-arm a saved-on master when access resolves.
- Add focused Core/history and headless chip, access-loss/playback, access-resolution, and real master-switch glow regressions. No new surface: existing `keyword-triggers` and bubble/overlay panic registrations remain.
- Fetch-only reference: `parity/trunk` resolved to `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported in this unit; existing head files win.

## WPF gap inventory and remaining work

The ledger remains **stub**, never wired/verified. Before this unit the remaining gaps were:
1. Recent-app ring, chips, exclusion of own/unknown/listed apps, and one-click additions (now ported).
2. Awareness status glow connected to live master updates (now ported through the existing shell host).
3. Entitlement loss stopping the armed service and clips, plus delayed access resolution restarting a saved-on master (now ported).
4. Live screen OCR capture/recognition, interval updates, own-UI/focus/app-scope exclusion, consecutive-scan confirmation, stable positional de-duplication and highlight-all vs matched-only behavior. Still missing.
5. Highlight window creation, matched word bounds, duration/color, removal when a word disappears, refresh of display capture visibility, and panic/restart lifecycle. Still missing.
6. OverlayPulse (double intensity then restore), ten-second BrainDrain melt (respect existing drain and zero blur), and Bubbles SpawnOnce when the ambient engine is off. Still missing their head APIs.
7. Avatar comment postponement while voice audio remains busy, and post-delivery Awareness v2 arbiter reporting. Still missing the audio-busy/v2 host contracts.
8. WPF batch-wide duck-once timing through the longest action audio plus 500 ms. Still missing; the existing head retains stoppable per-clip ducking.
9. Linux global keystroke source remains unavailable (a cross-platform product gap; WPF itself uses Windows hooks).

OCR/highlight work is not just a missing caller: the WPF capture and highlight services alone total 844 lines, before the roughly 240-line confirmation/matching path, platform adaptation, tests and panic lifecycle. They cannot be carried intact inside this unit's 1000-added-line budget. The visible app-list and existing-host lifecycle gaps were prioritized; the other listed behavior is explicitly deferred rather than represented by inert APIs.

Clipboard clarification: at pinned base `5c4535ecbee055572f96f59523858eea00e85358`, `git grep -n 'CheckTextForMatches(' 5c4535ecbee055572f96f59523858eea00e85358 -- ConditioningControlPanel` finds only the declaration in `Services/KeywordTriggerService.cs:977`. There is no live WPF clipboard scanner caller to reproduce. This does not justify P122 promotion: OCR and the other live gaps above remain.

## Verification limits

Validated with .NET SDK 10.0.402 and `DiffEngine_Disabled=true`. Windows hook/input sampling code compiles on Linux but cannot be exercised here. No real desktop or profile is used by tests. Full Windows/WPF runtime, real OCR/capture/highlight behavior, hardware haptics and live remote services were not exercised.

## Assumptions

- Keep WPF's memory-only app history across service stop/start, preserve the original spelling on case-insensitive reuse, and refresh chips when the tab/app-list UI refreshes.
- Reuse the existing shell pulse including its documented platform/performance gating limitations.
- Do not invent a clipboard scanner for a WPF method with no caller.


### Commands and results

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build <project> -c Release` for Core, Avalonia and all 12 Linux test csproj files named in build.yml | 0 each | Build succeeded; 0 errors |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | 552 warnings; 0 errors; Windows runtime not run |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build` | 0 | 3566 passed; 0 failed; 5 skipped (existing speech tests, Vosk model unavailable) |
| `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release` after final fixture isolation | 0 | 55 warnings; 0 errors |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter <focused classes below>` | 0 | 45 passed; 0 failed; 0 skipped |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed |
| `git diff --check` and `git diff --cached --check` | 0 | No whitespace errors |

Focused classes: KeywordAudioRuntimeTests, KeywordResponseTests, KeywordChasterTests, KeywordTriggersPanelTests, PanicSurfacesTests, AmbientBeatLoopsTests. Core's full run includes KeywordTextInputTests and the new eight-app ring test. The separate Linux test projects were compiled, not executed. Full Avalonia suite, render-all and desktop smoke were not run. This base has no test-plan-coverage.sh; numbered tester steps were added anyway.

Pre-fix evidence: the new chip and entitlement tests failed (empty chip collection; runtime still active after lapse). Removing only the glow caller made its new master-switch test fail (null effect). An initial overlapping build hit a PDB lock; all final builds ran sequentially. Adding the entitlement fixture exposed a pre-existing fixed-clock assumption in the panic test: the singleton intentionally retains its global cooldown after Stop. Fixtures now advance one shared logical clock; production cooldowns and all assertions remain unchanged. The final focused run passes.
