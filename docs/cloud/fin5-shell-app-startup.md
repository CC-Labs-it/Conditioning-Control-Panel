- Compared `shell-app-startup` with WPF `App.xaml` and `App.xaml.cs` at base `a3d9e984fb2f2c002f3849e485129134e367a6aa`.
- Seed the countdown's lazy avatar speaker before `Start()`, matching WPF App.xaml.cs:3184-3193. An absent avatar returns false, preserving Core's next-tick retry. Scripted priority speech clears queued presets, requests no sound, and does not extend AI bark suppression. Existing `tube-speech` panic registration owns the bubble.
- Add focused startup regressions and a numbered tester step; replace the row's duplicated historical narrative with current coverage and an honest **stub** gap list.
- Read-only trunk fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported from trunk. WPF at the pinned base is the implementation reference.

## Gap inventory and decisions

The row's previous generic “WPF-only services” description is not a completion claim. Compared startup service construction, seam seeds, deferred startup and shutdown ownership. Already reachable on this base: settings/catalogue fallback, splash, updater, mod activation/migration, brain/prompt hooks, bark/companion hooks, account restoration, quests/programs, content packs, Linux Vosk capture, tray, single-instance and Windows quick recal. Other rows retain their own documented limitations.

Remaining differences in the cited WPF startup surface:

| WPF reference (`App.xaml.cs` at the pinned base) | Remaining head gap |
| --- | --- |
| 1988-2530 and single-instance helpers | WPF diagnostic/hang-dump/watchdog startup, stale-instance takeover, early acknowledgement and first-instance file-open dispatch; existing `shell-single-instance` covers current IPC, not all takeover cases. WPF popup/render-thread workarounds are framework-specific. |
| 2536-2654 | Full recovery/migration and theme/cache integration are not equivalent. Mod host-close hooks for DTRH/Arcademy, resource-path cache, portrait availability and voice-line index remain unseeded where their WPF counterpart is absent. |
| 2634-2653 | Live-event theme repaint is dormant in WPF: the comment explicitly records no `LiveEventService.Apply/Clear` caller. This does not justify promoting the entire row under P122. |
| 2655-2744 | WPF browser-media/compositor and remaining engine host integrations are not all present; the head's schedulers/overlays are the current paths. No blanket service parity claim. |
| 2757-2899 | Achievement/gamification/skill-tree and roadmap integrations retain their feature-row limits; EMI has summon/dismiss and a chord, but lacks WPF's full moment/nudge/knock bus and app event subscriptions. |
| 2900-3114 | Companion asks still lack WPF's complete busy/session source matrix; memory signal and awareness sources retain head-specific gaps. |
| 3169-3202 | Countdown speech fixed here. Full server profile level/XP adoption, Descent migration/show-director startup remain incomplete. |
| 3204-3215 | Funscript start/stop exists in Core's video scheduler, but the head has no primary playback-time subscription; audio-sync coupling also needs its capture/host path. EMI haptic moment sink requires the full EMI bus. |
| 3216-3262 | OCR/highlight, full keyword action set and preset repair, remote-control/lobby service startup are not WPF-equivalent. The existing keyboard/audio keyword runtime is not the whole WPF service. |
| 3287-3311 | Dual-monitor/screen-mirror services, Windows speech capture and sherpa wake engine are absent. Linux Vosk exists; desktop deferred voice-model warmup/reconciliation is still missing. |
| 3358-3430 | Webcam/focus/blink UI exists; remaining gaze/attention-check host startup and Deeper video-enhancement/browser-discovery coupling retain their feature-row gaps. |
| 3445-3570 | Remaining possession/lockdown-dose, mantra-chant, wallpaper and gamification startup side effects retain their feature-row limits. |
| MainWindow global input initialization; 3967-4001 | Global chat/camera shortcuts and deferred voice arming remain; quick recal already exists on Windows. Arcademy's full host/bridge and mod-switch teardown are absent. |
| `App.xaml` | Avalonia's existing dark palette/styles are adapted resources; WPF-only implicit controls, DWM caption and font-repair machinery are not claimed as equivalent. |

