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
