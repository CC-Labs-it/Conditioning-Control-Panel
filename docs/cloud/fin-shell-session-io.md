# fin-shell-session-io

- Restore the WPF media-drop choice order and localized copy: Play, Edit, Add to Asset Library, Cancel. Edit creates an unsaved project with the correct media type/source; Play reuses the existing player. Closing or panic cancels the chooser. The shared player is registered for panic close.
- Import dropped enhancements with the existing Core EnhancementLibrary and EnhancementImportRules: schema sniff, validation, normalized-content duplicates, promotion, source-directory memory, summary/error toasts, immediate reload and 1800 ms row highlight/reveal. Source files are retained.
- Forward launcher drops through its existing panel-opening transition, then the shell's single import door. Invalidate the video queue after video imports and reuse the Assets tab's refresh; Flash already enumerates current images on every burst.
- Restore camera shortcut capture in Workshop and Settings, repaint both shortcut labels, share WPF key/modifier parsing with avatar chat, and rebind without restart. Register Windows global shortcuts with MOD_NOREPEAT on a dedicated message thread; silently retain in-window fallback on registration refusal. Dispose old registrations before replacements and on shell close. Sandboxed profiles never register real OS shortcuts.
- Add focused tests for import/deduplication/reveal timing, media choice routing/cancel, launcher drops, cache refresh, panic cleanup, modifier defaults and registration disposal. Keep the ledger `stub`: the existing player's audio engine and enhancement playback binding are dependencies, and non-Windows global registration remains absent.
- Reference/import credit: consulted read-only parity/trunk commits `9907e135eecada51f73b08cb4ce5aa355486acc4` and `5acf94a5f138213fdc0d767c7824e4117dd42411` for the enhancement-drop door. Adapted behavior directly from the WPF reference at `888cdbffe4d2ecf3967cee4a4334557ee533e54d`; no existing base file was overwritten from trunk.

## WPF gap audit at the base

The row's actual outstanding behaviors were:

1. Single playable media: the four-choice modal, Play into the shared player, Edit into an unsaved audio/video project, cancel and failure handling. The base offered only Library/Cancel.
2. Enhancement drop: importable-path/schema checks, loading errors, duplicate detection, promotion, last-source-directory persistence, refresh, localized summary/skipped/error/already-present notifications and timed row reveal. The base always reported that the library was not ready.
3. Imported media refresh: the video scheduler retained old queued paths. Flash already scanned disk each burst; the existing Assets tab already refreshed its tree on navigation, including repeat navigation.
4. Launcher drag acceptance and panel-open-before-import routing.
5. Global chat/camera registration, immediate rebind/unregister, chat foreground/restore/show-avatar/open-input sequence, camera consent-aware toggle, and the camera capture/label surfaces. Chat's existing local capture was consolidated into the same WPF-style shell route.

The row's older share/import/tree notes were stale: catalogue sharing, the window-wide overlay/import door, the asset tree, webcam tracker and Deeper windows already exist on this base. Existing rack/CRUD/export/session-drop behavior was retained.

## Verification limits

All required builds completed with .NET 10 (final SDK 10.0.402). Existing warnings remain; every final build reported `0 Error(s)`.

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build <project> -c Release` for Core, Avalonia and all 12 Linux test projects below | 0 each | `0 Error(s)` |
| `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | `554 Warning(s), 0 Error(s)` |
| `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release` | 0 | `Passed: 3545, Skipped: 5, Failed: 0, Total: 3550` |
| `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~SessionShortcutTests.CameraShortcutUsesWpfNamesAndDefaults` | 0 | `Passed: 3, Skipped: 0, Failed: 0` |
| `bash .github/scripts/core-guards.sh` | 0 | `All Core guards passed.` |
| `git diff --check` and staged equivalent | 0 | clean |

Build project results:

```text
CCP.Core/CCP.Core.csproj: exit 0
CCP.Avalonia/CCP.Avalonia.csproj: exit 0
Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj: exit 0
Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj: exit 0
Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj: exit 0
Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj: exit 0
Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj: exit 0
Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj: exit 0
Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj: exit 0
Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj: exit 0
Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj: exit 0
Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj: exit 0
Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj: exit 0
Tests/CCP.Core.Tests/CCP.Core.Tests.csproj: exit 0
Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj: exit 0
```

The original snapshot was clean but at a different commit. Fetch failed on the read-only checkout (exit 255); the authorized fallback clone checked out the exact required base. Initial validation exhausted the build volume because each test copied the application's assets; removing only generated output and building sequentially resolved it. Final checks above passed after recovery.

- UI-dependent WindowDropTests, SessionIoTests, SessionShortcutTests registration/rebind test, WorkshopCellsTests and PanicSurfacesTests are compiled but not run: this cloud lane has the documented font-creation limitation. No tests were weakened or skipped to conceal a failure; the old library-not-ready assertion was replaced by real import/deduplication assertions because that behavior changed.
- Windows RegisterHotKey is compiled on Linux, not executed. Non-Windows global registration deliberately falls back to in-window shortcuts.
- The full Vosk speech model is unavailable (download blocked); Core's five existing model-dependent tests skip themselves. No Vosk download workaround was attempted.
- Full media Play remains dependent on the unported EnhancementAudioPlayer and EnhancementHostService playback-time binding in the Deeper player. This unit exposes the existing player without claiming that dependency is complete.
- No render-all or live drag-and-drop check was run in this font-limited lane; UI regression and before/after failure proofs remain for the supervisor's GitHub runner.

## Assumptions

- Keep the unit `stub` while a real Play choice can reach an incomplete Deeper player. Do not claim `wired` merely because the choice opens a window.
- Preserve WPF Windows hotkey behavior behind a small OS seam; retain local shortcuts on other desktops rather than add a second portal/X11 subsystem to this unit.
- Reuse the current player/editor and Core library APIs; keep the session rack and CRUD implementation already on the base.

## Changed files

```text
CCP.Avalonia/Platform/Win32SessionHotkey.cs
CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.axaml.cs
CCP.Avalonia/Views/Controls/AppSettings/DevicesSettingsSection.axaml.cs
CCP.Avalonia/Views/Controls/Companion/Runtime/WorkshopBehaviorCell.axaml.cs
CCP.Avalonia/Views/Controls/Companion/WorkshopAccordion.axaml.cs
CCP.Avalonia/Views/Dialogs/MediaDropDialog.cs
CCP.Avalonia/Views/Tabs/BlinkTrainerTabView.axaml.cs
CCP.Avalonia/Views/Tabs/DeeperTabView.axaml
CCP.Avalonia/Views/Tabs/DeeperTabView.axaml.cs
CCP.Avalonia/Views/Windows/LauncherWindow.axaml.cs
CCP.Avalonia/Views/Windows/MainShellWindow.DeeperHub.cs
CCP.Avalonia/Views/Windows/MainShellWindow.DeeperTab.cs
CCP.Avalonia/Views/Windows/MainShellWindow.EnhancementImport.cs
CCP.Avalonia/Views/Windows/MainShellWindow.SessionIO.cs
CCP.Avalonia/Views/Windows/MainShellWindow.SessionShortcuts.cs
CCP.Avalonia/Views/Windows/MainShellWindow.axaml.cs
CCP.Avalonia/Views/Windows/PanicSurfaces.cs
Tests/CCP.Avalonia.Tests/SessionShortcutTests.cs
Tests/CCP.Avalonia.Tests/WindowDropTests.cs
docs/avalonia-parity.md
docs/cloud/fin-shell-session-io.md
```
