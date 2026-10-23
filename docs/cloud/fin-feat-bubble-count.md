# fin-feat-bubble-count

- Base verified at `97ee118a6f5fce03b4c82005a2ffc09e20c94db8`. The initial snapshot was at a different commit and its fetch was read-only; used the authorized fallback clone and created only `avalonia-port/cloud-fin-feat-bubble-count` from the required base.
- Added the WPF Pause other effects editor, including settings replacement/property-change rebinding. The scheduler latches the choice per round, stops flashes, holds bouncing text through lead-in/retry/Mercy, and releases on every exit. Normal completion resumes flashes only if previously running, the engine is running and flashes remain enabled; force cleanup never restarts them.
- Added WPF empty-library guidance: localized warning, installed-but-inactive pack advice, once per launch for scheduled rounds and every Test Now. Uses the existing in-app notification service; no desktop notification or new window type.
- Connected the existing For You game host to WPF's bounded defer: one replay, 500 ms polls, a 90 s deadline retained across re-deferral, no Freeze/game while waiting, fresh eligibility checks on replay, cancellation on stop/panic.
- Removed duplicated/stale evidence from the single ledger row. Kept `stub` because cross-feature dependencies below remain. Existing pack support is already reachable and tested; it was not reimported.
- Added focused Core regression coverage and headless user-path tests. No new surface: the existing `engine` panic registration reaches `CoreEngine.Stop` -> `BubbleCountScheduler.ForceCleanup`; the existing `games` registration closes For You. Tests use the established isolated profiles.
- Source credit: WPF effects-hold behavior introduced by `c52e7f6d8eac017c5dc35b343992b7fa7a66343c`; no-video behavior by `2c519a67a98f694140709700c1a0689f709d29e5`. Read the frozen WPF files on the pinned base and fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Adapted behavior into existing files; no whole-file replacement or additional trunk imports.

## Gap analysis

WPF reference: `Features/BubbleCountFeatureControl.xaml(.cs)` and `Services/BubbleCountService.cs`, at the base above. Service rules are part of the real Test Now/scheduled path behind the referenced editor.

| Gap found before this change | Result |
| --- | --- |
| Pack videos claimed missing by the ledger | Already wired by `BubbleCountHost.PackVideos`, `PackMediaPool` and content-pack startup/reload. Existing `PackMediaPoolTests.VideoAndBubbleSchedulers_PlayPackClips_WhenTheLibraryIsEmpty`/`ContentPacksWireTests` cover the paths. |
| For You on-screen guard, coalescing, bounded replay and eligibility recheck | Implemented against the real visible feed, including the 90 s chain deadline. |
| Pause other effects setting/editor and per-round flash/text hold | Implemented through existing CoreFlash and mandatory-video bouncing-text behavior, including default-off, next-round setting changes and cleanup. |
| Full overlay parking below a held round | Still missing the shared overlay reconciler. Existing topmost round windows alone do not guarantee ordering of every overlay, especially newly mapped ones. Dependency: `shell-clickthrough-overlays`, WPF `OverlayService.ReassertZOrder` / `EffectsHoldWindowHandles`. |
| Empty video library explanation (#1449) | Implemented with WPF copy, warning severity and scheduled/test frequency. |
| Reciprocal interaction exclusion | Still missing shared `InteractionQueueService`: `MandatoryVideoScheduler.Trigger` has no bubble-game guard; `LockCardWindow.ShowOnAllMonitors` only defers for PopQuiz; `PopQuizHost.IsInteractionBusy` is false. Their feature paths must participate in the same claim/release mechanism. The existing bubble-side 1 s wait is preserved. |
| Typed `ExtendTimeout(videoDuration + 120, BubbleCount)` and stuck recovery | Depends on that unported shared queue; adding a standalone timeout would not extend its claim or recover other queued interactions. |
| Feed ghost exception | `GameWindow.Fyp` explicitly reports ghost mode unsupported. All currently visible feeds own the screen. Future ghost support (`feat-fyp-host`) must exclude ghosted feeds from `IsForYouOnScreen`. |
| Browser-versus-native selection, native-poison skip and player safety | Existing dependencies of `shell-video-playback` / `win-bubble-count`, explicitly documented in `BubbleCountWindow`. The existing missing-LibVLC skip remains intact. |

No P122 dead-caller exception applies: WPF's scheduled and Test Now callers are live. This is a partial finish with explicit dependencies, not a dependency-only NOOP.

## Verification limits

- .NET SDK 10.0.402 installed without root access. The installer's first feed returned 403; its supported builds feed succeeded.
- Regression proof before implementation: `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~BubbleCountSchedulerTests` exited 1: 11 failed, 8 passed. After implementation: 23 passed, 0 failed, 0 skipped (exit 0), including pack advice and re-deferral deadline cases.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`, with `DiffEngine_Disabled=true`: exit 0; 3,560 passed, 0 failed, 5 existing Vosk-dependent cases skipped. No tests weakened or newly skipped. Model download is blocked in this runtime.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors. `dotnet build CCP.Avalonia -c Release`: exit 0, 0 errors (existing warnings).
- All 12 Linux CI test projects built with `dotnet build <project> -c Release`: exit 0, 0 errors each. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests and CCP.Avalonia.Snapshot.Tests (each under Tests).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors. WPF and Windows code compiled but not run.
- Build recovery: duplicate generated resources exhausted the worker disk (initial builds exited -6/134; one stale MSBuild worker exited 1). Removed only generated binaries between builds. The catalogue retry with `--disable-build-servers -m:1` exited 0; every required build ultimately passed.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. The first sandboxed attempt could not create temporary files; reran with workspace write access.
- Avalonia headless tests are compiled but not run here, per the task's measured font-creation limitation; the cloud-test-fonts lane is absent. Supervisor command: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~BubbleCountQueueTests|FullyQualifiedName~BubbleCountGameTests|FullyQualifiedName~ContentPacksWireTests'` with `DiffEngine_Disabled=true`.
- Windows-specific paths are compiled where supported but cannot run on this Linux worker. No real desktop, browser navigation, notifications or real user profile was used by the new tests.
- No render-all execution here because it requires the same unavailable font setup; no new visual-verification claim.

## Assumptions

- Reuse the head's existing mandatory-video text hold, including Show Over Videos, rather than inventing another overlay mechanism. Full WPF overlay ordering remains an explicit dependency.
- Cancel a pending feed replay on stop/force cleanup, including Test Now, so stale work cannot reopen after a stop. Ordinary Test Now still bypasses the engine/enable checks, as WPF does.
- Keep the existing bubble-side 1 s interaction poll; a shared queue port must replace both directions together. Do not mark the row wired while those user-visible behaviors remain missing.
