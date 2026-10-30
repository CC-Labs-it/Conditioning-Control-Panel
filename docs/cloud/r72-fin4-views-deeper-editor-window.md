# Editor window re-cut

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad. Old lane: e3b1211c300a, compared against 0e1aa0473e7a.
Only views-deeper-editor-window is advanced. The full old lane is over the 600-line budget. This cut restores keyboard editing using existing model, undo, inspector and transport paths. No WPF, Core, safety or Scrolller behavior changes.

## Ported

Paths below are relative to CCP.Avalonia/Views/Deeper unless prefixed otherwise. WPF paths are relative to ConditioningControlPanel/Views/Deeper. Old references are at e3b1211c300a; current references are in this commit.

| Behavior | Current | Old | WPF |
| --- | --- | --- | --- |
| Delete and Escape include rules and effects, retain text editing | DeeperEditorWindow.axaml.cs:3034; DeeperEditorWindow.MultiSelect.cs:83 | DeeperEditorWindow.axaml.cs:3114; DeeperEditorWindow.MultiSelect.cs:594 | DeeperEditorWindow.xaml.cs:5054; DeeperEditorWindow.MultiSelect.cs:631 |
| Left/Right nudge by 0.1s, Shift by 1s; no selection seeks; arrow controls retain keys | DeeperEditorWindow.axaml.cs:3046; DeeperEditorWindow.MultiSelect.cs:102 | DeeperEditorWindow.axaml.cs:3127; DeeperEditorWindow.MultiSelect.cs:646 | DeeperEditorWindow.xaml.cs:5027,5066; DeeperEditorWindow.MultiSelect.cs:738 |
| Preserve lengths and clamp starts; move rule time and constrained band; undo | DeeperEditorWindow.MultiSelect.cs:95 | DeeperEditorWindow.MultiSelect.cs:639 | DeeperEditorWindow.MultiSelect.cs:731 |
| End seeks to end; brackets skip current start and jump across region, haptic, effect and time rules | DeeperEditorWindow.axaml.cs:3053; DeeperEditorWindow.MultiSelect.cs:140 | DeeperEditorWindow.axaml.cs:3134; DeeperEditorWindow.MultiSelect.cs:684 | DeeperEditorWindow.xaml.cs:5073; DeeperEditorWindow.MultiSelect.cs:776 |
| Undo/redo rebuild rule pins with restored object identities | DeeperEditorWindow.MultiSelect.cs:692 | DeeperEditorWindow.MultiSelect.cs:805 | DeeperEditorWindow.MultiSelect.cs:851 |

## Already on base

Owner activation already exists at CCP.Avalonia/Views/Deeper/DeeperEditorWindow.axaml.cs:232. Deferred activation with visibility/minimized guards remains Part B (old CCP.Avalonia/Platform/WindowChrome.cs:38; WPF editor :160).

Local audio playback, seek, duration and end handling: CCP.Avalonia/Views/Deeper/DeeperEditorWindow.axaml.cs:949 and DeeperLocalAudio.cs:1; old editor :928; WPF DeeperEditorWindow.xaml.cs:1190. Waveform decode remains absent.
Preview allowlist on every hop and safe local-file checks: CCP.Avalonia/Views/Deeper/DeeperEditorWindow.axaml.cs:715,1040 and DeeperPreview.cs:29. Panic stops open-editor audio at editor :1001. These base paths are retained. Old editor :695,981,1023; WPF editor :507,5182.

## Part B

These old-lane additions are absent from this cut. Locations identify reusable old implementations and their WPF specifications, rather than implying they are wired here.

| Missing | Old lane implementation | WPF specification |
| --- | --- | --- |
| Waveform decode | CCP.Core/Services/Deeper/WaveformPeaks.cs:1; editor :1030 | editor :1198 |
| Native title chrome, browser profile | CCP.Avalonia/Platform/WindowChrome.cs:1; editor :202 | editor :159,507 |
| Page zoom/fullscreen, panic fullscreen teardown | DeeperPageBridge.cs:1; DeeperEditorWindow.Preview.cs:14 | editor :754,5182 |
| Haptic Test with device/routing feedback | DeeperEditorWindow.RuleEditor.cs:302 | editor :2739 |
| Preview into shared player | DeeperEditorWindow.RuleEditor.cs:1605 | editor :4164 |
| First-run/help tutorial and tutorial save events | editor :549,581,2598; CCP.Core/Services/Deeper/DeeperEditorIntroRules.cs:1 | editor :342,378,4511 |
| Dirty OS title, saved strip, hover time | editor :2707,2596,1563 | editor :4642,4504,1794 |
| Ctrl+D preserves clipboard; context duplicate/copy/cut/delete/jump; Ctrl+0 | DeeperEditorWindow.MultiSelect.cs:525; DeeperEditorWindow.Unified.cs:146; editor :3144 | DeeperEditorWindow.MultiSelect.cs:559; DeeperEditorWindow.Unified.cs:156; editor :5083 |
| Inspector undo sessions and region-end blur normalization | DeeperEditorWindow.MultiSelect.cs:608; editor :1819 | DeeperEditorWindow.MultiSelect.cs:700; editor :2091 |
| Persist timeline height and constrained-band drag inspector refresh | DeeperEditorWindow.MetadataDrawer.cs:222; editor :1547 | DeeperEditorWindow.MetadataDrawer.cs:208,220; editor :1777 |

Still missing even in the old lane: interactive tutorial Part 2 dispatch/advance, native browser-focus shortcut forwarding, Linux profile isolation/native browser hardening and native zoom equivalence, detached/past-end/non-time rule markers, relink probe with three-way cancel, anchored click-away help, local-video codec and waveform channel parity. Fluent icon divergence remains.

## Validation

Installed .NET SDK 10.0.403 with dotnet-install --channel 10.0 --version 10.0.403. Channel-only discovery tried a denied ci.dot.net endpoint; the stable builds endpoint succeeded.

- dotnet build CCP.Core: succeeded, 0 errors.
- dotnet build CCP.Avalonia: succeeded, 0 errors.
- dotnet build Tests/CCP.Avalonia.Tests: succeeded, 0 errors (existing warnings).
- dotnet test Tests/CCP.Core.Tests: 4635 passed, 6 existing skips, 0 failed.
- Editor interaction, DeeperRows, opacity labels and TestIsolationTests: 27 passed, 0 failed.
- Fail proof: temporarily bypassed the new key dispatch and removed history pin rebuild; all 10 new cases failed on assertions, 0 passed. Restored both files automatically before final validation.
- core-guards.sh: all Core guards passed. Initial read-only invocation could not create temporary files; rerun with write access passed.
- avalonia_docs.py: self-check OK. git diff --check: clean.

The affected older tests now use the production app builder, including bundled fonts, instead of a bare builder. Their assertions are unchanged. The first combined run exposed missing Inter registration; the corrected combined run passed.

Validation is headless. Native browser focus/fullscreen, physical haptics, native title chrome, real audio/video codecs and interactive rendering were not exercised. The full Avalonia test assembly was compiled; only the listed affected suites were run. Core speech integration retains its existing environment-dependent skips.
