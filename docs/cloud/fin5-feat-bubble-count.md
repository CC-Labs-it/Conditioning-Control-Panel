# fin5-feat-bubble-count

- Base verified: `560c0d0a10255b1d339121a96b3d3d80bb54c9d6`; local snapshot branch names were ignored.
- Added WPF For You deferral: one pending game, 500 ms polling, 90-second ceiling, no early Freeze, replay guards for engine/card state, and cancellation on stop/panic. The existing feed host supplies screen ownership.
- Mandatory video and lock cards now wait through Bubble Count's lead-in, clip, answer and strict retry; panic cancels pending requests. Reused the existing one-second polling convention.
- Added duration-aware stuck recovery: initial five-minute minimum, clip duration plus 150 seconds, retry renewal to 330 seconds. Cleanup closes all existing game/message surfaces without XP.
- Added WPF's localized empty-library toast, once per scheduler lifetime for scheduled rounds and every Test Now; inactive installed packs get their specific guidance.
- Added focused Core and headless host tests; replaced the duplicated ledger history with current behavior and honest remaining gaps, and added numbered tester steps.
- Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk implementation was imported; changes adapt the pinned WPF reference already on the base.

## WPF gap audit

Reference: `ConditioningControlPanel/Features/BubbleCountFeatureControl.xaml` and `.xaml.cs`, plus `Services/BubbleCountService.cs` and `Services/UI/InteractionQueueService.cs`, all at the pinned base above.

The row's old pack-clips gap is already closed: `BubbleCountHost` creates a `PackMediaPool(VideoType)`, and `BubbleCountScheduler.PickNext` draws from it with local fallback. No replacement content-pack service was needed. Scheduling, Bambi Freeze, live controls, strict retry/mercy and progression were already present and are retained.

This unit closes the old For You, reverse video/lock-card queueing, and Bubble Count timeout gaps. It also closes the newer WPF empty-library warning gap (#1449). Existing engine and lock-card panic registrations cancel the new timers; no new window surface was introduced.

Remaining behavior, so the row stays **stub**:

- WPF #1418's Pause other effects checkbox and full-round hold: flash suppression/restoration, bouncing-text fullscreen holds, and placing spiral/filter/brain-drain/corner-GIF/compositor surfaces below both game and answer windows. The Avalonia head lacks the matching fullscreen overlay ordering/reconciliation host. A checkbox alone would promise behavior it cannot provide. Porting that host across all those surfaces, together with the browser pipeline below, exceeds this unit's 1000-added-line budget; ambient bubbles still pause as before.
- WPF browser-routed Bubble Count video and shared-native-player poison cooldown/skip handling. These depend on the browser video host and native poison recovery service, beyond this unit's portable scheduler and existing LibVLC window. This unit preserves the existing no-LibVLC skip rather than turning missing playback into a strict loss.
- A single global FIFO interaction queue across all interaction types. This unit follows existing per-feature polling, so handover may lag one second and it does not promise FIFO order between simultaneous video/lock-card/quiz requests.
- For You ghost mode is not implemented by the feed host. Its current live windows always own the screen; the Core seam can distinguish ghost mode when that host gains it.

## Assumptions

- Preserve the existing one-second polling handover instead of importing a second global queue alongside other lanes' arbitration.
- A live For You window owns the screen, matching this head's current absence of ghost mode.
- Keep `stub`: partial parity and compiled tests are not proof of every WPF behavior.

## Verification limits

Final checks used .NET SDK `10.0.402`, Release configuration and `DiffEngine_Disabled=true`. No real desktop, personal profile, or Windows runtime was used. WPF and Win32 behavior cannot be run on this Linux worker. Live multi-monitor/media timing still requires desktop verification.

### Build results

Every final Linux build below used `dotnet build <project> -c Release --no-restore` and exited 0 with zero errors:

- `CCP.Core/CCP.Core.csproj`
- `CCP.Avalonia/CCP.Avalonia.csproj`
- `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`
- `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`
- `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`
- `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`
- `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj`
- `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`
- `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`
- `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`
- `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`
- `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`
- `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`
- `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`

- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors (existing warnings). WPF/Win32 code was compiled, not run.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore --filter FullyQualifiedName~BubbleCountSchedulerTests`: exit 0, 15 passed, zero failed/skipped.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore`: exit 0, 4,299 passed, five existing Vosk tests skipped, zero failed, total 4,304. Vosk model availability remains an environment limit; no tests were weakened or newly skipped.
- Failure proof: the first four new scheduler regressions failed against the unchanged implementation; reverse mandatory-video queueing then failed before its fix. Intermediate full runs during concurrent compilation hit the existing board frame-time budget (17.72 ms and 12.24 ms); the final full run after builds completed passed unchanged.
- `CCP.Avalonia.Tests` compiled with zero errors. Its relevant `BubbleCountQueueTests` / `BubbleCountGameTests` were not run, per the supplied cloud font limitation; supervisor headless execution is still required. No real desktop or user profile was touched.
- `git diff --check`: exit 0. The base has no `.github/scripts/test-plan-coverage.sh`; numbered WPF/Rows coverage was added directly to the test plan.
