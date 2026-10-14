# wire-views-tab-assets

- Branch: `avalonia-port/cloud-wire-views-tab-assets`; base: `avalonia-port/goongame-core-c`.
- Ported the active Content Packs browser: virtual pack/images/videos folders, checked counts, selection keys, folder and file opt-outs, active asset counts, and the selected pack folder retained after refresh.
- Added shared Core `AssetPackTree.Build`; the WPF `BuildPackTree` calls it too. Tests cover image/video ordering, disabled selection keys, empty packs, and fully disabled packs.
- Pack image thumbnails decrypt directly to a stream and reuse the existing four-way thumbnail gate. Context Preview decrypts to a temporary path, uses the existing MiniPlayerWindow, and deletes that path on close.
- Registered `asset-preview` in PanicSurfaces and the safety-critical test list; panic closes every open MiniPlayerWindow and releases temporary decrypts. Removed its outdated CLI-only exception from the starter coverage test.
- The headless Assets fixture enters through Library, opens an encrypted pack, verifies its decoded thumbnail and counts, deselects/reselects files, refreshes the selected folder, opens the real tile context menu, and checks panic cleanup. Its setup uses the application's shipped font configuration.
- Reference: WPF AssetsTabView.xaml/.cs and MainWindow.Assets.cs on the brief base. Fetched `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` read-only; no trunk code was imported.

## Remaining work: wire-views-tab-assets-b

The ledger remains **stub**, never verified. The full remaining WPF packs catalogue/UI exceeds the brief's line budget when included with focused coverage:

- Hidden pack catalogue cards: refresh, bulk delete, download/install/status/progress/rate-limit dialogs, activate/deactivate, preview images/carousel, card FX, and the corresponding ContentPackService/ContentPackStore change callbacks.
- Hidden creator Discord button and PacksScrollViewer wheel-to-horizontal-pan handlers.
- Platform video thumbnail extraction for local and encrypted pack videos. Videos retain the existing Fluent Video placeholder.
- Flash/video consumption of online media is an existing separate service gap recorded by the row.

The old ledger note about missing Assets/Packs/AssetBrowser help popups is superseded: MainShellWindow.HelpButtons.cs already attaches those three popovers globally. This slice does not change their behavior.

## Verification limits

All commands used .NET SDK 10.0.402. Builds were serial, `-m:1 -p:BuildInParallel=false`; once Core/head built, Linux auxiliary test builds used `-p:BuildProjectReferences=false`. After the first output-copy attempts exhausted disk space, only this worker's generated bins were removed and builds used all three SDK hard-link flags:

```
-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true
-p:CreateHardLinksForAdditionalFilesIfPossible=true
-p:CreateHardLinksForCopyLocalIfPossible=true
```

- `bash .github/scripts/core-guards.sh`: passed, both guards.
- `dotnet test Tests/CCP.Avalonia.Tests --filter 'FullyQualifiedName~AssetsTabTests|FullyQualifiedName~PanicSurfacesTests'` with `DiffEngine_Disabled=true`: 13 passed, 0 failed, 0 skipped (1 m 24 s).
- `dotnet test Tests/CCP.Core.Tests --no-build --no-restore` with `DiffEngine_Disabled=true`: 2812 passed, 0 failed, 6 skipped (the existing board-frame budget test and five optional Vosk-model tests), 2 m 29 s. No tests were weakened or newly skipped.
- `dotnet build CCP.Core/CCP.Core.csproj` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj`: passed, 0 errors.
- Explicit `dotnet build` for every Linux workflow test project passed: CCP.Avalonia.Tests; CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler and Snapshot Avalonia suites; CCP.Core.Tests, Settings and Content suites.
- `CCP_USERDATA_DIR=/tmp/worker5-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/worker5-render`: passed, 224/224 views rendered. Inspected `/tmp/worker5-render/AssetsTabView.png`; it shows the browser scaffold without a pack fixture. The encrypted pack path is exercised by the headless behavior test, not by render-all's empty view factory.
- No real desktop, Windows runtime, live pack download, or real user profile was exercised. WPF execution cannot run on this Linux host. Video thumbnail extraction remains deferred.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (plus the same serial/hard-link flags): attempted, then stopped after approximately ten minutes without a result; last output was the WPF temporary-project compile and NU1510 warning. The WPF adapter is **not claimed compiled**. Windows CI must finish that build and execute the WPF suite.

## Publication blocker

The final `git push origin avalonia-port/cloud-wire-views-tab-assets` failed with HTTP 403: `Permission to CodeBambi/Conditioning-Control-Panel---CSharp-WPF.git denied to ClearlyNotMich.` The empty claim is already on origin, but this implementation is only committed locally. No force push, alternate credentials, or PR was attempted. A patch bundle is preserved at `/workspace/scratch/wire-views-tab-assets.patch` for an authorized maintainer to apply to the claimed branch.

## Account correction for publication retry

The publication-block notes above describe the earlier `ClearlyNotMich` attempt. The user selected `ObviouslyNotMich` for this retry; that connection reports repository push permission and successfully uploaded a Git blob. The current origin branch head is the publication source of truth.
