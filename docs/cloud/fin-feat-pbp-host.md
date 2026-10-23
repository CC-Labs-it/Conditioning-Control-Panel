# fin-feat-pbp-host

- Reuse WPF's `HostNetPathRule` and `PbpNetRelay` in Core without changing their code. The Avalonia relay now judges the actual URI, refuses traversal before transport, handles trailing-slash bases, and omits the token on public watch routes. WPF continues calling the same moved helpers.
- Keep the foreground board's first Escape as `pbp:escape`, only after ready; a second inside two seconds remains full panic through the existing `games` surface. No new surface or panic registration is needed.
- Resolve the OS motion preference through the browser's `prefers-reduced-motion` query, apply app/gentle caps and WPF's gentle intro speed floor. Single-flight boot work preserves settings-before-identity ordering during asynchronous queries and drops frames after close.
- Carry and persist `letPeopleWatch`, `watchEffects`, and `watchAmount` through the existing Core typed whitelist. Add headless cases for persistence, invalid input, motion combinations, delayed queries, kept Escape and host net replies.
- Consolidate the parity row, remove stale missing-whisper claims, and retain `stub` with the remaining gaps below.
- Imports: none from trunk. Read-only `git fetch origin parity/trunk` succeeded; inspected tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Reused helpers come from the pinned base `73eb085671ce6576c4b3d6b3f6197bb720acb55f`.

## WPF audit and remaining gaps

Reference is the pinned base above, `ConditioningControlPanel/Services/PieceByPiece/PieceByPieceHostService.cs`, its `.Media.cs` and `.Friends.cs` partials, `Services/MotionFx.cs`, and `MainWindow/MainWindow.xaml.cs:1505`.

| WPF behavior missing at the start | Outcome |
| --- | --- |
| Parsed URI whitelist, exact outgoing URI, public watch routes without auth | Implemented using the unchanged WPF helpers; Core regression tests failed before the change. |
| OS animation cap, gentle visuals cap, gentle-only intro speed | Implemented; no answer defaults to animations enabled as WPF's failed OS read does. |
| Kept foreground Escape, ready guard, keyboard return, second-press panic | Frame and panic behavior implemented; `Web.Focus()` retains the existing head focus path. Native child keyboard focus is still a WebHost gap. |
| Watch preference boot fields and typed setting writes | Implemented and covered through real board messages and persisted settings. |
| PvP `stake-*` routing, bridge lifetime after window close, Chaster loss-row/time gate, wallet adoption | Missing: `feat-stakes`. Its Core bridge exists but the head's settlement/startup hooks and game routing remain unported. |
| Saved media choice, per-window opt-in, picker state/choice persistence, online pictures/clips, ratios, next-wave refill, disposal of temp copies | Missing: `feat-pbp-media-friends`; requires the board's online-media integration. |
| Local media-availability probe and updates, `mediaAvailable` settings, fresh `spectateMatchId` | Missing with media/friends integration; do not claim a complete spectate settings frame. |
| Friend challenge/accept, same-friend task reuse, timeout/replacement/close completion; Lobby host/join/watch; deferred intent after identity vs immediate live intent | Missing: `feat-pbp-media-friends`; real friends/Lobby callers must be connected together with the host protocol. |
| `MergedAccountRecovery.TryHandle` on net replies | Missing: `shell-login` recovery orchestration. Avalonia has not seeded Core's stop-sync, reauthenticate, reload and finished hooks. Merely calling the unseeded swap would not complete recovery. |
| SeasonRecap `TrackFeature(PieceByPiece)` | Missing: the head's SeasonRecapService feature mutation path; recording a launch in an unrelated memory log would not reproduce it. |
| Immediate browser process-failed close | Missing: native browser lifecycle seam in WebHost. The existing heartbeat closes a silent ready page; that is not an immediate process-failure callback. |
| Native window owner, independent board browser profile, autoplay policy, native web focus on launch/refocus/ready/kept Escape | Missing shared browser/window plumbing; GameWindow uses the common WebHost and its existing focus operation. |
| Shared WPF `PbpHostRules` net/media/watchdog delegation | Remains implementation debt. WPF reference orchestration is frozen in this unit; its existing pure URI/request helpers now live in Core. |

Whispers are already implemented on the base: own brain-drain clips, active-asset filtering, permitted bundled fallback and authenticated audio routes. The old retained paragraph incorrectly called them missing. Existing launcher/leash/free-access, settings/identity once, local reservoir/GIF split, presence, exit, boot-error latch/reset and watchdog behavior were retained. The once-per-window off-thread library snapshot remains the documented head choice versus WPF's per-request walk.

There are live WPF callers for these gaps; this is not a P122 dead-code exception or a pure dependency NOOP. The row remains `stub`. The unit implements the independent host gaps above; the remaining integrations are listed explicitly rather than advertised as wired.

## Verification limits

- SDK: .NET 10.0.402. Original snapshot HEAD differed from the requested commit and its fetch was read-only; the authorized fallback clone was verified clean at the exact pinned base before creating the implementation branch.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors.
- `dotnet build` for every Linux test project named in `.github/workflows/build.yml`: exit 0, zero errors: Core.Tests, Core.Settings.Tests, Core.Content.Tests; Avalonia.Tests, Avalonia.Consent.Tests, Avalonia.Language.Tests, Avalonia.CatalogueStartup.Tests, Avalonia.Migration.Tests, Avalonia.Scheduler.Tests, Avalonia.Report.Tests, Avalonia.GeneralSettings.Tests, Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 251 warnings, zero errors. Windows code compiled but was not executed on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- Before the relay fix: the nine new transport regressions failed (exit 1). After the fix: `dotnet test Tests/CCP.Core.Tests -c Release --filter 'FullyQualifiedName~PbpHostRulesTests|FullyQualifiedName~HostNetPathRuleTests'`: exit 0, 90 passed.
- Full `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: final exit 0, 3,611 passed, 5 existing Vosk skips. First full run had one failure in unchanged `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` (expected Loops, actual Clips); the isolated case on the pinned base and the full rerun both passed. No test was weakened or skipped to obtain this result.
- `DiffEngine_Disabled=true` was exported for .NET test runs. Vosk's five speech cases were skipped by the existing suite because the model is unavailable; no Vosk network workaround was attempted.
- Avalonia tests were compiled, not run, following this job's measured font-creation limitation; CI must execute `GameWindowTests` and `WebAssetServerTests`. Motion, Escape and preference cases therefore have compile coverage here, not local execution/fail-before proof. Live browser/OS motion, native keyboard focus and Windows desktop behavior were not run.
- `node --test --test-force-exit Assets/web/piecebypiece/smoke/bridge-carrier.test.js`: exit 0, 1 passed. Plain `--test` completed assertions but retained the existing heartbeat interval, so it was stopped and rerun with Node's runner exit option.
- Initial build/test retries hit the temporary volume's capacity. Generated test outputs were cleaned between projects; generated Windows binaries were placed on a larger temporary volume. These retries changed no source or test expectations.
- `git diff --check`: exit 0. 286 added lines versus the pinned base, below the 1,000-line cap.

## Assumptions

- The native browser's OS media query is the smallest cross-platform equivalent of WPF's Windows animation switch. A missing engine/query answer follows WPF's animations-enabled fallback.
- Keep the WPF reference orchestration intact; move only already-pure helpers unchanged.
- Do not substitute a no-op recovery/stakes/season hook for a complete user path. Preserve the honest missing list and `stub` status.
