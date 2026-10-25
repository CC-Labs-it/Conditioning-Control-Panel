# fin4-win-mod-creator

- Adapt Mantras and UI Art from WPF at `cb33de077c20e32a12fe692810c954b330bc1ba7`, preserving existing head files and reusing Core models, image slots, file pickers and dialogs.
- Reuse CoreAudio/LibVLC stoppable playback for preview toggle/replacement/completion/close/reset; ignore stale callbacks and register preview teardown in PanicSurfaces.
- Rewrite Modding tutorial section callbacks through the existing head tutorial host; activate the first step in WPF order.
- Add focused headless tests and numbered tester steps; retain `stub` status because remaining panels exceed this unit's 1000-added-line budget.
- Read-only import source fetched: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; adaptations credit the pinned WPF base above.

## WPF gap inventory

- UI Art: 34 stable paths, four groups, dimensions, shared-file descriptions, browse/drop/clear/load/export/reset. Implemented; Frame-related instructions omitted until framing exists.
- Art Framing: registry-filtered affordances; selected-image/decode warnings; per-surface previews/captions/scrims; drag, wheel zoom, double-click automatic crop; OK/cancel working copies; sanitize/round/filter nondefault framing; reset on image changes; manifest load/export. Still missing.
- Mantras: ordered phrase/prompt/response/enabled cards; add/remove; containment hint; mp3/wav prompt/response clips; retry/timeout rows; browse/play/clear; slug and collision-free names; missing-clip indicators; required phrase/containment skip warning; unique IDs, prompt fallback; JSON/clip write/load/reset and self-copy guard. Implemented.
- Event Audio: phrase slug/filename rows, browse/play/clear/remove, sorted bulk mp3 folder import, duplicate attachment skipping, non-slug flags, status/errors, first-wins slug export and read-only loaded slug rows. Still missing, including side files on re-export.
- Portraits: manifest import versus folder author mode, validation of skins/emotions/files, skin PNG authoring, idle/default selection, director defaults, import priority, recursive skin copying, load/reset. Still missing, including side files on re-export.
- Animated Emotes: sets 1..7, free-set allocation/duplicate rejection, folder import, JSON/map/clip/talk-pool validation, missing/unused-GIF and 50 MB warnings, valid-only filtered recursive GIF/map/timing export, sorted load/reset. Still missing, including side files on re-export.
- Preview stop/toggle/replacement/natural-end cleanup/reset/close and tutorial section-step rewriting were missing. Implemented using services already present on the base.

## Assumptions

- Prioritize complete Mantras and UI Art slot flows plus existing-service fixes within the hard line cap. Leave framing/Event Audio/Portraits/Emotes explicitly deferred rather than claim wired parity.
- Avalonia native file pickers and async dialogs replace WPF dialogs. Export collects mantra skip warnings using the existing Barks pattern.
- No new window is introduced; preview playback gets a panic registry entry.

## Verification limits

- Core guards: exit 0, `All Core guards passed`.
- Core tests with `DiffEngine_Disabled=true`: exit 0; 2819 passed, 0 failed, 5 existing Vosk tests skipped because the model is unavailable. No tests were disabled or changed to skip.
- Initial relevant Avalonia run: 16 passed, 1 failed. The existing `PanicSurfacesTests.EachPanicRouteCallsStopAllExactlyOnce` cannot create Segoe UI Black in this environment. All four new tests and the existing mod creator tests passed.
- This base lacks `.github/scripts/test-plan-coverage.sh`; numbered steps were added for future coverage checks.
- Native audio hardware, interactive file pickers and visual layout were not exercised. Windows/WPF code cannot run on this Linux host.
- Early overlapping builds hit Avalonia output-file contention; serial builds replaced them. Copying native packages exhausted disk; generated test outputs were reclaimed between projects for the final serial pass.
- `dotnet build -c Release -m:1 -nr:false` passed for CCP.Core, CCP.Avalonia and all 12 test projects named in build.yml (14 builds total), all with 0 errors. Builds used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; this includes the WPF app/test cross-build, compiled but not run.
- Regression mutation: build exit 0; replacing preview with the base play-only behavior and removing tutorial rewriting made both focused tests fail (exit 1): missing OnActivate callback and expected stop count 1 versus actual 0. Implementation restored before final verification.

- Restored final `dotnet build Tests/CCP.Avalonia.Tests -c Release -m:1 -nr:false`: exit 0, 0 errors.
- Restored final `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~ModCreatorFinishTests|FullyQualifiedName~ModCreatorBarksTests|FullyQualifiedName~ModCreatorPanelsTests'` with `DiffEngine_Disabled=true`: exit 0; 12 passed, 0 failed, 0 skipped.
- `git diff --check`: exit 0. WPF, native audio hardware, interactive pickers and visual rendering were not run; the font-limited panic-route test remains for the supervisor's environment.
