# wire-win-mod-creator

- Ports the complete Advanced panel from the WPF reference: avatar tube offsets/scale, Bubble Scale, ten skill-tree labels, and add/remove boost/stat-pill tooltip rows. It is reachable through Mod Manager → Create → Advanced; existing load, reset and export hooks now read the editable controls.
- Moves tube-number parsing/clamping, trimmed/capped text, tooltip limits/last-wins rules, and `ModBubbleScaleRules` into CCP.Core. Both WPF and Avalonia consume those rules; no head references in Core.
- Adds focused Core rule tests and headless form tests for editing/export, removal, loading/sanitization and replacing loaded values with a blank manifest. The existing Pools/Personalities tests remain intact.
- Keeps `win-mod-creator` **stub**. Follow-up lane **wire-win-mod-creator-b**: UI Art + Art Framing/crop editor and ArtFraming export preservation; Barks, Mantras, Event Audio, Portraits, Animated Emotes panels and side-file load/export; audio-preview stop/toggle; tutorial step rewriting. Together those WPF partials exceed this brief's 1,000-added-line limit, so Advanced is the coherent completed slice.
- No code imported from `parity/trunk` (fetched/read-only). Adaptation source: WPF `ModCreatorWindow.Advanced.cs` on base `avalonia-port/goongame-core-c`.

## Verification limits

- `dotnet build` passed (0 errors) for CCP.Core, CCP.Avalonia and all 11 Linux test projects named in `.github/workflows/build.yml`: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests and CCP.Avalonia.Snapshot.Tests. Logs: `/tmp/worker3-build-<project>.log` and `/tmp/worker3-build-results.txt`.
- Builds used `-m:1 -p:BuildInParallel=false`; after the head/Core build, auxiliary projects used `-p:BuildProjectReferences=false`. Three SDK hardlink flags (`CreateHardLinksForCopyFilesToOutputDirectoryIfPossible`, `CreateHardLinksForAdditionalFilesIfPossible`, `CreateHardLinksForCopyLocalIfPossible`, all `true`) resolved initial no-space copy failures. Successful final builds supersede those failed attempts; no source/test changes were used to avoid the failures.
- Fail-proof: with Advanced's sidebar still constructing its deferred panel, `dotnet test Tests/CCP.Avalonia.Tests --no-build --filter FullyQualifiedName~AdvancedPanelEditsLayoutLabelsAndTooltips -v:normal` failed (1 failed, 0 passed) because `Avatar Tube Layout` was absent. Then the production panel call was restored; final results below.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `CCP_USERDATA_DIR=/tmp/worker3-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/worker3-render`: exit 0, 224/224 views rendered; `ModCreatorWindow.png` shows the default Info panel. An out-of-tree preview runner called the same `RenderProof.Run` with sidebar navigation to Advanced: exit 0; `/tmp/worker3-render/ModCreatorWindow-Advanced.png` shows the changed panel and was inspected. No binding errors were reported for either Mod Creator render.
- Windows UI behavior cannot be run on this Linux worker. This slice adds no window, media playback, timer or overlay requiring a new panic surface.

- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (same hardlink/serial flags): succeeded, 509 warnings, 0 errors, 11m57s. The WPF app and test assembly compiled; neither was executed on Linux.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests --no-build --filter FullyQualifiedName~ModAdvancedRulesTests`: 8 passed, 0 failed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests --no-build --filter FullyQualifiedName~ModCreatorPanelsTests -v:normal`: 6 passed, 0 failed (including both new Advanced tests).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests --no-build`: 2,818 passed, 0 failed, 6 skipped (the existing optional Vosk-model tests and frame-budget test). No test skips were added or changed. Final focused ModCreatorPanelsTests rerun: 6 passed, 0 failed.

## Publication blocked

`git push origin avalonia-port/cloud-wire-win-mod-creator` was attempted once after the implementation commit and failed with:

> remote: Permission to CodeBambi/Conditioning-Control-Panel---CSharp-WPF.git denied to ClearlyNotMich.
> fatal: unable to access 'https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF.git/': The requested URL returned error: 403

The remote branch remains at claim `60f3bd7137b76603a9ef466bee541a9cef13fe08`; implementation commit `132232a489bd317f735c92a77ffac77e6f2aa393` and this handoff are retained locally. No credentials, identities, other branches or PRs were changed. A complete claim-to-HEAD patch is preserved at `/workspace/scratch/wire-win-mod-creator.patch` for publication when write access is restored. No further brief was claimed after the publication block.

Added lines versus base: 630 (limit 1,000).

## Account correction for publication retry

The publication-block notes above describe the earlier `ClearlyNotMich` attempt. The user selected `ObviouslyNotMich` for this retry; that connection reports repository push permission and successfully uploaded a Git blob. The current origin branch head is the publication source of truth.
