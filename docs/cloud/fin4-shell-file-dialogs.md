# fin4-shell-file-dialogs

- Base verified at `7ad17375fcd981162262cc0441669045c505d396`; the initial snapshot differed and was read-only, so work used the prescribed independent fallback clone. No local requester changes were available.
- Windows Library thumbnails now use the same Shell image factory and thumbnail/icon fallback as WPF, for both local and pack videos. Pack extraction is deleted after decode, native bitmap/DC/COM resources are released, and only a copied Avalonia Bitmap crosses to the UI thread. Existing four-worker throttling is reused.
- Library file reveal selects the exact Windows file using WPF's `/select,` quoting. A deleted file falls back to its existing parent; missing parents do nothing. Explorer launches are blocked in test/render sandboxes. Other platforms retain portal-aware folder opening.
- Preview close checks Windows foreground process ownership before activating its owner, matching WPF's no-foreground/same-process/other-process decisions. Non-Windows conservatively activates only an already-active owner. Existing `asset-preview` panic registration and pack-preview cleanup remain in use; no new surface was introduced.
- Tests cover local/pack video dispatch, pack-temp lifetime, Explorer quoting, missing-file fallback and foreground policy. Manual tester coverage is appended in `docs/avalonia-test-plan.md`.
- No trunk code imported. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; adaptations are from WPF at the pinned base, not an overwrite from trunk.

## WPF gap audit and scope

The single `shell-file-dialogs` row remains **stub**, never verified. At the pinned base its existing picker/import/pack behaviors were already reachable. WPF references reviewed: `MainWindow.Assets.cs` (folder tree, thumbnails, context menu, preview close), `MainWindow.SessionIO.cs` (session save/export), `MainWindow.PresetIO.cs` (preset/phrase export/import), `Features/MonitorTargetPicker.xaml.cs` (monitor mapping), `MainWindow.DeeperTab.cs` (multi-select enhancement import), and `MainWindow.DeeperHub.cs` (library filtering/host entry). The outstanding list from that row was:

1. Local and pack Windows-shell video thumbnails: implemented, including thumbnail-only then icon fallback and temporary extraction cleanup. Cross-platform codecs without Windows Shell keep the placeholder.
2. Exact file reveal: implemented on Windows; portable folder fallback retained elsewhere. Pack context-menu reveal still follows WPF's virtual-path behavior rather than extracting another persistent copy.
3. Foreground-aware preview owner activation: implemented on Windows. Portable foreground process detection remains absent; conservative non-Windows behavior is documented in the ledger.
4. Deeper native audio, waveform peaks/cache, playback time source and rule binding: still missing. `EnhancementAudioPlayer`, `AudioWaveformCache` and `EnhancementHostService` exist only in WPF (208 + 158 + 309 lines). `EnhancementHostService.Bind` additionally depends on the unported 1,261-line `EnhancementEngine`, time sources and device/effect integration. The Avalonia player explicitly documents these dependencies at its LoadAudio/Bind paths; simply selecting a file cannot supply them. A correct shared engine/audio adapter plus player/editor lifecycle and behavior tests cannot fit the 1,000-added-line unit alongside the Library work.
5. Deeper browser zoom and fullscreen: still missing. The Avalonia player documents that NativeWebView lacks WPF's ZoomFactor, document-created script injection and fullscreen-element signal. CSS zoom is not equivalent to browser zoom, and window fullscreen alone cannot follow HTML5 video fullscreen. These require a browser capability port, not a file-picker substitution.

No P122 dead-caller exception applies: WPF reaches the Library handlers and Deeper services. User-visible independent Library gaps were prioritized; dependency gaps are honestly retained rather than marking the row wired.

## Verification limits

Verification results are recorded below after the final checks. Windows native code is compiled on Linux, never executed here. Native GTK/portal dialogs, media codecs, focus transitions and shell thumbnails require desktop testing. Window-based AssetsTabTests remain compile-only per the supplied font-creation environment limitation; no pre-fix behavioral assertion result is claimed for those tests. No tests were skipped, deleted or weakened.

Assumptions: exact Explorer selection and foreground ownership are Windows-specific WPF requirements; other platforms retain their existing folder launcher and avoid stealing focus. The Deeper engine and browser capability ports need a separate unit. No new abstraction, setting, icon or panic surface is needed.

### Commands and results

- `.NET SDK 10.0.402` installed using the requested channel installer (the initial `ci.dot.net` lookup returned 403, then the installer succeeded). A pinned 10.0.100 install also succeeded.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, zero errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors.
- `dotnet build <test-project> -c Release -m:1 -nodeReuse:false -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true`: exit 0 for all Linux workflow test projects: CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests, CCP.Avalonia.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests and CCP.Core.Tests. Each compiled with zero errors; existing warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0; tail `All Core guards passed.`
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0; tail `Failed: 0, Passed: 2811, Skipped: 5, Total: 2816`. Existing Vosk tests automatically skip without the unavailable model; no skip logic was changed.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter FullyQualifiedName~FileDialogPlatformTests`: exit 0; tail `Failed: 0, Passed: 7, Skipped: 0, Total: 7`.
- Tests exported `DiffEngine_Disabled=true`. Builds/tests used `DOTNET_PROCESSOR_COUNT=2` after resource failures. An initial overlapping build hit a PDB file lock; later attempts exhausted disk copying assets into test outputs and produced worker/runtime aborts. Serial builds, hard-linked outputs and removal of generated test binaries resolved these failures. No product/test requirements were relaxed.
- `git diff --cached --check`: exit 0. The base does not contain `.github/scripts/test-plan-coverage.sh`; tester coverage was added explicitly, but that script could not be run.
- AssetsTabTests were compiled, not executed, under the supplied font-creation limitation. Windows Shell COM/GDI, Explorer selection, real focus, portal pickers and native playback were not run. No render-all/P122 exemption is claimed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -nodeReuse:false -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: final exit 0; tail `2 Warning(s), 0 Error(s)`. The first attempt compiled the WPF sources but exhausted disk during asset copying; removing already-verified generated Avalonia outputs allowed the unchanged incremental build to finish. WPF tests and Windows native code were not executed on Linux.
