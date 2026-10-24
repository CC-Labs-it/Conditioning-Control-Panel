# sync7-port-engine-bubble-hold

- Ported the available parts of WPF `c52e7f6d8eac017c5dc35b343992b7fa7a66343c`: the Bubble Count option, per-round capture, Flash schedule hold/restart rules, bouncing-text video pause, and session name/clock above START/STOP.
- The hold spans the 800 ms lead-in, clip, answer and strict retries. A settings edit affects the next round. Force cleanup never restarts flashes; normal completion requires flashes previously running, engine still running and Flash enabled.
- Session status uses the existing session tick, refreshes immediately on pause/resume, and writes no settings. Existing Core formatting handles countdown-off, elapsed open-ended sessions and name truncation.
- No parity/trunk files imported. Fetched parity/trunk read-only as instructed; implementation adapts only the listed WPF commits to existing Core/head code. No sibling feature commits re-ported.
- No new surface: existing Bubble Count, result and video windows retain their existing panic lifecycle. Sync ledgers and avalonia-parity.md are unchanged.

## Commit disposition

| WPF commit | Status and evidence |
| --- | --- |
| `33ae795c7eae660e37a5ac07be922159e75f7729` | **blocked-by-host**: Avalonia has neither Natasha's red ambient bubble nor the WPF shared click-through bubble host / hook-disc snapshot, native press catcher or per-flash low-level mouse hook. `CCP.Avalonia/Views/Overlays/BubbleOverlay.cs:34` documents the absent red bubble; the existing head uses input regions and routed pointer events. A disconnected catcher would do nothing, so none was added. |
| `c52e7f6d8eac017c5dc35b343992b7fa7a66343c` | **ported in available hosts; overlay pinning blocked-by-host**. `CCP.Core/Services/BubbleCountScheduler.cs:170` captures and enters the hold; `:261` releases it. `CCP.Avalonia/Views/Windows/BubbleCountHost.cs:42` applies the text hold without releasing an active mandatory video's pause. The video close path also respects a concurrent Bubble Count hold. The option is in `Views/Features/BubbleCountFeatureControl.axaml` and its code-behind. `CCP.Avalonia/Views/Windows/MainShellWindow.SessionRun.cs:172` renders the bar and `MainShellWindow.StartStop.cs:122` refreshes bare-run/idle status. Core settings, localization and `CCP.Core/Services/Session/SessionClockLabel.cs:64` were already merged. Tests: `BubbleCountSchedulerTests.Effects_hold_captures_option_and_force_cleanup_never_restarts_flashes`, `Strict_retry_keeps_hold_until_completion_and_disabled_flashes_stay_off`, `BubbleCountQueueTests.PauseEffectsCardReadsAndWritesTheCoreOption`, `SessionRunWireTests.StartLocksAndLabelsTrayStopPausesResumeRestartsStopOpensTheRecap`. Missing surface: WPF OverlayService's video-anchor reconciler / compositor hosts that pin spiral, pink filter, blur and corner GIFs below game/answer windows. The head's platform helpers do not supply that reconciler (RestackAbove has no Windows half). No substitute overlay architecture invented. |
| `1feb49131f7c4955d52e44c72ffde724ac18076a` | **already-covered in Core; head blocked-by-host**. `CCP.Core/Services/Chaos/EffectPayload.cs:103` already returns `settings.SimultaneousImages` for Ambient or UseFlashSettings, matching the revert exactly. No production change made. The head has no effect-payload runner: `CCP.Avalonia/App.axaml.cs:457` installs a logging placeholder. Full-burst execution on bubble pop awaits that host. |

## Verification limits

- SDK: .NET 10.0.100. Channel install failed discovery with HTTP 403; pinned 10.0.100 installed successfully.
- Focused Core command: `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~BubbleCountSchedulerTests`: exit 0, 12 passed.
- Regression proof: temporarily replaced the production option capture with false, ran `--filter FullyQualifiedName~Effects_hold_captures_option`: exit 1, 2 failed / 1 passed. Restored the production line, then ran the full Core suite.
- Full Core suite: exit 1, 3956 passed / 1 failed / 5 skipped. Failure: `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` expects Loops but receives Clips. Vosk tests self-skip without the unavailable model; no tests modified to skip.
- Avalonia tests compiled with zero errors. Execution deferred under the supplied cloud font-creation limitation; the new UI tests and clock assertions need the supervisor's headless runner.
- Core guards: exit 0, all passed. An initial read-only sandbox invocation could not create temporary files; rerun with writable execution passed.
- `test-plan-coverage.sh` is absent on this base. Added numbered manual checks to `docs/avalonia-test-plan.md`; no ledger rows added or promoted.
- `dotnet build <project> -c Release`: all 14 required Linux builds exited 0 with zero errors (Core, Avalonia and every Linux test project named in build.yml).
- WPF test project: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings / 0 errors. Compiled only; Windows tests/native input cannot run here.
- Baseline investigation: the unchanged expected commit in a detached worktree passed both the isolated failing DtRH test (1 passed) and the full suite (3953 passed / 5 skipped). This does not establish that the first branch-run failure is pre-existing; a branch-suite repeat is recorded below.
- Final focused Core repeat: exit 0, 12 passed.
- Full branch-suite repeat (`DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`): exit 0, 3957 passed / 0 failed / 5 skipped, 37 s. The earlier DtRH failure was not reproduced; its intermittent behavior remains a verification risk.
- No desktop, notification or native input tests ran.

## Assumptions

- Keep missing-host work explicitly blocked, as requested, and port the independent available pieces of c52e7f6d8.
- Reuse the existing Flash scheduler stop/start semantics: active bursts finish naturally when the schedule is stopped.
- Preserve WPF copy for the option, including its overlay wording; complete overlay pinning still requires the missing reconciler.
