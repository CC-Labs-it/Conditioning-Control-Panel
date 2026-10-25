# fin4 Workshop accordion

- Restore trigger mode/interval from Core settings on construction, reveal and settings replacement, suppressing writes during restore. User mode changes save; interval changes truncate to integer seconds and update settings without saving on each tick, matching WPF.
- Connect Edit phrases through the existing TextEditorDialog. Remove blank/case-duplicate input, never repopulate an empty pool, save only enabled phrases, and retain WPF user-added/default phrase provenance bookkeeping. Cancel leaves settings alone.
- Register the editor with panic; discard pending edits without another modal. Add focused headless regressions and numbered tester steps.
- Keep `views-companion-workshop-accordion` at `stub` with one current, explicit Missing list. No trunk code imported. `parity/trunk` was fetched read-only; no imported commit credits apply.

## Source evidence and remaining gaps

Reference pinned at `df5a59167c58ab3735c2a5295db3b3b7243af475`. Read `ConditioningControlPanel/Views/Controls/Companion/WorkshopAccordion.xaml` and `.xaml.cs`, plus the runtime-cell forwards and their live MainWindow handlers. The drawer's collapse/expand, six-cell order, stable anchors, deferred Normal-priority reveal, unknown-anchor fallback, wrapping layout and scaffold/content substitution already exist. Library editing, roster switching and preset CRUD already exist; the old row's blanket claim that presets are missing was stale.

The following WPF behavior is still missing (these are live callers, so P122 does not apply):

| Behavior | WPF reference (under ConditioningControlPanel) | Existing portable pieces / missing host |
| --- | --- | --- |
| Camera capture/reset/rebind, both labels, local/global input, consent-gated webcam toggle; global chat input | `MainWindow/MainWindow.SessionIO.cs`, camera methods and `BtnCameraShortcut_Click`; `MainWindow/MainWindow.BlinkTrainer.cs:355` | WebcamTracker and capture dialog exist. Camera key routing and global input lifecycle do not. A tracker alone does not make the configured key work. |
| Pause/resume browser media and mute state, tube/menu synchronization and session restore | `MainWindow/MainWindow.Patreon.cs:2122-2225` | No embedded browser host to execute media pause/play scripts. This is live state, not a persisted boolean. |
| Locked personality guard, JSON picker/import, companion assignment, immediate application for active companion, refresh and confirmation/error | `MainWindow/MainWindow.CompanionTab.cs:367` | Shared installed JSON lookup and settings exist, but no complete CommunityPromptService import/activation host. |
| Offline/cache-aware catalogue refresh; browse uninstalled entries (first five, install first); import/export; installed cards, activation/deactivation/removal and errors | `MainWindow/MainWindow.Patreon.cs:2334-2490`; `MainWindow/MainWindow.CompanionTab.cs:175-317`; `Services/Companion/CommunityPromptService.cs` | Models/lookup exist. Service, network/cache lifecycle, catalogue UI and installed-list action host do not. Activation also needs acknowledgement/moderation behavior. |
| Idle interval restarts live idle loop; mute updates tube menu; midnight-glass ownership gate | `MainWindow/MainWindow.Patreon.cs` behavior handlers; `Services/Arcademy/ArcademyHostService.cs` | Settings and glass repaint exist; idle loop and wallet ownership host are absent. |
| Trigger mode/interval restart actual phrase/audio timer; recorded-voice markers in editor; live phrase count and saved preset count | `MainWindow/MainWindow.Patreon.cs:1490-1599`; `MainWindow/MainWindow.Assets.cs` preset handlers; tube speech/reaction partials | Settings/editor now reachable. No RestartTriggerTimer or shared complete active phrase/audio service; preset count still uses the documented null-service fallback of zero. |

Scope decision: close the independently reachable settings/editor gaps first. Full parity additionally needs the browser, community lifecycle, global input and tube/audio/ownership hosts above. The WPF community service alone is 500 lines, its catalogue/installed-list UI roughly 300 more, and global hotkey service 272, before head adaptation, browser runtime, tube runtime and focused tests. Those reference sizes are evidence of scope, not a claim that copying them wholesale is necessary. Porting all remaining hosts faithfully cannot fit this unit's 1000-added-line budget. No fake pause state, input binding, ownership result or active phrase count was added.

## Assumptions

- Follow WPF's interval handler exactly: update settings immediately, but do not introduce a disk save on every slider tick.
- Reuse the current head's editor; its existing interaction differences remain outside this unit. Recorded-voice markers require the missing audio lookup.
- Partial implementation is preferable to a dependency-only NOOP because the trigger settings/editor paths have existing portable dependencies and fit the cap. The row remains stub.

## Verification limits

- SDK: channel installation failed with HTTP 403 during discovery; explicit .NET 10.0.100 installation from the builds feed succeeded.
- `dotnet build -c Release` for CCP.Core, CCP.Avalonia and all 13 test projects referenced in `.github/workflows/build.yml`: exit 0 throughout. WPF test build used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; Windows code compiled, not executed on Linux. Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release`: exit 0, zero errors.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed. `bash .github/scripts/test-plan-coverage.sh`: exit 127, script absent on this base. Numbered WPF/Rows steps added manually.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 4117 passed, 5 skipped, 4122 total (existing skips unchanged).
- Regression evidence before the implementation was compiled: the broad Workshop run reported the three new trigger tests failing on the actual gaps (`Assert.True`: restored toggle false; editor save/cancel tests: `Assert.Single`, no dialog). The earliest overlapping-build run had failed XAML loading before assertions; it is not the behavioral evidence.
- Final sequential `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~WorkshopPhrasePresetTests|FullyQualifiedName~PanicSurfacesTests'`: exit 0; 14 passed, 0 failed, 0 skipped. This includes all four added regressions (restore/write, edit/provenance, empty/cancel, panic discard).
- Broad `--filter 'FullyQualifiedName~Workshop|FullyQualifiedName~CompanionZonePills'` before final compilation: exit 1; 3 passed, 8 failed. Five failures were the documented `Could not create glyphTypeface` for Segoe UI Black in shell construction; three were the pre-fix regressions above. Shell font failures were not rerun after the focused suite passed. Full headless suite and render-all not run.
- Tests use the repository's sandboxed bootstrap; no real desktop or user profile is used. No new Windows-specific code was added.

The unit is a partial completion, not a wired parity claim. Remaining runtime hosts are listed above; no PR was opened.
