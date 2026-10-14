# wire-shell-start-stop

- Added WPF’s empty-local-flash-library recovery notice after a deliberate shell start: 18 seconds, Media Manager → Assets, Get packs → Discord pack catalogue through the existing sandbox-aware ExternalOpener. Core/session/remote starts stay silent; online/mixed, disabled-flash and audio-only starts do too.
- Moved the WPF recursive extension/IO probe into Core `LocalMediaLibrary`; both WPF warning methods call it. WPF preserves its local-source policy, custom wallpaper-folder preference, and “IO failure means do not nag” rule.
- Added queued secondary notification actions with WPF’s two-row/wrapping layout, primary/secondary styling and timer stop before callbacks. Existing single-action layout remains intact. Added a live translated running status and clearing on engine Stop.
- Read WPF `MainWindow.StartStop.cs` and notification styling, current Core engine and head callers. Fetched read-only parity/trunk; no code imported from it.
- Ledger remains **stub**. Follow-up **wire-shell-start-stop-b**: Relapse achievements, brain-drain/autonomy start integration, plain ramp/scheduler lifecycle, wallpaper-arming notice in the head and Start hero FX. Complete remainder exceeds the 1000-added-line cap. Remote gate, TotalSessions and bubble-count/mind-wipe/pop-quiz/video starts were already real on this base.

## Verification limits

- Core `LocalMediaLibraryTests`: 7 passed. Full Core suite: 2818 passed, 5 existing Vosk-model skips, 0 failed (2823 total); no skip was added.
- Head `EngineEmptyLibraryTests|NotificationServiceTests`: 3 passed, including real Start/Stop, the running label, 18s warning, Assets recovery action and queued secondary action/timer/layout. Final rerun: 3 passed, including the Get Packs Fluent gift icon (no emoji fallback).
- WPF reference changes are a shared-probe refactoring. Windows desktop behaviour cannot execute on Linux. No new panic-owned surface: WPF leaves these non-blocking recovery toasts on screen while stopping the engine.

- All 14 required Release builds succeeded: Core, Avalonia, the Linux workflow test projects and the WPF test project with `EnableWindowsTargeting=true` / `ValidateExecutableReferencesMatchSelfContained=false`. WPF emitted 512 warnings and zero errors. The final IconLabel-only change was additionally compiled by the final focused head run.
- `bash scripts/check-core-guards.sh` passed. `--render-all /tmp/ccp-f-start-stop-render-all` completed 224/224 cases. Inspected `/tmp/ccp-f-start-stop-evidence/StartStop-empty-library.png`, captured from the real Start test after the final icon change: notice, both actions and Running/STOP status render correctly. Default render-all cases do not exercise the empty-library warning; the live test supplies that evidence.
- Commands used shared .NET 10.0.402, Release, `-m:1 -p:UseSharedCompilation=false`, the workflow WPF targeting flags and copy-hardlink flags. Tests used `DiffEngine_Disabled=true`; focused UI tests used `Program.BuildAvaloniaApp()` with shipped fonts.
- Linux cannot run the WPF desktop or verify interactive multi-monitor/native overlays. No account, network pack download or real user library was used; test settings/assets live in disposable sandbox directories.
