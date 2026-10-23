- Wired the remaining supported shell lifecycle: manual-stop Relapse tracking and restart check, consent/entitlement-gated Takeover start, independent Takeover retention on Stop, Audio Only's forced layered bed, standalone audio retention, and forced unduck.
- Added the real two-second plain intensity ramp: captured bases, all six links/caps, restoration on Stop/panic, preset visual-ramp exclusion, and completion notification/stop.
- Added scheduler startup after the WPF 60-second grace, 30-second polls, overnight/day rules, tray hiding only when a tray exists, notifications, automatic stop, and manual-stop memory. Enabling it waits for the next poll, matching the live WPF caller.
- Wired the wallpaper arming warning (18 seconds, Media Manager and Get packs), using the existing library probe and sandboxed external opener.
- Wired Start charge, exhale, 100 ms press dip, 300 ms ignition wash, staggered 550/750 ms rings and 2.4-second heartbeat to existing controls. Ambient motion uses the shared BeatLoop; no infinite Avalonia Animation, Transform animation, Effect or new window was added. Motion, visibility, activation, remote and closed-window gates park it.
- Added time-of-day skill counters and the existing engine crash sentinel's start/clean-stop/startup-report path. Preserved this base's session dialog, remote gate, lockdown refusal, running label, empty-flash notice, TotalSessions and service starts.
- Imported only absent `CCP.Core/Helpers/IntensityRampRun.cs` and `SchedulerRun.cs`, from trunk commit `053445b900c371fcd5e88bfe7883acd05337070a`, inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Adapted its head integration into a new partial; existing base files were merged, never replaced by trunk versions.
- Added `EngineLifecycleRulesTests` and sandboxed `ShellStartStopLifecycleTests`. Timers stop through the existing `PanicSurfaces` engine registration and `CoreEngine.StoppedHook`; Takeover already has its own registration. No new independent panic surface exists.

## WPF gap audit

Reference pinned to base `ac612ae9a21cc55ad19ec400d0109b3465731dcd`, `ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs`, with `MainWindow.HeroFx.cs` and its cited service callers.

Before this change, the supported head lacked:

- `BtnStart_Click`: ignition; Relapse stop timestamp; scheduler manual-stop/start memory.
- `StartEngine`: Relapse check; time-of-day skill usage counters; Audio Only's layered bed; Brain Drain; Takeover; the plain ramp; crash sentinel; EMI engine-start event; mandatory-video enhancement prompt; system-initiated exclusions.
- `StopEngineCore`: ramp restoration; layered-bed stop/retention; Takeover stop/retention; forced unduck; sentinel clear; EMI stop event; Brain Drain and video-enhancement/interaction-queue teardown; embedded-browser/Deeper fullscreen teardown; WPF cache/LOH cleanup.
- Scheduler startup/poll/automatic stop/tray notifications and window/manual-stop state, and the empty-wallpaper arming notice.
- `UpdateStartButton` and remote state: charged gradient, exhale, heartbeat and motion/window-state lifecycle.

The implementable behaviors above are now reachable from the real Start/menu/session/scheduler and wallpaper checkbox paths. Remaining gaps keep the single ledger row **stub**:

- Brain Drain audio/capture/blur runtime (`BrainDrainFeatureControl` is still a settings surface). The new ramp writes its blur setting but cannot display the missing overlay.
- Mandatory-video enhancement prompt and `VideoEnhanceBridge.ForceUnbind` depend on the unported video-enhancement bridge; shared interaction-queue reset depends on `InteractionQueueService`. Existing local quiz/video cleanup remains intact.
- EMI `engineStarted`/`engineStopped` director events depend on the unported Fire/director half of EMI Desk; its window alone is insufficient.
- System-initiated start exclusions depend on the unported Lockdown Dose keeper. This head has no caller supplying that mode; normal user/scheduler starts retain their session count and Relapse behavior.
- Browser/Deeper fullscreen teardown depends on those unported fullscreen hosts. WPF's flash-cache release/LOH compaction has no corresponding cache API in this head; no speculative GC call was added.

`CheckSchedulerAfterSettingsChange` is dead in the pinned WPF: `MainWindow.Settings.cs` captures `schedulerWasEnabled = s.SchedulerEnabled` immediately before testing `s.SchedulerEnabled && !schedulerWasEnabled`. `rg -n CheckSchedulerAfterSettingsChange ConditioningControlPanel --glob '*.cs'` finds only that call and its declaration (plus explanatory rack comments). This is not a P122 claim for the whole row; render-all was not run.

## Verification limits

- SDK: .NET 10.0.402, installed with the requested channel-10 installer. Its preliminary ci.dot.net latest-version probe returned 403; the official builds download succeeded.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release -m:1`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release -m:1`: exit 0, 0 errors.
- Built every Linux test project named by `.github/workflows/build.yml`: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`: all exit 0, 0 errors. Existing compiler/analyzer warnings remain.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors. Windows/WPF code was compiled, not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. An initial read-only-sandbox attempt could not create temporary files; rerunning with filesystem writes allowed passed without changing guard baselines.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 2,825 passed, 0 failed, 5 existing Vosk-dependent skips (model download unavailable here). No tests were weakened, removed or newly skipped. The seven focused Core cases pass. An initial new range-ramp assertion expected zero flash opacity; it exposed the existing WPF-compatible 10% setter floor and was corrected to assert that floor.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~PanicSurfacesTests&FullyQualifiedName!~EachPanicRouteCallsStopAllExactlyOnce'`: exit 0; four font-free panic registration/order/source tests passed.
- Relevant test discovery (`--list-tests` with the five lifecycle/notice/tray/jump/panic class filters): exit 0, 19 cases found. `ShellStartStopLifecycleTests`, `EngineEmptyLibraryTests`, `ShellTrayTests`, `JumpRightInTests` and the window-opening panic test compiled but were not executed because the task explicitly identifies the cloud font-creation failure; CI must run those headless cases. Render-all was not run. Consequently no window-test fail-before/fix or visual parity claim is made.
- Live desktop, actual LibVLC playback/ducking, tray hosting, and Windows execution were not exercised. New tests use the existing isolated profile, fake audio players, and an OS-notification sink.

## Assumptions

- Match the live WPF scheduler poll behavior; do not revive its unreachable immediate-settings-change branch.
- Keep the shell visible when no tray host exists, using this head's established tray policy.
- Preserve WPF's wall-clock Relapse upper bound, including its clock-rollback behavior.
- Keep the ledger at stub until the named dependencies land; this branch implements the supported user-visible gaps within the 1,000-added-line cap.
