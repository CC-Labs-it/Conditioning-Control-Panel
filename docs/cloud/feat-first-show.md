# feat-first-show

- Added pure deterministic EMI score and mono PCM WAV synthesis to Core. Adapted the frozen WPF `EmiVox` recipes from read-only `parity/trunk` tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` (the reference file matches this base; its last trunk change is `6e0042c765ec7b21b6760f6612b7b63110095bac`). Playback, master volume, output suppression, two independent stoppable voice/cue slots and disposable per-show clip caching remain in Avalonia through CoreAudio.
- Speech lands once when the card appears after EMI settles, with WPF's serious/celebration moods. Confirmation includes the panic hint. Close, Escape and panic release playback and clips.
- Help and EMI book replay buttons use the existing singleton FirstShowService; the first-run door and panic surface are retained.
- Only live bubble circles contribute native input rectangles, using narrow inscribed pixel strips. Movement, popping, expiry, gather and close update the region. Pointer events on the transparent canvas reach the existing Pop callback; all other stage points remain click-through. Failed native region updates close the show.
- The 11–15 s scene reuses the base's BrainDrainVisual and BrainDrainCapturePump; no additional capture implementation or session settings changes. The stage, EMI and speech windows are excluded from capture. Phase two enables melt when motion permits; the same light veil remains when capture is unavailable.
- Added focused synthesis, audio and stage tests. Updated the ledger to `wired`, never `verified`.

## Verification limits

- Base verified at `890d82bb7d5394c8432e765261938208a350f6fb`; clean snapshot on local `work` before creating the feature branch.
- .NET SDK 10.0.402 installed with dotnet-install. Initial channel lookup returned 403; the official SDK download succeeded.
- `dotnet build -c Release`: Core, Avalonia, and all twelve workflow-listed test projects, including the WPF tests with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: zero errors. Existing warnings remain.
- `bash .github/scripts/core-guards.sh`: passed, 429 XAML references checked. An initial read-only sandbox attempt could not create its temporary directory; rerunning with authorized write access passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3,350 passed, five existing Vosk tests skipped (model unavailable), zero failed.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~EmiVoxSynthesisTests`: 11 passed. An isolated comparison against the unchanged WPF recipe passed for 30 representative line/mood combinations, with byte-identical WAV output.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~FirstShowAudioTests`: exit 0; one passed after restoring playback. This test creates no window and needs no fonts.
- Fail proof: deliberately changing BasePitch made the frozen-score test fail (exit 1); disabling voice playback made the audio test fail (exit 1). Both mutations were restored before final validation.
- The extended FirstShowStageTests compile with zero errors but were not run: this snapshot predates the cloud font test fix and the supplied environment limits prohibit that window-test run here. The supervisor's GitHub rcheck must exercise replay, settled speech, pointer-to-Pop, gather/close regions and shared blur lifecycle.
- Native X11/Wayland stage checks were not run: this environment has neither an isolated X server nor a Wayland compositor. No real desktop or profile was used. Native Wayland remains refused by the existing click-through gate; the headless test covers the same unsupported-native-region path. Windows code and WPF tests compiled but were not run on Linux.

## Assumptions

- Reuse the shared BrainDrainVisual directly as a scene-owned visual so onboarding neither starts a session nor changes the session overlay. Its status text is hidden to match WPF's show; the fallback veil remains visible.
- Use conservative strips inside each bubble circle rather than a rectangular bounding box, so corner points stay click-through. Native Win32 keeps the repository's cursor-based rectangle seam.
- Keep the cached clips for one show, matching WPF FirstShowAudio, then stop playback before deleting them.
