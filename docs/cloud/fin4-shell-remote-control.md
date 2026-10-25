# FIN4 shell remote control

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. The independent cloud snapshot was at a different commit and read-only; work uses the authorized fallback checkout.
- Hide the native embedded browser while the controller overlay is visible. Restore after its 200 ms exit fade, cancel restoration on reconnect, and respect an active leash gate's browser blindfold.
- Notify every controller join; flash the Windows taskbar only when the shell is hidden/minimized, without restoring or activating it. Keep platform code behind a small seam; Linux retains the OS notification.
- Route pink/spiral show/stop and opacity through Core to the existing head hosts. Preserve WPF bounds (pink 0–50, spiral 0–100), default 25, null-parameter no-op, save order, and ability to run without starting the engine. Stop/panic clears the remote hold without clearing saved switches; explicit engine Start or a later overlay command releases suppression.
- Route mind-wipe trigger/start/stop to the existing Core host: trigger only with local clips, start with saved frequency and volume, stop on opted-in disconnect as well as panic.
- Add focused Core and headless regressions and numbered tester steps. No new windows or effects: existing overlay hosts retain their panic registration.
- Replace the repeated historical ledger prose with one honest `stub` row. Directory opt-in was already wired on the base; it is not a remaining gap.
- Reference branch fetched read-only: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk source imported. Adapted behavior directly from WPF at the pinned base; historical imported commits remain credited in the ledger.

## WPF gap inventory and scope

Compared `MainWindow.RemoteControl.cs`, `RemoteControlService.cs`, `MainWindow.RemoteHud.cs`, `Windows/RemoteHud/RemoteHudRules.cs`, and the v2 service against the base's shell, tab, relay and command table.

Implemented here: browser airspace blindfold, Windows join flash, pink/spiral switches and opacity verbs, mind-wipe verbs. Existing live behavior includes relay lifecycle, consent/pairing, directory opt-in, command/emote UI, overlay session info, Start lock, OS notification, haptics and safety gates.

Still missing (row remains **stub**, never verified):

- HUD pill: top-centre nonactivating placement, name/initial/connect elapsed/last action, fold/unfold, strength tag, More/Easy/Stop, 1600 ms confirmation and 2500 ms Stop debounce, local panic/leash safety. Easy also needs haptic rescale and opacity restoration.
- Live preview/status: screen/capability feeds, media/idle counts, controller name, event rings, available-session list and session progress callbacks. Advertise only supported commands.
- Remote session start/pause/resume/stop: available built-in/custom lookup, generic fallback preserving local settings, start ownership and teardown, strict request filtering, and local-panic pause refusal. SessionRunner exists but these WPF ownership/safety semantics have no remote caller yet.
- Controller HypnoTube playback/stop: autoplay/fullscreen lifecycle and browser reveal/re-hide, tray minimize/restore. Basic browser navigation and a tray host exist; the remote playback host is still absent.
- Autonomy, Brain Drain/Melt, wallpaper and trigger_lock_card commands, remote quest/controller-word hooks, and merged-account 409 recovery after every relay call.
- Native Linux taskbar urgency equivalent; Windows overlays retain the base's platform restriction.

These are live WPF paths, not P122 dead code. The combined HUD, status, session ownership/safety, browser playback and account recovery work exceeds this unit's 1000-added-line budget; no standalone stub hosts or unused interfaces were introduced to pretend they were complete. User-visible gaps that reuse existing hosts were prioritized.

## Verification limits

- Pre-fix regression: eight new overlay switch/opacity cases failed, all returning `not on this build` (0 passed, 8 failed). The final tests seed a fake head refresh seam.
- SDK: .NET 10.x. All Linux workflow test projects, CCP.Core and CCP.Avalonia built with zero errors (see command results below).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3964 passed, 5 existing Vosk-model-dependent tests skipped, 0 failed. No test was weakened or newly skipped.
- Focused Core remote/session filter: exit 0; 40 passed, 0 skipped, 0 failed.
- Relevant Avalonia filter (`RemoteControlShellTests|RemoteControlTabTests`): exit 1; all three tests stop at font creation (`Inter` / `Segoe UI Black`) before behavior assertions. Compiled with 0 errors; behavioral execution requires the supervisor's font-capable runner. Browser/attention regression red/green execution is therefore unavailable here.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0.
- `.github/scripts/test-plan-coverage.sh` is absent at this base; invocation returned 127. Numbered WPF/Rows tester steps were added anyway.
- Initial multi-project output copies exhausted the 8.8 GB writable disk. The complete retry builds sequentially and removes only generated per-project test outputs; final results supersede those infrastructure failures.
- Windows taskbar code is compiled on Linux, not executed. Headless attention tests use an injected callback; OS notifications use the existing sink. No real desktop or user profile is used by tests.
- No live paired-controller, composited-X11 overlay, tray/taskbar, or native browser playback test. The remaining HUD/session/browser/account behaviors above are not claimed wired.

## Assumptions

- Preserve WPF Windows taskbar behavior behind an OS seam; use the existing notification on Linux until a native urgency seam exists.
- Do not introduce a browser reveal API without a working remote playback caller. Restore the idle blindfold lifecycle now and retain playback as an explicit gap.
- Preserve the repository's existing overlay platform restrictions and use the already registered hosts.

## Command results

All builds used `dotnet build <project> -c Release`; each ended with **0 Error(s)**. The WPF test build also used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; Windows binaries were compiled, not run.

| Check | Exit |
| --- | --- |
| `build CCP.Core/CCP.Core.csproj` | 0 |
| `build CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `build Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `build Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `build Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `build Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `build Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 |
| `build Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `build Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `build Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |
| `head-focused` | 1 |
| `build Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `build Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `build Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |
| `core-focused` | 0 |
| `core-all` | 0 |
| `build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 |
| `core-guards` | 0 |

Test result tails: Core focused **40 passed / 0 failed / 0 skipped**; Core full **3964 passed / 0 failed / 5 existing Vosk skips**; Avalonia focused **0 passed / 3 font-creation failures**. All tests used `DiffEngine_Disabled=true`. The five Vosk cases did not execute without the model; native Windows, real controller pairing and real desktop effects were not run.
