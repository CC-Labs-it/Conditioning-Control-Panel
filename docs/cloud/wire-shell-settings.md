# wire-shell-settings

- Claimed branch: `avalonia-port/cloud-wire-shell-settings`, parent claim `c0a2003de7592f96f2fbefb7bb62734f12c73d5c`; base `avalonia-port/goongame-core-c`.
- Restored every Help guide entry point to the existing tutorial engine. The shell browser is hidden through the tour and restored on close/panic; the existing `tour` panic surface still owns the lifecycle. Modding opens the owned Mod Creator with its tutorial; Awareness reuses its existing one-shot Puppy-editor completion path, which refuses a second active tour.
- Save now persists the live settings before offering a preset. New, overwrite, save-as, cancel and duplicate-name refusal reuse the Presets page's existing CRUD/input code. Overwrite preserves id, creation date and description; cancel leaves the settings save intact. No second settings-control reader was introduced.
- Folder changes discover encrypted packs in the old and default roots, ask before moving, use Core `PackFolderMover`, then update settings, rescan installed packs/the asset tree and clear dealt video queues. Refusal changes the chosen folder while preserving the original pack files. The existing default-root resolver is reused.
- Removed the WPF migration/copy/size duplication: WPF now delegates to the same existing mover, which checks the copy before deleting the source. Moved the WPF exact-root refusal rule to Core `AssetFolderSafety`; both heads delegate to it, including the OneDrive/Common-folder cases the old Avalonia copy omitted.
- Added headless tests for shell actions, preset dialogs, natural/abandoned Awareness tours, owned Mod Creator/report windows and both migration answers using only synthetic packs in scratch directories. Related headless test setup now uses the shipped font configuration.
- Read WPF MainWindow.Settings.cs and its folder-picker/preset helpers. Fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk implementation was imported.

## Remaining work: wire-shell-settings-b

The row remains **stub**. The functional shell paths above are connected; completing the WPF narration/FX services and their behavior coverage would exceed this slice's 1000-added-line budget:

- EMI tour narration (`EmiTourNarrator` and its Desk moment-bus integration).
- Save's tick/ripple animation (`FlashSaveAbsorb`, WPF HeroFx).
- Localize the native English Yes/No preset-offer labels. The existing localized WPF offer text, titles and messages are reused.
- Mod Creator's deeper tutorial section-navigation gaps remain owned by its existing window/tour lanes.

## Verification limits

Commands used SDK 10.0.402, `DiffEngine_Disabled=true`, and all three SDK hard-link flags to avoid copying dependency/content trees:

```
-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true
-p:CreateHardLinksForAdditionalFilesIfPossible=true
-p:CreateHardLinksForCopyLocalIfPossible=true
```

- Regression proof before the wiring: `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~ShellSettingsWiringTests` failed both original tests: guide click left `CoreTutorial.IsActive` false, and Save opened no owned offer dialog (`/tmp/worker5-settings-before.log`).
- `bash .github/scripts/core-guards.sh`: both guards passed.
- Explicit `dotnet build` of Core, Avalonia, and every Linux workflow test project passed (13 targets, zero errors). Test projects: CCP.Avalonia.Tests, CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot; CCP.Core.Tests, Content, Settings. Auxiliary test builds used `-p:BuildProjectReferences=false` after the heads were built; only this worker's generated auxiliary bins were removed afterward.
- New shell wiring suite: six cases passed initially; the remaining case failed because its lookup used the XAML namescope for a programmatically added button. Its corrected lookup now locates the actual visual button; no assertions were removed or weakened. The corrected case plus FirstRunFolderPickerTests, StudioPresetsWireTests, TutorialTourTests and LinuxMediaDefaultTests passed: 19/19 (`/tmp/worker5-settings-related-tests.log`).
- WPF test build attempted with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -p:UseSharedCompilation=false -m:1`, bounded to 180 seconds. It timed out in the WPF temporary-project compile; no successful WPF compile or execution is claimed.
- No real desktop, live report submission, real user profile, or paid pack was exercised. Tests use CCP_USERDATA_DIR sandboxes and synthetic encrypted pack files.
- `dotnet test Tests/CCP.Core.Tests --no-build --no-restore`: 2815 passed, 0 failed, 6 existing skips (board-frame performance test plus five optional Vosk-model cases), 2 m 7 s. After adding explicit OneDrive coverage, `--filter FullyQualifiedName~AssetFolderSafetyTests` rebuilt the Core test project and passed 8/8, including all three OneDrive environment-variable variants. The test restores each variable in its own process and never creates/reads a real OneDrive folder.
- Captured and inspected `/tmp/worker5-settings-evidence/SettingsPresetOffer.png` from the real overwrite/cancel/save-as test: all three Yes/No/Cancel choices and the saved-first message fit. Reproduce with `CCP_SETTINGS_EVIDENCE_DIR=<dir>` on the focused overwrite test.
- `CCP_USERDATA_DIR=/tmp/worker5-settings-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/worker5-settings-render`: passed, 224/224 views rendered. `MainShellWindow.png` shows the shell entry points; the live preset offer is the separate test capture above.
