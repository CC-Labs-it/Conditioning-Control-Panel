# Video leftovers re-cut onto 7.2

Base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`.
Working checkout: `/tmp/ccp` (points to `/workspace/ccp` for disk capacity).
WPF paths below are relative to `ConditioningControlPanel/`; Avalonia paths are relative to `CCP.Avalonia/`.
No WPF source, safety path, or Scrolller change. Parity status remains stub.

## Ported

| Behavior | WPF spec | Avalonia implementation |
| --- | --- | --- |
| Mod-aware hero and side art, 480/800px decode, UI-thread repaint, balanced subscription | `Features/VideoFeatureControl.xaml.cs:32,39,563,584` | `Views/Features/VideoFeatureControl.axaml.cs:79,100,106,116`; named borders in `.axaml:30,296` |
| Test failure logs and shows an error with the exception message | `Features/VideoFeatureControl.xaml.cs:532-533` | `Views/Features/VideoFeatureControl.axaml.cs:465-475` |

Reused the implementation and two tests from `dafbd27095a6`, with an additional mod-switch/lifecycle test adapted from the base's `FeaturePlateArtTests`. Error copy uses two new keys in all ten Core language files. No new Core platform types.

## Lane audit and part B

Fetched all three named lanes and compared their own-work ranges:

- haptics/device: `3092682f6419..dafbd27095a6`.
- shell settings: `ebc147dbfe04..50ba1436f695`.
- EMI settings: `0e1aa0473e7a..45d52d35f3f3`.

The complete remaining EMI path needs speech, offer feasibility, ask UI, queue/cancellation handling and narrator integration. The old ask UI alone is 323 lines, its offers host 120, its service speech 112 and narrator 98, before integration and focused tests. It cannot accompany this independent video slice within 600 changed lines. Keep base safety behavior when re-cutting those files.

| Status | Behavior and WPF spec | Avalonia evidence / continuation point |
| --- | --- | --- |
| Already | Mock feedback and shutdown cleanup, WPF `App.xaml.cs:3217` | `Views/Overlays/HapticMockToast.cs:22`, `App.axaml.cs:1258-1260`; retain base teardown |
| Already | External tab/rack usage mapping, WPF `Services/EmiDesk/EmiTargets.cs:125,139` | `Views/Windows/EmiDesk/EmiTargets.cs:37-51` uses shared `EmiDoors` |
| Already | Connecting edge emits `hapticsConnected`, WPF `App.xaml.cs:3225` seeds the sink | Shared `CCP.Core/Services/Haptics/HapticService.cs:160-171` already emits; the head sink remains missing |
| Missing, part B | Deliver that moment through the EMI director, WPF `App.xaml.cs:3225`, `Services/EmiDesk/EmiDeskService.cs:660` | `App.axaml.cs:639` still documents the absent sink; old `dafbd27095a6:CCP.Avalonia/App.axaml.cs:61` seeds it. Needs the speech PR first |
| Covered by PR #3247 | Funscript playhead subscription, WPF `App.xaml.cs:3218-3223` | `5d42c841ee:CCP.Avalonia/App.axaml.cs:639-640`; `MandatoryVideoOverlay.SubscribePlaybackTime` in that PR |
| Covered by PR #3288 | Startup clock/countdown speech | `ab43a43768:CCP.Avalonia/App.axaml.cs:192,203,677`; WPF `App.xaml.cs:3197-3206` countdown startup |
| Covered by PR #3305 | Save feedback | `987183403f:CCP.Avalonia/Views/Windows/MainShellWindow.Settings.cs:241`; WPF `MainWindow/MainWindow.HeroFx.cs:702` |
| Covered by PR #3319 | Basic moment speech and presentation hold guard, WPF `Services/EmiDesk/EmiDeskService.cs:677,765` | `5179f4f9be:CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskService.Speech.cs:13-36`; absent on base |
| Missing, part B | Tour summon, start, per-step fallback, completed/skipped ending, WPF `Services/EmiDesk/EmiTourNarrator.cs:121,139,159,186-195` | Base `Tours/TutorialHead.cs:100` starts without narrator; old `50ba1436f695:CCP.Avalonia/Views/Windows/EmiDesk/EmiTourNarrator.cs:59-95` and `CCP.Core/Services/EmiDesk/EmiTourMoments.cs:13-26` |
| Missing, part B | Ask feasibility, draw, waiting chips and one-slot speech queue, WPF `Services/EmiDesk/EmiDeskService.cs:207,750-755`, `Windows/EmiDesk/EmiDeskWindow.Bubble.cs:586` | Base `Views/Windows/EmiDesk/EmiDeskService.cs:26` has no speech partial; old `45d52d35f3f3:CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskService.Speech.cs:31-66`, `EmiDeskWindow.Ask.cs:19-40`, `EmiOffers.cs:25` |
| Missing, part B | Ring speech and pin nudge, WPF `Services/EmiDesk/EmiDeskService.cs:1273`, `Windows/EmiDesk/EmiDeskWindow.Ring.cs:237-258` | Base `Views/Windows/EmiDesk/EmiDeskWindow.Ring.cs:62` counts opens only; old `dafbd27095a6:CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskService.RingMoments.cs:46-88` |
| Missing, part B | Remote-media offer after empty-video guidance, WPF `Services/Video/VideoService.cs:2462-2469` and `App.xaml.cs:891` | Base `Views/Overlays/MandatoryVideoOverlay.cs:863`; old `dafbd27095a6:CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:821` and `Platform/RemoteMediaOffer.cs:51` |
| Missing, part B | Interaction takeover prompt, WPF `Features/VideoFeatureControl.xaml.cs:480-529` | Base and result `Views/Features/VideoFeatureControl.axaml.cs:482` explicitly defer this; the old lane also defers it |

## Validation

SDK installed with `dotnet-install --channel 10.0` (10.0.403). Required Core, Avalonia and Avalonia test builds succeeded with zero errors. Core tests: 4635 passed, 6 skipped, 0 failed. Focused video tests: 3 passed. Core guards and `avalonia_docs.py` passed.

Fail-proof: temporarily remove the initial art paint, mod subscription and error dialog, then run the same tests. All 3 fail (exit 1): shipped art is absent, no mod subscription exists and no error dialog opens. The exact final source was restored; rerun: 3 passed, 0 failed, 0 skipped.

An initial overlapping build hit a shared Avalonia PDB race. Sequential builds passed; no test or guard was weakened. Focused tests are `Tests/CCP.Avalonia.Tests/VideoFeatureLeftoverTests.cs`: shipped plates, mod switch and unsubscribe, and throwing Test action. Headless setup uses Skia per `TestIsolationTests.cs`.


## Review follow-up

The initial implementation notes above describe the original slice. Both P2 findings from the local review are fixed: the video constructor now calls `Helpers.ModArt.BindFeaturePlates` with `heroWidth: 480`. The helper keeps its original single 800px decode for callers that omit the parameter. WPF `Features/VideoFeatureControl.xaml:20` is the 480px hero spec. The copied painting and subscription code is removed.

The Test error now uses existing `title_error` and `msg_error_triggering_video_0` with `Loc.GetF`, matching WPF `Features/VideoFeatureControl.xaml.cs:533`. The two duplicate keys were removed from every language file. The existing English assertions remain unchanged.

The parity row keeps all earlier evidence and remains stub. Its stale statement that video art and the Test error dialog remain unported was removed. The review evidence is appended to its notes.

Commit `bc0366cf94` repaired the CI failure in `MakeHerYoursCommunityTests`: pointer input could precede the tab entrance and live compositor frame. The test now completes the entrance, waits for the live rendered batch, and asserts the actual hit target and pressed state. It also registers portable fonts for standalone execution. All original behavior assertions and the ten-second dialog timeout remain. This is test hardening, with no WPF or product behavior change. Release validation for that commit passed 33 affected tests and three standalone repeats with no skips.


Current review implementation references: `CCP.Avalonia/Views/Features/VideoFeatureControl.axaml.cs:69` binds the shared plates and `:442` uses the existing localization keys; `CCP.Avalonia/Helpers/ModArt.cs:193` owns the optional hero width. `FeaturePlateArtTests` additionally asserts that the default hero and side share the decoded source. Its standalone setup now registers bundled Inter; the first focused review run exposed the missing registration before reaching the art assertions.

The video mod-switch test also reused a second app bootstrap without Inter. It now calls the fixture's existing `EnsureApp`, so either plate test can run first without a font-registration failure; all behavior assertions are unchanged.

Review validation: Release build completed with zero errors; 16 focused plate, video and isolation tests passed with zero skips, and the Community test passed separately. Core tests: 4636 passed, 5 existing Vosk skips, 0 failed. Core guards, documentation self-check and the ten-language key-presence check passed. No WPF source or safety behavior changed.
