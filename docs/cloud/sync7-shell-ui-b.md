# sync7-shell-ui-b

- Port WPF `b8703b7c3`: 20x14 drawn language flags in both the chrome and General pickers, including the closed selection box. Retain language tags, full-name tooltips, text search and accessible names. Regional codes share the matching base painter; unknown codes have no flag.
- Port WPF `26f11ee06`: inset both dashboard logo/pass faces by 24 px. Avalonia's rounded Border already clips its children to its CornerRadius; retain that native clipping instead of duplicating WPF's resize geometry helper.
- Port WPF `ca6c3bc4a`: the Discord join control uses a 34 px blue bubble with the WPF vector mark and the existing floating localized label, tooltip and click handler. Preserve the existing Fluent icon API for the other bubbles.
- Read `parity/trunk` only; no trunk code imported.

## Verification limits

Environment: Linux, .NET SDK 10.0.402, `DiffEngine_Disabled=true`, and isolated test/render profiles. Builds used `-c Release -m:1 -p:UseSharedCompilation=false`, `DOTNET_PROCESSOR_COUNT=2`, and the four MSBuild hardlink-copy properties to keep generated output within the shared disk limit.

- `dotnet build` succeeded for **14 projects / 0 errors**: CCP.Core, CCP.Avalonia; CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests; CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests; and ConditioningControlPanel.Tests. The WPF cross-build used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` and completed with 564 existing warnings, 0 errors. Windows tests were compiled, not executed.
- `bash .github/scripts/core-guards.sh`: **All Core guards passed** (429 XAML references checked).
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~ShellSyncSevenTests|FullyQualifiedName~HoverBubbleBarTests|FullyQualifiedName~IconGuardTests'`: **12 passed, 0 failed**. Final invocation reused the already-built Core/head with `--no-restore -p:BuildProjectReferences=false`; those product binaries remained unchanged through the final build sweep. The fixture uses the real RenderProof font registration; non-animation checks set Motion Off and restore it. Existing hover/pulse tests retain their motion assertions.
- Regression proof: `LogoAndWeeklyPassShareTheInsetAndResizeTogether` failed before the fix (expected 24 px, actual 0). Tests also caught Avalonia's missing closed-selection template and the larger un-clipped drawing bounds: the final implementation explicitly sets SelectionBoxItemTemplate and DrawingImage.Viewbox. The final tests paint every flag and the real closed Italian picker, check distinct pixels/rounded corners, and exercise Discord's real dressed control and one-open-label behavior.
- `dotnet test Tests/CCP.Core.Tests -c Release` and the final `--no-build` rerun: **2845 passed, 1 failed, 5 skipped** each. The unchanged `BoardTilesTests.A_frame_fits_the_budget` was the sole failure (22.55 ms / 21.70 ms against a 12 ms budget during shared load). `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget`: **1 passed** in isolation. Five Vosk model-dependent cases skipped because the external model is unavailable. No Core test expectations were changed.
- Final `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /workspace/scratch/root-shell-ui-b-render`: **225/225 views rendered**, exit 0. Inspected `SettingsTabView.png` (rounded/inset logo and blue Discord bubble), `GeneralSettingsSection.png` (closed flag/name row), and `MainShellWindow.png` (chrome flag and dashboard bubble). An earlier render was interrupted before the final template rebuild; the completed final render ran with all own builds stopped.
- `git diff --check` and `git diff --cached --check`: passed.

The sandbox does not open the public Discord URL; its existing click handler is preserved, but the actual external launch was not executed. Native web-view packages are absent, so the standalone dashboard render includes the existing web-view-unavailable placeholder.

Changed files: head `Controls/LanguageFlag.cs`, `Controls/HoverBubbleBar.cs`, both language-selector hosts, `Views/Tabs/SettingsTabView.axaml`; `ShellSyncSevenTests`, `HoverBubbleBarTests`, the tightened `icon-emoji.count` (173 -> 172); both sync/parity ledgers and this handoff.

Windows desktop execution and a WPF/Avalonia side-by-side comparison are unavailable in this Linux environment. Existing parity rows stay `stub`/`wired` according to their remaining work.
