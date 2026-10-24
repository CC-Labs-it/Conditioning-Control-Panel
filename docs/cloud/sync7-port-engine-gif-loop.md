# sync7-port-engine-gif-loop

- Audited WPF `734a9e477bd4778f00d7918e5853f6aac8ca7cd9` (`fix(flashes): GIF flashes keep animating round after round`) against base `39260f198fad67d90bc988c63a00480830e5fefc`.
- **734a9e477: blocked-by-host.** The Avalonia flash host has neither the remote clip warm/ready pool and URL-keyed decode cache nor a flash clip player with admission slots. `CCP.Avalonia/Views/Overlays/FlashOverlay.cs:450` loads local images, opens each selected file, and returns a single decoded `Bitmap` at line 478. `CCP.Avalonia/Views/Overlays/FlashOverlayWindow.cs:223` presents that bitmap in an `Image`. The explicit pending-animation note is `CCP.Avalonia/Views/Overlays/FlashOverlay.cs:36`.
- The WPF change owns materialized remote files outside the shared sweep, reuses a URL's existing file, refreshes cached decodes with the current clip path, trims oldest-used files (soft cap 64, hard cap 128, recent window 120 seconds), releases them at teardown, and retries cap-refused posters when a player slot becomes available. These operations have no corresponding flash runtime on this base. Existing video players elsewhere in the head do not supply the missing flash pipeline.
- No production code, unused Core helper, test, surface, parity row, or sync ledger was added or promoted. Building the missing hosts would re-port sibling work outside this unit. No new panic registration is needed.
- Read-only source fetch: `parity/trunk` tip `a4ee52d40`. No trunk code imported; the audited WPF source is credited above.

## Verification limits

All commands used .NET SDK 10.0.402 and Release configuration. Tests used `DiffEngine_Disabled=true`.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors (existing warnings).
- `dotnet build <project> -c Release`: exit 0, 0 errors for every Linux test project listed in `.github/workflows/build.yml`: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, and `CCP.Avalonia.Snapshot.Tests` (each under `Tests/<name>/<name>.csproj`).
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed (429 XAML references checked). The initial read-only sandbox invocation could not create temporary files; rerunning with filesystem access passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127, script absent from this base. No ledger changes require coverage.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 3,952 passed, 1 failed, 5 skipped. Existing `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` expected `Loops`, actual `Clips`. No production or test files changed in this branch. Five speech tests skipped because the Vosk model was unavailable; the suite is not fully verified.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~FlashOverlayPlacementTests|FullyQualifiedName~A_relisted_folder_is_walked_in_full_before_anything_repeats|FullyQualifiedName~A_random_burst_rolls_its_count_inside_the_range'`: exit 0; 4 passed, 0 failed, 0 skipped. These existing flash math/selection tests do not open UI or require fonts.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; 557 warnings, 0 errors. WPF app and tests cross-compiled; not run on Linux.
- `git diff --check`: exit 0. Added-line count against `origin/avalonia-port/main-20261024-compat`: 32 (documentation only).
- UI/font-dependent Avalonia tests were not run, per the reported environment font limitation. Other test projects were compiled, not executed. Windows runtime behavior cannot be run on Linux.
- No regression test or production-line reversal can demonstrate this fix until the missing flash hosts exist; this document does not claim the commit is ported or already covered.

## Assumptions

- Follow the explicit blocked-by-host instruction for the whole commit rather than adding a disconnected file-ownership helper. The blocker includes both remote file lifetime and cap retry behavior.
- Existing first-frame local GIF display is not equivalent to the WPF remote animated clip path.

## Blocker

The remote flash pool, URL decode cache, and animated flash clip player/admission surface must land before `734a9e477` can be adapted and behavior-tested. Revisit this unit on a base containing those hosts.
