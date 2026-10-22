# game-dtrh-host-c: heartbeat recovery and crash diagnostics

- Added the WPF heartbeat ladder to the Avalonia DTRH host: poll every 5 s, recover only after ready and strictly beyond 10 s in a descent or 20 s in the hub, consume heartbeat frames, relaunch once per application session, then give up cleanly. Deadline decisions live in Core `DtrhHeartbeatRule`.
- Panic/ordinary closure cancels pending recovery, including panic re-entering from `Closed`; closed carriers cannot start runs or erase crash evidence. A failed relaunch is logged instead of escaping the dispatcher.
- Reused the existing Core `ChaosCrashSentinel`: run-started marks it, a clean page booking clears it, teardown/watchdog booking preserves it, desktop startup consumes/reports it once, and clean app shutdown closes game timers before clearing it.
- Added carrier-driven headless regressions and Core deadline cases. The GameWindow test bootstrap now uses the production `Program.BuildAvaloniaApp()` so bundled Inter and AppFonts are registered; existing assertions and test cases are preserved.
- WPF behavior reference: base `avalonia-port/goongame-core-d` at `27850c70070e1078f7a8c0bc2010d35de1bcd284`, `DtrhHostService` heartbeat/recovery/run bookkeeping and `App.xaml.cs` sentinel lifecycle. No WPF source changes or trunk code imports. Read-only `parity/trunk` was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.

## Verification limits

- Environment: .NET SDK 10.0.402, Linux; `DiffEngine_Disabled=true`, `DOTNET_PROCESSOR_COUNT=2`, MSBuild `-m:1`. Test assemblies use their existing `CCP_USERDATA_DIR` sandboxes.
- Fail-before proof: `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dtrh_CrashSentinel_CleanEndClearsButCloseBankPreserves -m:1` failed at `Assert.True(File.Exists(path))` immediately after the carrier delivered run-started (1 failed / 0 passed). Initial bare-builder attempts failed earlier on missing font registration; the production builder corrected that setup before this behavioral proof.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release -p:BuildProjectReferences=false -m:1`: **3108 passed, 2 failed, 5 skipped**, total 3115. The unchanged `BoardTilesTests.A_frame_fits_the_budget` exceeded its budget (19.84 ms); unchanged `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` expected Loops but observed Clips. Targeted recheck of those two plus the seven new heartbeat cases: 8 passed / 1 failed; the remote-media case passed, board performance still failed (27.30 ms). These failures were not hidden or fixed outside this unit. Five existing Vosk tests skipped because no model was installed; no test was edited to skip.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~DtrhHeartbeatRuleTests -m:1`: **7 passed, 0 failed, 0 skipped**.
- `bash .github/scripts/core-guards.sh`: **All Core guards passed**, 429 XAML references checked. `git diff --check` passed.

- Final head run: `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false --filter FullyQualifiedName~GameWindowTests.Dtrh_ -m:1`: **8 passed, 0 failed, 0 skipped** (2 m 56 s), against the completed final Avalonia build. This includes the existing lifecycle/online-media/Escape cases and the new regressions:
  - `Dtrh_CrashSentinel_CleanEndClearsButCloseBankPreserves`
  - `Dtrh_Heartbeat_RelaunchesOnceAndRetainsCrashEvidence`
  - `Dtrh_Heartbeat_BeatsRefreshDeadline_ExitAndQueuedPanicCannotRelaunch`
  - `Dtrh_Heartbeat_PanicDuringRecoveryCloseCannotRelaunch`
  - `Dtrh_Heartbeat_FailedRelaunchDoesNotEscapeTheDispatcher`

- `dotnet build <project> -c Release -m:1 -v:q`: **0 errors** for CCP.Core, CCP.Avalonia, and every Linux test project listed in `.github/workflows/build.yml`:
  - `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`
  - `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`
  - `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`
  - `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`
  - `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`
  - `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`
  - `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`
  - `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`
  - `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`
  - `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`
  - `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`
- GeneralSettings and Language first failed copying assets when the shared disk filled (MSB3021/MSB3027). After removing completed generated output and pausing concurrent copies, both were rebuilt successfully; remaining builds used `-p:CreateHardLinksForAdditionalFilesIfPossible=true -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true`. These flags only change artifact copying. Existing compiler/analyzer/XAML warnings remain.

- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -v:q -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (with the same four hardlink flags): **build succeeded, 564 warnings, 0 errors**, 5 m 49 s. WPF and its tests were compiled on Linux, not run.
- Not run: native WebKitGTK/WebView2 browser failures, real desktop launch/relaunch, Windows runtime, or the other test suites (their projects were compiled). No render was required: this slice changes lifecycle/recovery, not layout or fullscreen. The carrier exposes no process-failed event; silence after ready is covered, a native crash before ready is not.

## Remaining work: game-dtrh-host-d

`game-dtrh` remains **stub**. The following existing gaps remain: meta snapshot/meta-command/Warren shelves; ccp.art/ccp.spirals/ccp.content/ccp.mod origins; loom-list/loom-*; favorites/asset-stats; barks; sfx; fire-payload; freeze/mute; vn-speaking; haptics; fullscreen-set; report-bug; session stats; bubble credit; RevealService.Sync; EMI desk fires; audio-web pack request; tray tuck; SeasonRecap TrackFeature; Play-tab/Exclusives doors; kept-escape for race/board/Breakout; and any future carrier process-failed hook. Prior documented payout/escape deviations remain. No copy sweeper or Windows-only carrier work was included.
