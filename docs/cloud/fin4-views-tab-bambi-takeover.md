# fin4 views-tab-bambi-takeover

- Exact base: `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. Original snapshot differed and was read-only; used authorized fallback clone at the exact base.
- Added Core chant state machine, existing LibVLC voice adapter and tab/lifecycle wiring. Added deterministic Core tests, headless controls/panic test and numbered tester steps. Retained one honest stub ledger row.
- Read-only import source fetched: parity/trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported. Behavior adapted from frozen WPF at the base SHA.

## Pinned WPF audit

Read both cited tab files and live handlers in MainWindow.Autonomy.cs; compared settings seeds, mod art, start/stop and consent, all trigger/behavior sliders/toggles, voice hints, model opener, device settings, Test/Test voice, links, wallpaper and chant.

Already wired despite stale ledger text: Test voice -> MainShellWindow.TestSpokenMantra -> real voice funnel; Open models folder -> ExternalOpener; voiced-content hint and feature-art refresh. Removed historical duplicate prose.

Implemented from Services/MantraChantService.cs: explicit/idempotent start, no-content refusal, bounded 12 picks, available prompt/response order, 2.5 s pair beat, live clamped 0–60 s inter-mantra gap, failed-clip wait/retry, 500 ms mute watch dropping partial pairs, fresh pair on unmute, chant × master volume with mute winning, stale-completion invalidation, stop/disarm. App startup clears opt-in; entitlement lapse stops; exit disposes. No new window; audio has a panic registry entry.

Still missing (row stays stub):

- Wallpaper host: Services/Video/WallpaperService.cs is 852 lines before OS adaptation and tests. Local/remote pools, durable materialization, coordinator wiring, async OS writes, pulse/restore ownership cannot fit with chant and tests under the 1000-line cap. Folder remains inert; Keep-off restore and Keep-on empty-library warning (MainWindow.StartStop.WarnIfWallpaperLibraryEmpty) remain absent.
- Wallpaper attribution/link and remote-consent visibility (RefreshWallpaperCredit/ScrolllerLink_Click); requires the missing wallpaper consumer contract.
- Companion permissions link (OpenCompanionPermissions_Click -> ShowTab("permissions")); no Avalonia permissions destination.
- Hidden Force Start (BtnForceStartAutonomy_Click -> Autonomy.ForceStart). WPF has a live XAML handler; no P122 dead-code exemption claimed.
- Inherited shell-autonomy gaps: Web Video action, avatar countdown, event-audio announcements, complete diagnostic Test dialogs. Existing announcer banner is present.

The old shell ledger also named Spiral and Brain Drain pulses as gaps. At the pinned base, `rg -n 'BrainDrainPulse|SpiralPulse' ConditioningControlPanel/Services/AutonomyService.cs` finds only the two PerformAction switch cases (1004, 1048). WPF delegates selection to Core Candidates, which never adds them (AutonomyScheduler.cs:109). They are not reachable through the live random picker and are not counted as missing user behavior here; no row promotion relies on this finding.

## Assumptions

- Prioritize the user-visible chant loop; keep stub for larger wallpaper and shell dependencies.
- Reuse this head's LibVLC volume/lifetime behavior rather than adding NAudio or another transport abstraction. There is no WPF output circuit-breaker API here; failed LibVLC clips follow the retry wait.

## Verification limits

- .NET SDK 10.0.402 installed. Initial checkout verification showed a mismatched HEAD; read-only fetch failed (255), fallback clone and exact-base verification succeeded (0).
- `dotnet build -c Release` passed (0, zero errors) for CCP.Core, CCP.Avalonia and every Linux test project named in build.yml: CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, CCP.Avalonia.Tests, Core.Content, Core.Settings and Core.Tests.
- The first broad build pass exhausted the filesystem (134 on affected builds). Removed generated test outputs and reran sequentially; every Linux build passed. An overlapping early app build hit intermediate DLL contention (1); sequential builds passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, 3966 passed, 5 pre-existing Vosk skips (model unavailable). Focused MantraChantLoopTests: 13 passed.
- Relevant headless classes via `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~TakeoverTabUiTests|FullyQualifiedName~PanicSurfacesTests'`: exit 0, 8 passed, zero skips. The initial registry source check failed because the host type was indirect; fixed with explicit StopForPanic entry point and reran green. No font failure in this snapshot.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable (127); script does not exist on this base. Numbered WPF/Rows tester steps added.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 2 warnings, 0 errors. Its first native-asset copy exhausted disk (134); freed validated Avalonia outputs and retried successfully. Windows WPF execution, real desktop/audio output, hardware/mic behavior and render-all were not run. No PR opened.

Push of the authorized unit branch failed (128): HTTPS username/credentials unavailable. No credential workaround attempted. Commit and full format-patch supplied to the requester.
