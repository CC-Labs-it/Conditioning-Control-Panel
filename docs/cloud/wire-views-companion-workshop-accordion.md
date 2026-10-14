# Workshop phrase preset slice

- Restored the reachable Triggers & Phrases preset selection, Save and Delete controls. Selection calls the existing shared Core `PhrasePreset.ApplyToSettings`; Save calls `FromCurrentSettings`. No duplicate settings/snapshot rules were introduced.
- Restored the persisted selected ID on mounting and companion-tab reentry, without applying a saved snapshot over the live configuration. User selection writes settings and preserves Core deep-copy semantics.
- Save matches WPF's 350×150 centered, non-resizable name prompt, default `Preset N`, literal prompt, button order, margins and trimmed name. Delete uses the existing head's confirmation helper with the WPF text, including the no-selection message.
- Added three headless tests through the visible existing buttons, covering save/deep copy, restore versus user selection, and cancel/confirm deletion. All three failed against the original head; all three passed with the slice.
- Read WPF `MainWindow.Assets.cs` phrase preset handlers and the Workshop/Triggers views. Fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported.
- The whole row stays `stub`. Follow-up **wire-views-companion-workshop-accordion-b**: camera shortcut/global hotkey plus live webcam routing; browser pause/suspend; live roster switching/personality; community prompt service browse/import/export/refresh; phrase Edit/Manage flows, trigger timer and active phrase pool/count. New presets use WPF's null-service count fallback 0 while that service is absent. This slice changes saved phrase configuration; it does not provide an absent companion runtime.

## Verification limits

- `dotnet build` Release of `CCP.Core`, `CCP.Avalonia` and all 11 Linux test csproj named in `.github/workflows/build.yml`: all exit 0. These used `-m:1`, disabled shared compilation and MSBuild copy hardlinks, with a 1.5 GiB per-process GC cap. Pre-existing warnings remain; the final main head-test build reported 50 warnings / 0 errors.
- `bash .github/scripts/core-guards.sh`: exit 0; 424 XAML references checked, all Core guards passed.
- `DiffEngine_Disabled=true CCP_REQUIRE_VOSK=1 CCP_VOSK_MODEL=<verified model> dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore`: **2816 passed, 0 failed, 0 skipped**, 1m58s.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~WorkshopPhrasePresetTests`: original head **0 passed / 3 failed**; implementation **3 passed / 0 failed / 0 skipped**. Repeated using `--no-build --no-restore` from the final mandatory build: **3 passed / 0 failed / 0 skipped**, 8s.
- `CCP_USERDATA_DIR=<sandbox> dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /workspace/scratch/worker-a-workshop-render-all`: exit 0, **224/224 views rendered**, 224 PNGs. Inspected `WorkshopTriggersCell.png` (preset selector and Save/Delete buttons) and `WorkshopAccordion.png` (collapsed default header). The modal name prompt is dynamically built and is not a standalone RenderProof type; its behavior/dimensions were exercised headlessly, with no native Windows visual comparison.

- Tests and rendering use isolated `CCP_USERDATA_DIR` profiles; the headless secret store is memory-only. No real desktop or user data was used.
- No WPF source or Core behavior changed. The conditional Linux WPF cross-build was attempted on this same base earlier in this session with `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false`: C# compilation completed, but final resource embedding failed CS1566/out-of-memory on `ConditioningControlPanel.g.resources` under the memory cap. A working WPF/test build and native Windows execution were not available; this unchanged WPF target was not rerun.
- Native Windows modal chrome, global hotkeys, webcam, live companion playback and community server behavior were not run. This head reuses Avalonia modal chrome; WPF ToolWindow decoration was not visually compared.
