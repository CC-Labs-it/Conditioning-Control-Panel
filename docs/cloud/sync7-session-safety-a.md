# sync7-session-safety-a

Partial coherent session/remote safety slice; the shell parity rows remain **stub**.

- Adapt WPF `e5d607d85`, `9b8317e4e`, `293a89197` and `838c4908b`: move the pure `SessionClockRules` into CCP.Core, which WPF now references; independently guard runner tick/stop steps, count failures per named step, log the first full exception and a stop summary. A failed restore no longer skips the log or Stopped notification.
- Explicit user START paths (button, Start normally, Jump right in and companion quick menu) can resume a panic-paused session. Resume starts a stopped locally owned engine; remote/system starts never resume it. A resumed session does not increment TotalSessions. The session reasserts pending starts, lock-card remaining window and global Mind Wipe hold after restarting the engine.
- Paused session clocks carry the localized pause mark immediately, including when countdown is disabled.
- Adapt WPF `28078c756`: move `RemoteEndRule` to CCP.Core; ending/disconnecting Remote leaves a locally started engine and its running services alone, releases only the controller's strict lock and restores panic. Remote end causes remain distinct: leash cut keeps the WPF remote-service teardown, while explicit controller panic ends the real session runner and engine and retires deferred starts. Remote session-start verbs, remote browser playback and Brain Drain are not supported by this head, so no remote-started-session ownership is invented.
- No parity/trunk code imported. WPF current source and the six commits listed in the brief were read; existing Avalonia files were adapted in place.

## Remaining: sync7-session-safety-a-next

- `18ab80220` remains needs-port: browser takeover playback/fullscreen registration and remote browser host teardown are absent from this head. Port those hosts and their teardown together; adding an unused stop rule would not provide a user path.
- `293a89197` remains needs-port with partial evidence: first full exceptions go to Avalonia's existing daily Serilog file, not WPF's crash.log. Mirror first failures to the head's crash-report log when that logging path exists. Session-specific Mind Wipe remains an existing runner limitation (the global one is held off after resume).
- The broader shell Start/Stop and session parity rows remain stub; this branch does not claim every session feature or full WPF head parity.

## Verification limits

.NET 10; `DOTNET_PROCESSOR_COUNT=2`, `DiffEngine_Disabled=true`. Build options:
`-m:1 -p:UseSharedCompilation=false -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForAdditionalFilesIfPossible=true -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true`.
All desktop tests used the repository's sandbox profiles; the touched fixture uses the production font setup through `RenderProof.EnsureSetUp()`.

`dotnet build <project>` completed for every Linux target below (warnings remain):

| Project | Result |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | Exit 0; 0 errors |
| `CCP.Avalonia/CCP.Avalonia.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | Exit 0; 0 errors |

- `bash .github/scripts/core-guards.sh`: **All Core guards passed**. `git diff --check` passed.
- `dotnet test Tests/CCP.Core.Tests --no-restore -m:1 -p:BuildProjectReferences=false` with the build options above: **2865 passed, 0 failed, 6 skipped, total 2871**. Five skips need the unavailable Vosk model; the existing frame-budget test is skipped. One earlier full run hit the existing Goon scheduled-fire timeout; the unchanged test passed in the final full run.
- `dotnet test Tests/CCP.Avalonia.Tests --no-restore -m:1 -p:BuildProjectReferences=false --filter 'FullyQualifiedName~SessionRunWireTests|FullyQualifiedName~IconGuardTests'` with the build options above: **7 passed, 0 failed, 0 skipped**. The real Pause and START buttons restore the engine/session together, leave TotalSessions unchanged on resume, and update both clock labels. The test also renders `<temp>/ccp-sync7-session-safety-paused.png`; inspected it showing `Wire Test 00:59 [PAUSED]` in the real shell.
- Fail-before evidence: `SessionRunnerTests.ThrowingProgress_DoesNotEscapeTick_AndNextTickStillRuns` failed against the original runner before the guards were added.
- A broader head test batch (SessionRunWire, RemoteControlTab/Shell, RemoteHapticPanic, TubeQuickMenu, JumpRightIn, ShellTray and IconGuard) initially reported **15 failed, 6 passed** because existing bare AppBuilder fixtures could not resolve Inter/Segoe UI glyph typefaces before reaching behavior assertions. The touched fixture now uses the shipped font setup; unrelated fixtures were left for their own lanes.
- The first Avalonia output-copy attempt ran out of disk space; retry with output hardlinks and reclaimed generated output succeeded. All 13 Linux projects above ultimately built.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` with the build options above reached the WPF temporary-project compilation but did not complete within the 300-second attempt (runner exit 124). Its remaining compiler was stopped. **WPF was not fully built or executed; Windows UI/native behavior was not run here.**
- `CCP_USERDATA_DIR=/tmp/ccp-worker-2-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/ccp-worker-2-render-all`: exit 0, **225/225 views rendered**. `MainShellWindow.png` is the normal shell; the separately generated `ccp-sync7-session-safety-paused.png` shows the new paused clock state. The renderer emitted the expected sandbox showcase-manifest loopback connection-refused warning. No real desktop/profile was used.
