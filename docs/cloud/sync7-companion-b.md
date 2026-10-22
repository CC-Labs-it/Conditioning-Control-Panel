# sync7-companion-b

Base: `avalonia-port/main-20261015-compat`. Branch: `avalonia-port/cloud-sync7-companion-b`.

- Port the full prompt editor part of WPF `24756ace8`: open on the running persona when the custom prompt is off, preserve hidden stored custom fields on an off/off save, and persist only edits on a fork. Reuse Core `ActivePersonaPromptText`.
- Port the subliminal part of WPF `ec388b526`: Fluent silent-speaker marks, the spoken-list hint, and the enabled-only Audio Whispers hint. The UI calls the same recorded-clip lookup as playback; mod changes refresh the hint. Non-spoken editors remain unmarked.
- Port WPF `1249e3846` switch feedback; prove the already-merged Core `SlutModeBite`, `PromptAssembler` and `ChatSpice` from `43a671569` are used through the actual Avalonia shell switch and tube send path.
- No trunk files imported. Read-only import source fetched at `parity/trunk`.

## Follow-up: sync7-companion-b-next

This branch is a coherent slice; affected parity rows stay stub. Main-sync rows 8 and 27 remain needs-port for their unfinished halves. Rows 295 and 296 also remain needs-port.

- `24756ace8`: PersonalityStudio awareness reactions and field headroom.
- `ec388b526`: companion random-trigger editor/player path. WorkshopTriggersCell's random-trigger editor and timer are still stubbed; the distinct custom-keyword trigger editor is not the same host.
- `df219f44d`, `8e1e7f830`: personality studio editing/deleting user presets, copy-on-prompt-override protection, single-line name box, non-overlapping footer. This head lacks WPF's V2 PersonalityPage/CompanionPickerCard studio entry path; no inert studio or invented entry button was added.

## Verification limits

Commands used `PATH=/workspace/shared/dotnet:$PATH`, `DOTNET_PROCESSOR_COUNT=2`, `DiffEngine_Disabled=true`, `-m:1 -p:UseSharedCompilation=false`. Final output copies used all four `CreateHardLinksForCopyFilesToOutputDirectoryIfPossible`, `CreateHardLinksForCopyLocalIfPossible`, `CreateHardLinksForAdditionalFilesIfPossible`, `CreateHardLinksForCopyAdditionalFilesIfPossible` properties set to `true`.

- `dotnet build CCP.Core/CCP.Core.csproj` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj`: succeeded, 0 errors.
- Built every Linux test project named by `.github/workflows/build.yml`: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Snapshot.Tests`: succeeded, 0 errors.
- `dotnet test Tests/CCP.Core.Tests`: 2845 passed, 6 skipped, 0 failed (2851 total). Five existing Vosk speech tests and the existing board frame-budget test skipped under their existing environment guards. No test was skipped or weakened by this branch.
- `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~CompanionSync7EditorsTests`: 4 passed, 0 failed. The switch/real tube-send test keeps the actual shell unshown and Motion off; the full-shell layout is a separate unresolved render limit below. The test still checks every non-explicit preset's Core prompt, the switch's bubble text and the actual tube-to-brain request's long-reply instruction/token cap.
- WPF cross-build attempted: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (same bounded/hardlink flags). Restore and Core dependency succeeded, but the unchanged WPF app was still compiling after 20 min 12 s; terminated that attempt. The WPF test project did not complete and was not run.
- Final restored build: `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj --no-restore` with the bounded/hardlink flags: succeeded, 0 errors. Then `dotnet test Tests/CCP.Avalonia.Tests --no-build --no-restore --filter "FullyQualifiedName~CompanionSync7EditorsTests|FullyQualifiedName~CompanionEditorsTests.PromptEditor|FullyQualifiedName~SubliminalWhisperShowTests|FullyQualifiedName~IconGuardTests"`: 17 passed, 0 failed. No builds or output copies ran in parallel with this final runtime check.
- Added lines versus base: 351 (limit 1000).
- Pre-fix proof: temporarily restored only `CompanionPromptEditorDialog.axaml.cs` from the named base, then ran `DisabledCustomPromptShowsRunningPersonaAndPreservesStoredText`: 1 failed, 0 passed (expected running persona; actual `old custom voice`). Restored the intended source byte-for-byte afterward.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet run --project CCP.Avalonia --no-build -- --render-all /workspace/scratch/worker3-render`: produced 151 PNGs, then stopped producing output at MainShellWindow initialization; terminated after more than three minutes without another PNG. This is an incomplete all-view run, not a pass.
- `dotnet run --project CCP.Avalonia --no-build -- --render-view TextEditorDialog /workspace/scratch/worker3-render/TextEditorDialog.png`: exit 0. Inspected this PNG: the spoken-list hint and silent-speaker mark are visible, with separate columns for the mark and Remove.
- Initial attempts hit the shared compiler failure, output-copy disk exhaustion and the bare test-builder's missing Inter/Segoe fallback. Retried with bounded non-shared compilation, hardlinks and the production `RenderProof.EnsureSetUp` bootstrap; no assertions were removed. A combined legacy full-shell test run was terminated when it stalled; no pass is claimed for `CompanionPersonalityZoneTests` or the whole Avalonia suite.

WPF/Windows desktop, real recorded audio output and side-by-side WPF visual comparison were not run on this Linux headless host. No row is marked verified.