The pinned WPF Arcademy host is 6,140 lines and EMI Desk service is 2,128 lines before their companion surfaces and tests. These hosts are substantial independent features: for example the WPF Arcademy host includes browser ownership, message dispatch, economy/sync and shutdown flush, while EMI requires its moment bus and event sources. Porting that set correctly with tests exceeds this unit's 1,000-added-line budget. Keep the row `stub`; do not substitute empty callbacks or fake services. This is a partial implementation, not a dependency-only NOOP.

## Verification limits

Commands used .NET 10 SDKs (10.0.100 installed initially; the channel installer subsequently installed 10.0.402, the final default), Release, `DiffEngine_Disabled=true` for tests, `-m:1 -p:UseSharedCompilation=false` for builds. Test-project builds used `-p:BuildProjectReferences=false` after building the heads separately.

| Command / check | Exit and result |
| --- | --- |
| Required checkout verification | 0; clean required base commit; local snapshot branch was `work`. |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 warnings, 0 errors. |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 198 existing warnings, 0 errors. |
| `dotnet build` of all 12 Linux test projects in `.github/workflows/build.yml` | All 0; all 0 errors (catalogue startup, consent, general settings, language, migration, report, scheduler, snapshots, main Avalonia, Core content, Core settings, Core). |
| `bash .github/scripts/core-guards.sh` | 0; all guards passed (429 XAML references, 1,569 head types, 2,236 Core types). Initial read-only invocation could not create temporary files; successful run had temporary-file write access. |
| `bash .github/scripts/test-plan-coverage.sh` | 127; script is absent on this base. Numbered startup tester step added; exactly one ledger row remains. |
| Pre-fix `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~StartupDescentSpeechTests` | 1; both tests failed at `Assert.NotNull(fuse.Speaker)` with the original unseeded countdown constructor factored into the same factory. |
| Post-fix `dotnet test Tests/CCP.Avalonia.Tests --filter 'FullyQualifiedName~StartupDescentSpeechTests\|FullyQualifiedName~TubeSpeechQueueTests\|FullyQualifiedName~DescentFuseShellTests\|FullyQualifiedName~StartModsTests\|FullyQualifiedName~SplashStartupTests'` | 1; 8 passed, 9 failed. Six failures are missing Inter/Segoe glyph typefaces, including the new visual regression. Three existing splash tests throw NullReferenceException at their control lookups. No test weakened or skipped to hide these failures. |
| Post-fix `dotnet test Tests/CCP.Avalonia.Tests --no-build --filter FullyQualifiedName~StartupKeepsThePhaseAvailableBeforeTheAvatarExists` | 0; 1 passed, 0 failed, 0 skipped. |
| `dotnet test Tests/CCP.Core.Tests -c Release` | 1; 4,474 passed, 1 failed, 5 skipped. The failure is the unchanged `BoardTilesTests.A_frame_fits_the_budget` (136.96 ms vs 12 ms budget under concurrent build load). Existing Vosk tests skip without the unavailable model. |
| `git diff --check` | 0. |

`dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` also passed (exit 0; 557 existing warnings, 0 errors), including its WPF app reference. Compiled only: Windows code was not executed on Linux.

The isolated `BoardTilesTests.A_frame_fits_the_budget` rerun after all builds passed (exit 0; 1 passed, 0 failed, 0 skipped). The full unloaded Core rerun (`dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore`) then passed: exit 0; 4,475 passed, 0 failed, 5 existing Vosk skips; 4,480 total.

 No real desktop, microphone, notification service, user profile or physical haptic device is used by the focused tests. Windows code can only be compiled here, not executed. No render-all or manual WPF comparison has been performed; no `verified` claim.

## Assumptions

- Resolve the open avatar lazily through the existing `AvatarTubeWindow.Live` lookup, the Avalonia equivalent of WPF's `App.AvatarWindow`. A closed/absent tube must not burn the phase announcement.
- Preserve Core's existing once-per-phase ordering, petname substitution and terminal silence; do not duplicate these rules in the head.
- Prioritize the reachable countdown speech gap while documenting the wider startup dependencies honestly.
