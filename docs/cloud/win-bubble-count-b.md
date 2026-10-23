- Keep existing CountBubble windows above a clicked game: tunnel pointer presses through the game and forward native browser pointer events (including secondary monitors) to the existing X11 restack helper. Windows installs Avalonia's WndProc hook returning MA_NOACTIVATE without swallowing mouse input or making the game click-through.
- Move decoder startup, pause, stop and native disposal off the UI thread. Arm an eight-second startup/UI-heartbeat guard before decoder creation; keep it armed through teardown. Stop waits at most 500 ms, with a separate 500 ms native-disposal budget. Quarantine the entire player/media/sink graph on timeout or native failure; never recycle it on late completion. Detach queued frame publication without freeing callback buffers prematurely.
- Gate new native games with WPF's 60-second poison cooldown using a monotonic clock; browser games bypass that gate. Unseen native ends/timeouts skip instead of opening an answer or recording a loss. Fence stale player, browser, frame and delayed bubble callbacks; panic cancels a completion still awaiting teardown.
- Add deterministic fake-stop/clock tests and extend BubbleCountGameTests for click restacking, secondary browser input, cooldown skips, and panic/native-close races. Retain both existing win-bubble-count ledger entries as stub.
- Reference provenance: fetched and inspected read-only parity/trunk at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; it still has the same native-safety stub. No trunk files imported or overwritten. WPF behavior adapted from the frozen reference at base `e85d70acffa4e285a036d4ad48ea9a5d0a5c1fc6` (activation hook, startup guard, bounded stop, teardown ordering).

## Verification limits

- Checkout initially clean at `41d34136d3e3d4225ad48da419d3943184da8fbe`; fetched the specified base and verified HEAD equals `e85d70acffa4e285a036d4ad48ea9a5d0a5c1fc6` before creating this branch. No requester-local changes were present.
- SDK: .NET 10.0.402. All commands below use Release and `DiffEngine_Disabled=true` for tests. Builds ran sequentially with `-m:1`; an earlier overlapping build lost an MSBuild worker (MSB4166), then the sequential builds passed.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.` The initial read-only-sandbox invocation could not create temporary files; rerunning with writable temporary storage passed.
- Regression proof: temporarily replacing the bounded stop wait with an unbounded await caused `Wedged_stop_is_bounded_quarantined_and_never_disposed_on_late_return` to fail (exit 1, TimeoutException). Restored the fix before final checks. This is a behavioral mutation check, not execution of the entire original window implementation.
- `bash scripts/x11-overlay-probe.sh`: exit 127, `kwin_wayland not installed`. No live desktop was touched. X11 native stacking integration remains unrun.
- Font-dependent BubbleCountGameTests and BubbleCountQueueTests were compiled, not run, following this job's measured cloud-font limitation; the cloud-test-fonts lane is not present in this base. Supervisor rcheck must execute these tests. The deterministic lifetime tests require no fonts and ran locally.
- Windows activation code and the WPF test project compiled, but no Win32/WPF tests ran on Linux. No live browser-provider or LibVLC playback smoke was run. The five Core Vosk tests retain their existing skips because the model is unavailable (the job reports download HTTP 403); no tests were weakened or disabled.

Build commands and results (all tails end in `0 Error(s)`):

| Project passed to `dotnet build <project> -c Release -m:1` | Exit |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 |

The WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.

| Test command | Exit | Result tail |
| --- | --- | --- |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build` | 0 | `Passed!  - Failed:     0, Passed:  3343, Skipped:     5, Total:  3348, Duration: 35 s - CCP.Core.Tests.dll (net10.0)` |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~NativeVideoLifetimeTests` | 0 | `Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 892 ms - CCP.Avalonia.Tests.dll (net10.0)` |

Final source changes were rebuilt with `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~NativeVideoLifetimeTests`; exit 0: `Failed: 0, Passed: 6, Skipped: 0, Total: 6`.

## Assumptions

- A request arriving while another game or native teardown is active is skipped; it cannot take ownership of the previous decoder.
- WPF's eight-second stall threshold, 500 ms emergency-stop budget and 60-second cooldown are the fixed policies for this unit. Native disposal also gets a bounded wait, separately from Stop.
- The image-backed Avalonia player needs no HWND-video detach rescue: native startup and teardown now run off-thread. The managed heartbeat posts a skip to the dispatcher; it does not forcibly hide native handles during an unrelated dispatcher stall.
- Quarantine is an intentional process-lifetime leak. Cooldown expiry permits a fresh player, never reuse or disposal of the quarantined graph. No shared-LibVLC retirement mechanism is added in this unit.
