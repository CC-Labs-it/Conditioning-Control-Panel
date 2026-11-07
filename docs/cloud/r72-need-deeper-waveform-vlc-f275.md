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
| Cached waveform extraction and fallback | `Views/Deeper/DeeperEditorWindow.xaml.cs:1198`, `:1209`; `Services/Deeper/MediaDurationProbe.cs:32` | Ported: source-rate PCM16 WAV adapter, Avalonia decoder registration and asynchronous peak assignment. Waveform duration initializes the timeline before playback; zero playback duration retains it. Decode failure leaves a flat strip; late results cannot update a closed editor or different source. |
| Shared cache and bucket rule | `App.xaml.cs:309`; `Services/Deeper/MediaDurationProbe.cs:37` | Already: `CCP.Core/Services/Deeper/AudioWaveformCache.cs:46`, `:85`. WPF adapter stays unchanged. |
| Manual editor help | `Views/Deeper/DeeperEditorWindow.xaml.cs:378` | Ported: one staged editor overlay, rerunnable after closing, host-close and panic cleanup. |
| First-run editor coachmarks | `Views/Deeper/DeeperEditorWindow.xaml.cs:350` | Ported: saved once, deferred until layout, active/pending Part 2 takes priority. Pending launch cancels on close or panic. |
| Short-walk flash | `Services/TutorialService.cs:927` | Ported: seed FlashDemo with one image for 1600 ms, preserving an injected override. Missing shell/assets remains a no-op. |
| Retargeting, target preparation, deferred events | `Windows/TutorialOverlay.xaml.cs:209`, `:583` | Already: `CCP.Avalonia/Views/Windows/TutorialOverlay.Stage.cs:194`, `:239`; keep the base's in-window stage. |
| Part 1 and success-only Part 2 | `Views/Deeper/NewEnhancementDialog.xaml.cs:202`; `Views/Deeper/DeeperEditorWindow.xaml.cs:291` | Covered by PR #3237: editor `axaml.cs:594`, dialog `axaml.cs`; not copied. |
| EffectAdded, RuleAdded, FileSaved | `Views/Deeper/DeeperEditorWindow.Unified.cs:251`, `:408`; editor `xaml.cs:4511` | Covered by PR #3237: editor `Unified.cs:1086`, editor `axaml.cs:2550`; not copied. |
| Mod Creator section navigation | `Windows/ModCreatorWindow.xaml.cs:249` | Covered by PR #3345: Avalonia Mod Creator `axaml.cs:329`; not copied. |
| Editor interactions | WPF editor partials | PR #3316 still leaves `StartEditorTutorial` as a log stub at editor `axaml.cs:599`; this cut supplies help and first-run coachmarks. |

Review fixes follow WPF `Services/Deeper/MediaDurationProbe.cs:37` and editor
`xaml.cs:1198`: source-rate/channel PCM, waveform duration before playback,
and a cache tag bypassing old mono peaks. Core now accepts validated extensible
PCM16 from LibVLC for 5.1 audio. The six-channel fixture checks duration and
each channel's peak; malformed extension/subtype/format fields are rejected.
No WPF source changes or new translation keys. Native codec execution is unrun.
Earlier validation: Core 4638 passed/6 existing skips; waveform filter 13 passed;
affected Avalonia/race 47 passed/3 existing native skips. Earlier fail-proofs
covered stereo/rate, duration fallback, transcode options and cache migration.
LibVLC `modules/mux/wav.c:198` selects extensible headers for more than two channels.
Extensible validation: full Core suite 4640 passed/6 existing skips; waveform
filter 15 passed; all three builds 0 errors; affected Avalonia/race/isolation
22 passed/3 existing native skips. Removing extensible support failed the
six-channel test; bypassing validation failed the malformed-header test.
Both mutations restored; Core rebuilt and all 15 waveform tests passed again.
Core guards, docs self-check and git diff --check passed.

## Part B

No uncovered triage gap is deferred. The two rows remain `stub`, not verified.
Broader existing row gaps remain outside this cut:

- Overlay fade timing: WPF `Windows/TutorialOverlay.xaml.cs:108`, `:1305`;
  Avalonia `Views/Windows/TutorialOverlay.Stage.cs:52`, `:67` still show/tear down immediately.
- Sidebar door preparation: WPF `Windows/TutorialOverlay.xaml.cs:698`;
  Avalonia `Views/Windows/TutorialOverlay.axaml.cs:488` remains a stub.
- Preview fullscreen/zoom: WPF editor `xaml.cs:926`, `:1481`;
  Avalonia editor `axaml.cs:3256` fullscreen region still requires unsupported NativeWebView hooks.
- Part 2/events and Mod Creator integration are owned by #3237/#3345 above.
