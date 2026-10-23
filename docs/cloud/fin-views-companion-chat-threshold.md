- Restore WPF's three thinking pulses: 450ms up/down, initial opacity 0.25, and child delays of 0/150/300ms. Each cloned WPF storyboard repeats its own delay; the port follows those individual cycle lengths.
- Reuse the existing shared 30fps `BeatLoop` (inherited trunk import `66ca21fd3`) instead of infinite Avalonia animations. Park hidden/minimized surfaces; reset dots to full opacity after thinking, room hiding, and unloading; release the brain session on unload and re-sync it on reload. Shrink the animation ratchet allowance.
- Restore dormant shimmer after an already-loaded view changes state: a 250ms wait followed by one 1.4s CubicEaseInOut sweep, width sampled once, with WPF's top-left transform origin.
- Add `ChatThresholdMotionTests` for dot timing, repeat delays, hidden-ancestor/minimized parking, stop/reset, detach/reload, and state-triggered shimmer timing/one-shot behavior. Add a focused `HerRoomChatMemoryTests` regression for detaching the live session while unloaded and projecting missed turns on reload. No new window or notification surface is introduced, so no additional panic registration is needed.
- Keep the ledger row `stub` with an explicit browser dependency list. The local motion gaps mean this is not a pure dependency NOOP.
- Reference checked at required base `cb8923d880b209496994524e6ba87063b7befe2c` and fetched read-only `parity/trunk` tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No new trunk files were imported or existing head files overwritten.

## Remaining WPF behavior and dependency evidence

The initial comparison found these gaps:

1. Thinking-dot stagger and independent repeat timing, visibility parking/room restart, opacity reset, and releasing/re-attaching the view's VM observation: implemented here.
2. Dormant shimmer triggered by a state change after load, its 250ms delay, and WPF's transform origin: implemented here.
3. Watch-chip browser-media ownership: still missing. WPF `Views/Controls/Companion/Runtime/CompanionLinkLauncher.cs:47` takes `MediaOwner.AvatarLink` with `takeover: true` before navigation; line 58 releases with `embedded-browser-unavailable` when embedded navigation fails.
4. Video autoplay/fullscreen: still missing. WPF `MainWindow/MainWindow.Browser.cs:435` arms parse-time autoplay, implemented by `ArmVideoAutoPlay` at line 776. Avalonia `Views/Windows/MainShellWindow.Browser.cs:170` accepts the flag but only logs it at line 190.

Items 3 and 4 depend on **wire-shell-browser-b-next**, which owns the BrowserMediaService/media reporter, parse-time autoplay hook, and companion caller. Searches of `CCP.Core` and `CCP.Avalonia` at the required base found no BrowserMediaService implementation or ArmVideoAutoPlay hook. The existing companion launcher explicitly defers ownership to the browser follow-up. Page fullscreen alone from wire-shell-browser-b will not supply either dependency. These WPF behaviors have live callers; P122 does not apply.

Already reachable behavior was retained: state ladder and localized copy; last-three conversation turns and bark echoes with provenance-aware AI badges; last-heard; brain send, trim/blank/single-flight and kill-switch checks; refusal/fallback tube bubbles; session/provider synchronization; Enter/Shift+Enter; newest-line scrolling; locked teaser; open-full-chat/history/unlock/Engine Room; assistant-only sanctioned-title chips; remote-controller guard; embedded preference/HTTPS fallback; native wheel chaining. Existing Core projection and navigation rules remain shared.

## Verification limits

- SDK: .NET 10.0.402. Channel discovery encountered an unavailable CI feed; explicit stable-version installation found the SDK already installed.
- `dotnet build ... -c Release`: CCP.Core, CCP.Avalonia, and all 11 Linux test projects named by `.github/workflows/build.yml` passed, exit 0, 0 errors. The test projects were CCP.Avalonia.CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests, Language.Tests, Report.Tests, Scheduler.Tests, Snapshot.Tests, CCP.Avalonia.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests, and CCP.Core.Tests. Final recompilation after the lifecycle adjustment also passed: CCP.Avalonia exit 0 (172 warnings, 0 errors), CCP.Avalonia.Tests exit 0 (50 warnings, 0 errors).
- `bash .github/scripts/core-guards.sh`: exit 0, "All Core guards passed." The initial read-only scratch-directory attempt failed; rerunning with scratch-directory access passed without source changes.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: initial exit 1 (2828 passed, 1 scheduling failure, 5 skipped). The unchanged `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` received a null schedule handle while the WPF compiler was active. Isolated retry of `GoonClockAndRoundsTests`: exit 0, 12 passed. Full retry without competing compilation: exit 0, **2829 passed, 0 failed, 5 skipped**, total 2834. The five existing Vosk cases skip because the model is unavailable in this environment; no tests were altered or disabled.
- WPF compile attempt: `timeout 240s dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` exited **124** at 240 seconds (154 warnings and MSB4166 when the compiler node was terminated). WPF compilation remains unconfirmed; Windows code was not run.
- Per the supplied cloud font limitation, the Avalonia suites were **compiled but not run**. No runtime red/green claim is made for the new regressions, and no render-all/native-browser/real-desktop proof was attempted. These remain for the supervisor's font-capable rcheck runner.
- Pending focused runner command (with `DiffEngine_Disabled=true`): `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~ChatThresholdMotionTests|FullyQualifiedName~HerRoomChatMemoryTests|FullyQualifiedName~ChatThresholdLinksTests|FullyQualifiedName~CompanionLinkLauncherTests|FullyQualifiedName~CompanionCardsRowTests|FullyQualifiedName~AmbientBeatLoopsTests.NoNewInfiniteAnimationInTheHead'`.
- `git diff --check`: passed. No WPF or Core source changes, new surfaces, new dependencies, or test suppressions.

## Assumptions

- WPF's cloned storyboard includes each child's BeginTime in that storyboard's repeated duration; preserve that timing instead of imposing one common 900ms cycle.
- Preserve the existing Fluent icon decision and native Avalonia wheel chaining.
- Do not duplicate the browser lane's media service or represent accepted-but-ignored autoplay as wired.
