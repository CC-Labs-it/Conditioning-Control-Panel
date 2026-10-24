# fin-shell-video-playback

- Recreated on exactly `39260f198fad67d90bc988c63a00480830e5fefc` (`avalonia-port/main-20261024-compat`), replacing the earlier base. No push attempted, as requested.
- Preserved new-base behavior: toy-button attention and ReleaseToyBatches cleanup, the sorted panic safety list and its After ordering contracts, and compatibility Core seams.
- Mandatory videos wait for cascades and the visible For You feed in WPF order: 500 ms polls, one pending request per obstruction, 90 s ceiling, first-request path/strictness, enabled/running recheck and Stop/panic cancellation. Earned requests bypass the feed; only an explicit Test press gets WPF's localized 6 s toast.
- Mandatory teardown detaches blits and closes windows before stopping/disposing native players off-thread. Buffers stay rooted until native disposal succeeds; failed/hung release keeps them alive. Per-player watch credit and identity guards isolate late callbacks from replacement clips. Existing max-length carry and toy cleanup remain intact.
- HelpVideoWindow uses the existing muted looping InlineLoopVideo with Uniform fit, caption/link fail-soft behavior, singleton replacement and panic registration. Drawn loops still win; no tutorial assets invented.
- Consolidated the ledger to one row per feature id using the task's existing last-row-wins rule. Retained other ids' final rows verbatim; updated only the video/help content, both still stub. Superseded duplicate rows were removed, without promoting statuses.
- No code imported from parity/trunk (fetched tip a4ee52d40cd347d288dcf7fdaa8445b79f8e5038). Adapted existing head/Core files against this base's WPF VideoService, DualMonitorVideoService, InlineLoopVideo, MiniPlayerWindow and HelpVideoWindow.

## Remaining WPF gaps

Already present: local/pack shuffled picks, disabled/duration filters, jittered schedule and pre-roll, DND retry, screen targeting/secondary cap, volume/device/duck/fill, strict/grace keys, pointer/toy attention and verdicts, watch credit and max-length carry. MiniPlayer and inline playback already work.

Still missing: the 3 s off-thread video watchdog with 8/18/28 s rescue ladder and Windows escape; shared-instance retirement/rebuild, leases, four-retire circuit breaker and poison cooldown; remote queue/cache/browser fallback/no-videos offer; browser-media short retry and interaction queue; chaos ownership/random-segment, primary clock/seek/pause/external-mute and enhancement-driving hooks; global Escape routing; gaze attention/no-activate/restacking; floating mandatory presentation. Enhancement-player audio/waveform, webcam and fullscreen/audio transport remain under its own row. Existing inline popup reopen and ancestor visibility deviations remain. Nonblocking release provides memory safety, not fresh-instance recovery; a hung release retains its native resources.

This has live code changes and is not a dependency-only NOOP. These remaining gaps prevent a wired claim.

## Verification limits

- .NET 10.0.402; DiffEngine_Disabled=true for every test invocation.
- All 14 Linux builds from the prior run passed again (exit 0, zero errors): Core, Avalonia and the 12 Linux test projects in build.yml. Used Release and CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true, removing generated test outputs between projects to fit the disk.
- WPF app/test build with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false: exit 0, 557 warnings, zero errors. Windows behavior was compiled, never run.
- Full Core suite first run: exit 1, 3960 passed, 5 Vosk skips, one unchanged DtrhRemoteMediaTests refill/back-off assertion failed (Loops versus Clips). That test passed in isolation (exit 0); the complete rerun passed (exit 0, 3961 passed, 5 Vosk skips, zero failures). No change to that test or service.
- MandatoryVideoSchedulerTests: exit 0, 37 passed. The six cascade/feed regressions failed before the implementation was restored (0 passed, 6 failed, exit 1).
- Prior head filter (VideoNativeReleaseTests plus PanicSurfacesTests.EveryIdIsUnique and TheOrderingConstraints): exit 0, 4 passed.
- core-guards.sh: exit 0, all guards passed; 429 XAML references checked.
- Existing and added help/inline/mandatory/feed window tests compile with zero errors but were not run under the stated cloud font/LibVLC limits. No render-all, real playback/audio, webcam or Windows runtime check. Tests use isolated CCP_USERDATA_DIR profiles.
- Ledger validation: 422 unique ids, 1273 superseded rows removed; id set unchanged and every other selected final row retained verbatim. No existing test was weakened, deleted or disabled.
- The complete exported patch uses Git binary-delta encoding for the large ledger cleanup; source files remain normal text. Patch import is checked against the exact pinned base before delivery.

## Assumptions

- A visible feed owns the screen regardless of keyboard focus. Ghost mode is unavailable on this head.
- Trigger returns true for an accepted deferred request. Preserve WPF's bounded expiry policy and no backlog.
- Preserve the new base's toy attention and panic ordering rather than restoring older versions of those files.
- Last ledger row per id is authoritative, as specified by the task; global consolidation removes superseded rows without changing the selected content for other ids.
