# fin4-shell-assets

- Add shared voice-line listing for the existing phrase editor and Workshop preset active count: packaged mod folder first, per-mod install/download union next, eligible baseline last; WPF extensions and filename ordering, disabled/removed rows, stable IDs and deferred positional-ID migration.
- Forward keyword-list and patch-notes wheel input to the nearest outer viewer even before the inner list reaches its boundary, matching WPF. Quests already has a horizontal-only viewer that bubbles vertical wheel input.
- Restore Windows shell video thumbnail selection and icon fallback through a small Windows-only seam; keep OpenCV first-frame decoding off Windows. Pack temp extraction/deletion stays in its existing caller.
- Add focused Core/headless regressions and numbered manual steps. Reuse existing dialogs/windows and panic registration; no new surface.
- Keep the ledger at `stub` with a specific online-consumer dependency. No WPF reference files changed.
- Import source fetched read-only: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported in this unit; existing pack code retains its original attribution.

## Gap inventory and scope

At base `7ef559b1c77e5daa728bc97f113fe5d51b53afa1`, the row named missing wheel helpers, voice-line counts, Windows shell frame choice and online flash/video consumption/pool invalidation. The first three are addressed here.

The packs strip is unreachable in WPF: `git grep -n PacksSectionEnabled 7ef559b1c77e5daa728bc97f113fe5d51b53afa1 -- ConditioningControlPanel/MainWindow` finds the `const false` declaration at MainWindow.xaml.cs:208; TabNavigation.cs:577 guards strip refresh and the shell markup collapses the strip controls. Refresh/download/activate/upgrade/account links, callbacks, rotation and strip wheel have no live strip caller. No drag/drop handler exists in MainWindow.Assets.cs. This is evidence for exclusion, not a promotion under P122.

Remaining dependency: the online halves of WPF FlashService and VideoService. The present head has FlashOverlay's local/pack selection and MandatoryVideoScheduler.PickNext's local/pack queue. Neither reads MediaSource or fetches remote entries. WPF's remote flash path (FlashService.cs:3755 onward) includes coordinator channels, GIF/clip fetching/cache, mixed source selection and pool invalidation; VideoService.cs:8154 onward adds remote queue/consent/format/blocklist handling. Together those sections alone approach the unit's line allowance before adaptation and tests; adding the playback host paths to the changes here exceeds 1000 additions. Picker and preset state are already reachable, but state cannot substitute for playback. This is a partial implementation, not a pure-dependency NOOP.

## Verification limits

All builds used Release; test runs used `DiffEngine_Disabled=true`.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors.
- Every Linux test project in build.yml compiled with zero errors: Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.Tests, Avalonia.Consent.Tests, Avalonia.Language.Tests, Avalonia.CatalogueStartup.Tests, Avalonia.Scheduler.Tests, Avalonia.Report.Tests, Avalonia.GeneralSettings.Tests, Avalonia.Snapshot.Tests. Initial attempts hit a shared PDB lock and disk exhaustion copying native libraries; sequential retries cleaned completed outputs and passed.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exit 0; 2814 passed, 5 skipped, zero failed. Existing skips include unavailable Vosk models; this unit did not disable tests.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~ShellAssetsCompletionTests|FullyQualifiedName~AssetsTabTests|FullyQualifiedName~WorkshopTriggersCellTests'`: exit 1; 3 passed, 11 failed. All 11 failures report font creation errors (Inter or Segoe UI Black), matching the documented environment limitation.
- Fail proof: temporarily removed the two fixes and ran ShellAssetsCompletionTests: exit 1; voice rows were empty instead of a/b; wheel offset was 50 instead of 120. Restored fixes and ran `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release -m:1 --filter 'FullyQualifiedName~ShellAssetsCompletionTests'`: exit 0; 3 passed, zero failed/skipped.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. `git diff --check`: exit 0. The test-plan coverage script is absent on the pinned base; numbered WPF tester steps are provided.
- WPF test-project cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: initial attempt exhausted disk copying dependencies after compiling the WPF head; retry exited 0 with zero errors (2 warnings). The WPF tests were compiled but not run on Linux.
- Windows shell COM/GDI code compiled in the Linux Avalonia build but was not executed. Windows thumbnail success/frame comparisons, WPF tests, full headless suite and render-all were not run. No new surface or panic registration was necessary; existing dialogs retain their registration. Tests use the repository's CCP_USERDATA_DIR sandbox and memory-only secret store.

## Assumptions

- Retain OpenCV first-frame thumbnails off Windows, where Explorer providers cannot run.
- Retain Quests' native vertical-wheel bubbling; explicitly forward only the two WPF always-forward inner viewers.
- Keep the packs strip disabled as in the pinned WPF reference.
