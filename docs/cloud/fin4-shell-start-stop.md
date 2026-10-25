# fin4-shell-start-stop

- Verified base `05caa12f51a8a5cd166cda7d5b0b7edf512a9c14`; snapshot branch was `work`. Unit branch: `avalonia-port/cloud-fin4-shell-start-stop`.
- Ported Start charge, mod colours, exhale, ignition and heartbeat using existing ring/button surfaces and shared `BeatLoop`. No infinite Avalonia Animation, Effect or new window. Motion/focus/minimize/remote gates park ambient work.
- Audio binders now observe master-volume publications and restore, rebind settings replacement, and unsubscribe on detach. Layout-free regression fails against original binder (23 expected, 37 actual), passes with fix.
- Additional stale Core comment found: Avalonia already has `Platform/LayeredAudio.cs`. Reused it for Audio Only Start (ignores standalone toggle) and WPF Stop's preserve-standalone rule. Stop releases ducking. Fake-player test covers bed lifecycle.
- Added numbered tester steps; retained ledger **stub** with explicit missing dependencies.
- Fetched `parity/trunk` read-only; imported no trunk code, so no imported commit credits.

## WPF gap audit

Reference pinned to base SHA above: `ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs` and `MainWindow.HeroFx.cs`. Four named base gaps:

1. Brain Drain engine start/stop: still missing. Live WPF calls Start and unconditional Stop; P122 does not apply. `Services/LockCard/BrainDrainService.cs:215` starts when enabled (not a dead rework flag). Requires clip scanning/trigger/audio arbitration and capture/blur/overlay ownership. Reference service: 496 lines; `Services/Compositor/BrainDrainCapturePump.cs`: 771; `BrainDrainLayer.cs`: 432. No Avalonia host. Complete adapted host plus tests exceeds remaining line allowance.
2. Start hero FX: implemented with WPF order, timing, grow factors, easing and hue rotations. Existing decorative sibling rings/button transform reused; shared capped beat replaces WPF ambient clocks and finite ignition tracks (ponytail shortcut).
3. Wallpaper warning: still missing with its host. Avalonia `BambiTakeoverTabView.ChkWallpaperKeep_Changed` only persists; no WallpaperService activation/deactivation. WPF Win32 `Services/Video/WallpaperService.cs` is 644 lines before adaptation, OS seam and tests. A notice alone would advertise an absent wallpaper feature. These hosts plus this unit's visible fixes cannot fit 1000 added lines.
4. Ramp master repaint: fixed by observing shared settings, including replacement/detachment.

Additional differences found rereading StartStop: Audio Only bed and ForceUnduck now wired through existing hosts. Still missing: mandatory-video enhancement prompt (unported match/webcam host); engine crash sentinel and EMI Desk engine-start/stop publication; broad interaction/fullscreen teardown for unported browser/enhancement hosts. These are not claimed wired. Existing flash/video/quiz/bubble/count/mind-wipe lifecycle, session dialog, lockdown, scheduler and ramp math retained. Scheduler settings-change method remains dead in WPF, already documented by base; no P122 promotion/render-all claim.

## Assumptions

- Favor pinned WPF behavior and keep the row stub while live service dependencies remain.
- Existing shell decoration introduces no new panic surface. Existing engine Stop/panic and window closure own lifecycle; finite ignition ends within 830ms.
- Full Brain Drain and wallpaper hosts cannot fit the remaining cap with faithful tests; do not substitute fake effects or a notice alone.

## Verification limits

- SDK: .NET 10.0.402 (installed with dotnet-install; initial version metadata URL returned 403, SDK download succeeded).
- `dotnet build <project> -c Release`: exit 0 / 0 errors for all 14 projects below. The WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Existing warnings retained.


  - `CCP.Core/CCP.Core.csproj`
  - `CCP.Avalonia/CCP.Avalonia.csproj`
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
  - `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj`

- Final `dotnet build Tests/CCP.Avalonia.Tests -c Release` after FX safety cleanup: exit 0, 0 errors.
- `bash .github/scripts/core-guards.sh`: exit 0, **All Core guards passed**.
- `export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; **2829 passed, 5 skipped, 2834 total**. Existing Vosk tests skip without the unavailable model; none were weakened or removed.
- Original binder + new layout-free regression: exit 1; **expected 23, actual 37**. Restored fixed binder afterward.
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~ExternalMasterVolumeWritesRepaintWithoutLayoutOrAudioSideEffects|FullyQualifiedName~StartHeroTests|FullyQualifiedName~EngineAudioOnlyTests|FullyQualifiedName~StartStopIntegrationTests'`: exit 0; **4 passed, 0 failed**. Production app/font setup initializes this selection.
- Broader filter (`AudioSettingsTests`, `StartHeroTests`, `EngineAudioOnlyTests`, `StartStopIntegrationTests`, `LayeredAudioTests`, `EngineEmptyLibraryTests`): exit 1; **3 passed, 6 failed**. All six fail before assertions with the known **Could not create glyphTypeface: Segoe UI Black** shell construction error when existing tests initialize Avalonia without production font configuration. Initial full audio regression attempt hit the same error. Narrower successful selection is reported separately, not presented as a green broader suite.
- `git diff --check`: exit 0. `test-plan-coverage.sh` does not exist on this base; numbered coverage was added for future checking.
- Not run: Windows WPF runtime tests, native desktop/audio-output checks, full Avalonia suite or manual/render-all proof. No new OS-specific implementation. All tests opening windows use the existing sandbox profile; audio-only coverage uses fake players/mixer.
