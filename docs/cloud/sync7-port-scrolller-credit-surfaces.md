# sync7-port-scrolller-credit-surfaces

- Ported the existing flash host's on-picture attribution from WPF `e878ce391b9afd03440cdd8b4078ac8b085ede45`: the media path reaches `CCP.Avalonia/Views/Overlays/FlashOverlay.cs:145`; `FlashOverlayWindow.cs:236` places the passive credit inside the picture card, bottom-right, sharing its geometry and native opacity envelope. It disappears on pop/exiting fragments like the reference compositor path. No work-area chip or separate window is introduced.
- Added the minimal identity prerequisite in `CCP.Core/Services/Fyp/Online/ScrolllerCredit.cs:13`, using the existing ScrolllerPolicy URL boundary and WPF's entry-id/remote-temp contract. No feed selection, downloads, browser launches or sibling credit surfaces are added.
- Added `ScrolllerCreditIdentityTests.OnlyScrolllerMediaCarriesTheMark` and `ScrolllerFlashCreditTests.MarkBelongsToPictureAndLeavesWithIt`, plus the tester step in `docs/avalonia-test-plan.md` section 9.
- Read-only `origin/parity/trunk` was fetched and inspected at `a4ee52d40`. No trunk code was imported: this adaptation uses the listed WPF reference and its existing credit identity prerequisite.

## Commit dispositions

- `6e3580fd00a249d785be76019bdc4c29d50e588b`: **blocked-by-host**. The ledger correctly says “head: none yet”. There is no Avalonia BrowserVideoEngine, browser-player message bridge, or floating mandatory-video host. `CCP.Core/Services/Video/MandatoryVideoScheduler.cs:38` explicitly excludes remote clips; `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:192` uses LibVLC events, not the already-merged web player. Adding the remote pool and browser/floating hosts exceeds this unit. No uncalled URL registry or page payload helpers were copied.
- `d0b04bd5d3f64c482bece4a8f64f6001a570513d`: **blocked-by-host** for the same browser-credit session lifecycle. There is no creditClick/mediaEnded host handler or queued browser link to fix. The existing native video guard/attention lifecycle remains unchanged.
- `e878ce391b9afd03440cdd8b4078ac8b085ede45`: **ported to the existing native flash host**, files and tests above. There is no Avalonia FlashLayer compositor to update. Remote flash selection remains absent (`FlashOverlay.LoadPictures` walks local and pack assets); callers presenting a WPF-labelled remote-temp picture now receive the credit. This is surface support, not a claim that remote flash acquisition is wired.

## Panic ownership

The credit is a child of FlashOverlayWindow, not a new surface. Existing `PanicSurfaces` entry `engine` calls `MainShellWindow.StopEngine`; its teardown reaches `App.StopDesktopOverlays` / `App.axaml.cs:1009` and `FlashOverlay.CloseAll`, including flashes playing exits. Thus the mark closes with its picture and creates no independent timer/window requiring registration.

## Assumptions

- A native per-picture flash card is the counterpart of the WPF per-window path. Reuse its existing fade instead of starting another credit animation; use WPF's badge text, backing, 11 px size, 75% opacity, 6/4 margins and passive input behavior.
- Preserve the WPF identity contract for remote temp copies. Do not infer that arbitrary local files or arbitrary HTTPS URLs belong to Scrolller, and do not implement remote acquisition in this lane.
- Browser and floating video infrastructure is out of scope, as directed for missing hosts. No ledger or parity status was promoted.

## Verification limits

- `dotnet build -c Release`: Core, Avalonia, and every Linux test project named in `.github/workflows/build.yml` were built. The sequential matrix passed except the new Avalonia test initially needed a missing enum namespace import; its corrected rebuild passed with 0 errors (exit 0). Generated test binaries were removed between projects to fit the writable disk.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML references checked). `test-plan-coverage.sh` is absent on this base.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 1, 3964 passed / 2 failed / 5 skipped. The unchanged failures were `BoardTilesTests.A_frame_fits_the_budget` and `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`. Both passed in isolation together with all 13 new identity cases (15 passed, exit 0). Vosk tests skipped because the model is unavailable.
- Red proof: temporarily changed `ScrolllerPolicy.IsScrolllerUrl`'s recognition branch in the new helper to return false. `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~ScrolllerCreditIdentityTests` failed as expected: 2 failed / 11 passed, exit 1. The production line was restored immediately; the same filtered command then passed all 13 cases (exit 0).
- Avalonia runtime tests are deferred under the task's cloud-font environment limit. Supervisor command: `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~ScrolllerFlashCreditTests`. The test assembly already installs a `CCP_USERDATA_DIR` sandbox and disables real-desktop awareness.
- Initial validation attempts also hit read-only temporary storage, an overlapping Avalonia output-file lock, and writable-disk exhaustion. Writable reruns and sequential builds with generated-output cleanup resolved those infrastructure issues. The disk-exhausted full Core run is superseded by the result above.
- Windows UI cannot execute on Linux. No desktop notification, live user profile, or real media network request is used by the new tests.

- Windows-targeted compilation: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings / 0 errors. WPF application and test assembly compiled; neither was run.
