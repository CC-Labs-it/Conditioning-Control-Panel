# fin-shell-file-dialogs

- Base verified at `932d6af1af6fad0f575306bfec14644b1da1610c`; clean snapshot, local runtime branch `work`, then unit branch `avalonia-port/cloud-fin-shell-file-dialogs`.
- Added the missing Library Content Packs tree over the already-seeded `ContentPackStore`: active installed packs only, WPF images/videos order, built-in display names with id fallback, file sorting, checked states, recursive selection, active counts and saved-preset counts. Refresh preserves a selected virtual folder and removes inactive packs.
- Image thumbnails decrypt to streams through the existing bounded thumbnail loader. Pack previews decrypt to an owned temporary file, use the existing MiniPlayer, and remove the file after player teardown. All MiniPlayer previews now close through the `asset-preview` panic registration.
- Restored the spiral file picker's WPF confirmation after save, gallery repaint and overlay refresh. Cancel leaves settings alone and shows no confirmation.
- Added focused tests for pack tree/counts/selections, thumbnail decoding, preview cleanup/panic, refresh/deactivation, preset counts, and spiral pick/cancel/confirmation order.
- Updated only the `shell-file-dialogs` ledger row, retaining `stub` with an explicit Missing list.
- Imports: none. Fetched and inspected read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk files replaced base files. Behavior was adapted directly from the WPF reference at the pinned base SHA above.

## WPF comparison

The row was `stub` on the required base. Its cited WPF paths were reviewed: `MainWindow.Assets.cs`, `MainWindow.SessionIO.cs`, `MainWindow.PresetIO.cs`, `Features/MonitorTargetPicker.xaml.cs`, `MainWindow.DeeperTab.cs` and `MainWindow.DeeperHub.cs`. The head paths also led to WPF `Features/SpiralFeatureControl.xaml.cs:508-550` and `Windows/MiniPlayerWindow.xaml.cs`.

| Behavior absent on the base | Result |
| --- | --- |
| Assets.cs:517-623 active Content Packs virtual tree and typed folders | Implemented in `AssetsTabView.Packs.cs`; existing Library door reaches it. |
| Assets.cs:727-819 pack folder selection, sorting and thumbnails | Implemented for images via decrypted streams. Video thumbnails remain missing. |
| Assets.cs:1099-1138 pack folder selection keys, parent propagation, Select/Deselect All | Implemented using Core's `SelectionKey`, keeping pack paths out of disk folder exclusions. |
| Assets.cs:1205-1260 pack extraction before owned preview | Implemented with per-preview cleanup and panic close. Exact foreground-aware owner reactivation remains missing. |
| Assets.cs:1472-1503 / 1983-2040 pack counts in current selection and saved presets | Implemented; typed leaf files counted once. |
| SpiralFeatureControl.xaml.cs:543-547 confirmation after selection | Implemented with the same title/message and order. |
| Assets.cs:856-868 Windows shell video thumbnails, including encrypted packs | Missing: no portable thumbnail service in this head; existing Fluent video placeholder remains. |
| Assets.cs:1194-1202 Explorer selects the actual file | Missing: portable opener currently opens the containing folder for disk media; pack media have no disk location before extraction. |
| SessionIO.cs:1467-1480 media Play route fully plays audio and drives enhancement rules | Still depends on the unported EnhancementAudioPlayer/AudioWaveformCache and enhancement host playback-time/rule binding. Existing `EnhancementPlayerWindow.LoadAudio` only updates UI state. The editor already has `DeeperLocalAudio`, but the player still lacks its waveform/volume, webcam consent and enhancement-rule integration; substituting an ungated play call would not finish that behavior. Browser zoom and fullscreen signaling also remain incomplete. |

Session/preset/phrase import-export dialogs, folder picker and personal-folder refusal, monitor target selection, media Play/Edit/Add/Cancel routing, and enhancement import validation/dedup/copy/toasts/remembered folder/reveal already have callers on this base. They were preserved. Hidden pack-card administration, preview rotation and wheel-to-pan remain under `views-tab-assets`; they are not exposed by the file-picker work here. No P122 dead-code exception is claimed.

