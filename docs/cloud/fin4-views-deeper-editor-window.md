# fin4-views-deeper-editor-window

- Adapted keyboard nudge/jump/reset/duplicate, rule Delete/Escape, item context actions, inspector undo grouping and restored-rule pin refresh from the frozen WPF reference at base `3c589d35847872f72711b394c34db583de765fcd`.
- Added the persisted preview/timeline splitter, region-end blur normalization and constrained-band inspector refresh, using existing Core settings and editor infrastructure.
- Added focused headless interaction tests and numbered manual steps. No new window or panic surface; the existing editor/fullscreen/audio registrations remain applicable.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. All adapted code comes from the base WPF files; existing head files were merged, never overwritten.

## Gap inventory and scope

Before this unit the row lacked all of the interactions above plus: tutorial Part 2 dispatch/advance; browser focus shortcut forwarding; native browser profile/settings/document-created injection/zoom equivalence; detached, past-end and non-time rule marker semantics; relink library lookup and Yes/No/Cancel; anchored click-away help; local video codec and max-over-channels waveform parity. The cited WPF ItemsList, LaneChrome, MetadataDrawer, MultiSelect, Ruler, Unified and main editor files were inspected against the head.

The direct editing interactions were prioritized within the 1000-added-line budget. The row remains **stub**. The remaining gaps are the tutorial overlay/new-enhancement continuation, native browser integration and shortcut forwarding, special rule markers, relink lookup/cancel, help popup semantics, and media decoder parity. These include live WPF callers, so P122 does not apply. This is a partial implementation, not a pure dependency NOOP.

## Assumptions

- Keep WPF timing, bounds, menu copy and internal clipboard semantics; retain Avalonia's existing localized resources and theme.
- Native browser key forwarding needs a platform bridge and remains explicitly missing; bubbling shortcuts cover managed editor focus.
- Context menus belong to the existing editor surface and require no additional window panic registration.

## Verification limits

- Required base verified exactly; the read-only snapshot fetch failed, so the authorized fallback clone was used.
- `dotnet build -c Release`: Core, Avalonia and every Linux test project listed in `build.yml` passed with zero errors. Accumulated asset copies initially exhausted disk; generated outputs were removed and the test-project builds were repeated sequentially, successfully.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, 2818 passed, 5 existing Vosk-model tests skipped (2823 total). No tests were weakened or skipped by this unit.
- Focused Avalonia filter: `DeeperEditorInteractionTests`, `DeeperEditorChromeTests`, `DeeperEditorTestFireTests`, `DeeperEditorBridgeTests`, `DeeperEditorPreviewTests`, `DeeperEditorTutorialTests`, `DeeperRowsTests`, `PanicSurfacesTests`: 36 passed, 0 failed. The initial four interaction tests failed against the unchanged editor before implementation; failures included rule Escape, nudge, duplicate and the absent blur handler. New clipboard, metadata, context, splitter, jump and history cases extend that coverage.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `test-plan-coverage.sh` is absent on this base; numbered tester steps were added in the required document.
- `git diff --check`: exit 0. The staged change is below 1000 added lines against the pinned origin base.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings, 0 errors; WPF application and test project cross-compiled, not executed.
- Windows-only native code is not executed on Linux. Native browser/video/device parity, full Avalonia suite and render-all were not run.
