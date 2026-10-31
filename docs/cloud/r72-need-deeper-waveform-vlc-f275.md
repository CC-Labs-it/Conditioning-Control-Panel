# Deeper waveform and editor tours, re-cut onto 7.2

Base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`. Initial cloud HEAD was
`aa9918833722f057079e455167cb3d1b14462340`; fetched the intended branch and reset
local `work` to the required commit. Worktree was clean. No local requester changes.

Read each lane's own diff, without cherry-picking its ancestry:

- `deeper-waveform-vlc`: `61a934110bdb..1738cd25e159`.
- `fin-win-tutorial-overlay`: `679cb7d87fd4..901dc5ca3cc2`.
- `wire-views-deeper-editor-window`: `0e1aa0473e7a..9bda14a7abe8`.

| Behaviour | WPF specification | Disposition on this cut |
| --- | --- | --- |
| Cached waveform extraction and fallback | `Views/Deeper/DeeperEditorWindow.xaml.cs:1198`, `:1209`; `Services/Deeper/MediaDurationProbe.cs:32` | Ported: Core mono adapter, Avalonia decoder registration and asynchronous peak assignment. Transport duration stays authoritative. Decode failure leaves a flat strip; late results cannot update a closed editor or different source. |
| Shared cache and bucket rule | `App.xaml.cs:309`; `Services/Deeper/MediaDurationProbe.cs:37` | Already: `CCP.Core/Services/Deeper/AudioWaveformCache.cs:45`, `:84`. WPF adapter stays unchanged. |
| Manual editor help | `Views/Deeper/DeeperEditorWindow.xaml.cs:378` | Ported: one staged editor overlay, rerunnable after closing, host-close and panic cleanup. |
| First-run editor coachmarks | `Views/Deeper/DeeperEditorWindow.xaml.cs:350` | Ported: saved once, deferred until layout, active/pending Part 2 takes priority. Pending launch cancels on close or panic. |
| Short-walk flash | `Services/TutorialService.cs:927` | Ported: seed FlashDemo with one image for 1600 ms, preserving an injected override. Missing shell/assets remains a no-op. |
| Retargeting, target preparation, deferred events | `Windows/TutorialOverlay.xaml.cs:209`, `:583` | Already: `CCP.Avalonia/Views/Windows/TutorialOverlay.Stage.cs:194`, `:239`; keep the base's in-window stage. |
| Part 1 and success-only Part 2 | `Views/Deeper/NewEnhancementDialog.xaml.cs:202`; `Views/Deeper/DeeperEditorWindow.xaml.cs:291` | Covered by PR #3237: editor `axaml.cs:594`, dialog `axaml.cs`; not copied. |
| EffectAdded, RuleAdded, FileSaved | `Views/Deeper/DeeperEditorWindow.Unified.cs:251`, `:408`; editor `xaml.cs:4511` | Covered by PR #3237: editor `Unified.cs:1086`, editor `axaml.cs:2550`; not copied. |
| Mod Creator section navigation | `Windows/ModCreatorWindow.xaml.cs:249` | Covered by PR #3345: Avalonia Mod Creator `axaml.cs:329`; not copied. |
| Editor interactions | WPF editor partials | PR #3316 still leaves `StartEditorTutorial` as a log stub at editor `axaml.cs:599`; this cut supplies help and first-run coachmarks. |

Port locations on the Avalonia/Core head:

- Decoder registration: `CCP.Avalonia/App.axaml.cs:898` and `CCP.Core/Services/Deeper/AudioWaveformCache.cs:132`.
- Peak load/display: `CCP.Avalonia/Views/Deeper/DeeperEditorWindow.axaml.cs:961`, `:1001`.
- Manual help: the same editor `axaml.cs:589`; first-run: `:555`; panic cancellation: `:579`.
- Flash demo: `CCP.Avalonia/Tours/TutorialHead.cs:65`.

All WPF paths in the table are relative to `ConditioningControlPanel/`.
PR heads: #3237 `d82fc38339938eb5a4456844fa5caf580c15f32e`,
#3345 `cc81cd0997a13b70d420bac881f972b6ff9f0f2e`,
#3316 `5b428a7c87f9714f93fd01c5671f42da761c3e78`.
PR references mean the fetched `refs/pull/<number>/head`, not a claim that the PR
has merged. GitHub metadata queries returned Forbidden; git fetch of all three
PR heads succeeded. No WPF source changed. No new translation keys.

The decoder reuses LibVLC's race mono 16 kHz output. Its bucket count/cache rules
match Core; stereo cancellation and resampling can differ from WPF's original
full-rate channel maxima. Native codec playback and desktop flash were not run.
The base safety, stop/lock, Scrolller and staged-overlay implementations remain
in place; panic additionally cancels queued editor coachmarks.

## Validation

SDK channel lookup tried a denied ci.dot.net URL; installed SDK 10.0.100 with
`dotnet-install --channel 10.0 --version 10.0.100` instead.

- `dotnet build CCP.Core`: 50 warnings, 0 errors.
- `dotnet build CCP.Avalonia`: 201 warnings, 0 errors.
- `dotnet build Tests/CCP.Avalonia.Tests`: 144 warnings, 0 errors.
- `dotnet test Tests/CCP.Core.Tests`: 4636 passed, 6 existing skips, 0 failed.
- Affected Avalonia filter: DeeperRowsTests, DeeperEditorTutorialTests,
  TutorialFlashDemoTests, TutorialTourTests, TutorialStageTests,
  PanicSurfacesTests, TestIsolationTests: 43 passed, 0 failed, 0 skipped.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `python3 .github/scripts/avalonia_docs.py`: avalonia_docs self-check OK.
- Fail-proofs: truncating PCM failed the Core adapter test (1/1). Disconnecting
  peaks, editor help, first-run and FlashDemo failed their five focused cases.
  Leaving the queued intro armed on panic failed its separate test (1/1). Every mutation was restored before final validation.
- Final restored builds: Core 50 warnings/0 errors, Avalonia 201/0, tests 144/0.
  Restored cache filter: 11 passed; affected Avalonia filter: 43 passed again.
  Core guards and docs self-check passed again; git diff --check clean.

New tests exercise mono bucket peaks/duration, successful and failed waveform
loading beside playback, one manual overlay and rerun/host-close/panic teardown,
first-run once/active/pending guards, queued panic cancellation, FlashDemo seed,
override retention, and actual short-walk step activation.
No tests were removed or weakened.

## Part B

No uncovered triage gap is deferred. The two rows remain `stub`, not verified.
Broader existing row gaps remain outside this cut:

- Overlay fade timing: WPF `Windows/TutorialOverlay.xaml.cs:108`, `:1305`;
  Avalonia `Views/Windows/TutorialOverlay.Stage.cs:52`, `:67` still show/tear down immediately.
- Sidebar door preparation: WPF `Windows/TutorialOverlay.xaml.cs:698`;
  Avalonia `Views/Windows/TutorialOverlay.axaml.cs:488` remains a stub.
- Preview fullscreen/zoom: WPF editor `xaml.cs:926`, `:1481`;
  Avalonia editor `axaml.cs:3247` fullscreen region still requires unsupported NativeWebView hooks.
- Part 2/events and Mod Creator integration are owned by #3237/#3345 above.
