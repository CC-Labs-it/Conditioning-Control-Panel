- Fixed the remaining Preset I/O route: a file dropped on the launcher now brings the linked panel up, hides the launcher through its existing exit handoff, and calls `MainShellWindow.ImportDroppedFilesAsync`.
- Matched WPF `LauncherWindow.xaml.cs:147-159` and `LauncherHost.OpenPanelWithDrop`: accept file data on drag-over; let the existing panel importer determine the file type and report success/invalid content. Reused the panel's local-path extraction and Core `DropRules`, `PresetDropRules` and `PresetFileService`; no second importer or pure-rule copy was added.
- Added `LauncherPresetDropTests` using real headless drag events and isolated profiles. Normal `.preset.json`, browser-renamed `.preset (1).json` and content-sniffed `.json` files import after the panel appears; invalid content imports nothing; the source remains intact.
- Updated only ledger row `shell-preset-io` to **wired**. No visible layout, new surfaces, new icons or WPF code changed.
- No trunk code imported. Read-only `parity/trunk` inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; the WPF behavior already on the brief's base was the spec.

## Verification limits

Commands used .NET SDK 10.0.402 with `DiffEngine_Disabled=true`, the shared 1.5 GiB GC heap cap, serialized checks, repository hardlink copy flags and isolated test profiles.

- Before the fix: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~LauncherPresetDropTests`: **3 failed / 0 passed**, proving the launcher did not allow file drops.
- After the fix (including invalid-content coverage): the same filtered command gave **4 passed / 0 failed / 0 skipped**.
- `dotnet build CCP.Core -c Release` and `dotnet build CCP.Avalonia -c Release`: passed.
- All 11 Linux test projects named in `.github/workflows/build.yml` built successfully: Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Report, Avalonia.Scheduler and Avalonia.Snapshot.
- `bash .github/scripts/core-guards.sh`: both guards passed.
- Full `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore` with the checksum-checked Vosk model required: **2816 passed / 0 failed / 0 skipped**.
- WPF cross-build was not rerun for this head-only change: the earlier same-base command in this session, `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`, failed reading `ConditioningControlPanel.g.resources` with `CS1566` / `OutOfMemoryException` under the shared heap cap. WPF execution and native Windows desktop drag-and-drop cannot run here.
- Render-all was not rerun: this change adds behavior to existing windows and changes no visible layout. The user path is exercised through headless raw drag events.
