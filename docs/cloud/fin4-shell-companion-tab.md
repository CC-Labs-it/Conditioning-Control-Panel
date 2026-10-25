# fin4-shell-companion-tab

- Complete roster JSON personality assignment and installed community prompt actions, reusing the head's room, file picker, account provider and dialogs.
- Adapt WPF CommunityPromptService into Core: cache/discovery, offline/download rules, JSON import/export, activation/removal, acknowledgement refusal and soft validation advisory. Shared installed lookup remains authoritative.
- Apply assignments on companion switch and immediately for the active companion; refresh assigned labels, active name/reset, and settings on tab show. Register community dialogs with panic and suppress late Browse results after panic.
- Fix export reading defaults when CoreSettings has no service provider; the focused export assertion failed before the one-line lookup fix.
- Source: pinned WPF `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. Fetched read-only parity/trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## WPF gap inventory

- Already present on the base: roster switching/avatar refresh; mod support/name/level/ring/tooltips; hero/AI/event/audio paths; drawer persistence and visibility lifecycle. The previous row repeated obsolete absence claims.
- Closed here: assignment picker/folder fallback/cancel, fresh-ID import, assignment persistence and immediate activation; assigned labels/tooltips; installed rows/empty placeholder/activate/remove; explicit acknowledgement and validation advisory; active name/reset; Browse/Import/Export/Refresh and tab-show settings synchronization.
- `GetActivePromptDisplayName` has only its declaration in the pinned WPF tree (`git grep -n GetActivePromptDisplayName 73b1ccf -- ConditioningControlPanel`). Live runtime readout uses installed name with ID fallback; the port follows that caller.
- Compatibility passthroughs and aggregate VM are replaced by existing direct zone ownership, with no null compatibility controls added. Trigger editing/timer behavior belongs to its separate feature row; this row restores the WPF tab-show readback.

## Assumptions

- Preserve WPF's simple first-five/first-install Browse flow and Anonymous export fallback; do not invent a browser window or an author editor.
- Every companion is unlocked from level one, so no new locked-personality path is necessary.
- Preserve WPF removal semantics: clear active ID but retain custom text until Reset. An unassigned companion leaves the current personality active.

## Verification limits

- `dotnet build -c Release`: Core, Avalonia and all 12 Linux test projects referenced by build.yml compile with zero errors. GeneralSettings retried successfully after an MSBuild child-node failure; Avalonia.Tests clean-rebuilt successfully after output contention. Existing warnings remain.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, 3,958 passed / 5 existing Vosk skips. Initial concurrent run had board-frame timing and remote-media cache failures; the complete rerun passed without code/test changes.
- Focused `CommunityPromptsCoreTests`: exit 0, 5 passed. Export test failed before the lookup fix.
- Isolated `CompanionCommunityPromptsTests`: exit 0, 4 passed using the shipped font bootstrap.
- Combined filter (new class, RosterSwitch, TabVisibility, HeroSync, PanicSurfaces): exit 1, 7 passed / 8 font-creation failures (`Segoe UI Black`) with existing classes' process-wide bootstrap. Initial panic ordering failure was fixed in the registry; the ordering check passes. No tests were weakened or skipped.
- Windows-targeted test build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors (554 existing warnings). Initial copy contention cleared on sequential retry. Windows runtime is not exercised here.
- Core guards: exit 0. `test-plan-coverage.sh` is absent on this pinned base (attempt exit 127); numbered tester coverage was added. `git diff --check`: exit 0.
- Native file pickers and production catalogue access require manual validation. HTTP/cache tests use a fake handler; headless tests use the existing isolated CCP_USERDATA_DIR fixture. Vosk model tests retain their pre-existing missing-model skips.
- No PR opened. Only the requested unit branch is pushed.