## Verification limits

All commands used .NET SDK `10.0.402`, Release configuration, and `DiffEngine_Disabled=true` for test execution.

- `bash .github/scripts/core-guards.sh`: exit 0; “All Core guards passed” (424 XAML references checked).
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0; **2,811 passed, 0 failed, 5 skipped, 2,816 total**. Existing Vosk tests skipped because the speech model is unavailable; tests were not changed or weakened.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~ContentPacksWireTests'`: exit 1; **7 passed, 1 failed**. The failing existing `EachPanicRouteCallsStopAllExactlyOnce` test constructs the shell and hits the same font error before assertions. All four Content Pack wiring tests and the other three panic registry/order tests passed.
- The final `AssetsTabTests` UI regressions, `WindowDropTests`, `DeeperImportTests` and MiniPlayer UI tests were not run after the font failure. Their project is compile-checked; supervisor execution is needed.
- `dotnet build <project> -c Release`: every required project compiled with **0 errors**. The final headless/consent builds used `-m:1 -p:UseSharedCompilation=false`. The WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.

| Build project | Final exit | Result tail |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | 0 Warning(s); 0 Error(s); Time Elapsed 00:00:05.08 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | 0 Warning(s); 0 Error(s); Time Elapsed 00:00:13.65 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | 1 Warning(s); 0 Error(s); Time Elapsed 00:00:18.16 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | 0 Warning(s); 0 Error(s); Time Elapsed 00:00:11.03 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | 0 Warning(s); 0 Error(s); Time Elapsed 00:00:39.28 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | 0 Warning(s); 0 Error(s); Time Elapsed 00:01:07.23 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | 173 Warning(s); 0 Error(s); Time Elapsed 00:02:15.37 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | 0 Warning(s); 0 Error(s); Time Elapsed 00:00:06.87 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | 0 Warning(s); 0 Error(s); Time Elapsed 00:00:16.03 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | 223 Warning(s); 0 Error(s); Time Elapsed 00:01:10.05 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | 10 Warning(s); 0 Error(s); Time Elapsed 00:00:28.88 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | 0 Warning(s); 0 Error(s); Time Elapsed 00:00:03.30 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | 118 Warning(s); 0 Error(s); Time Elapsed 00:00:09.86 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 | 512 Warning(s); 0 Error(s); Time Elapsed 00:04:51.05 |

The first Consent build failed with MSB4166 (child build node exited); its single-worker retry passed. The final headless build also recompiled the final Avalonia implementation. No tests were altered to bypass the environment failures.
- `git diff --check`: exit 0.

The pre-fix pack regression probe compiled, then failed during `MainShellWindow` construction with `InvalidOperationException: Could not create glyphTypeface`. It never reached the pack assertions, so this is **not** claimed as a behavior fail-proof. The cloud font limitation prevents meaningful UI execution here; final focused UI tests are compile-verified for supervisor execution, not reported as passing.

Native GTK/portal dialogs, file-manager selection, desktop foreground behavior, LibVLC media playback and browser video behavior were not exercised. Windows-specific code can only be compile-checked here; no WPF/Windows runtime tests were executed. No Vosk model download was attempted because the job reports that destination blocked.

## Assumptions

- Preserve the base's seeded Core services and existing window types; add no new service abstraction or native playback pipeline in a shell file-dialog unit.
- Match WPF's built-in pack display-name lookup with id fallback, retain its image-before-video order, and use the existing Fluent folder icon with the plain title “Content Packs”.
- Pack grouping nodes retain WPF display counts but derive check state from typed leaf counts, so bulk selection followed by individual deselection cannot leave an empty group checked.
- Do not mark the row wired while known reachable behavior is still incomplete. This is not a pure dependency NOOP: the base also lacked the independently implementable pack browser and spiral confirmation.
