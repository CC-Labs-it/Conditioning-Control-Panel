# fin-feat-first-show

- Base checkout verified at `db079bf91474c77a5ea67ec306f6337a7de44b83`; only the task branch is published. WPF source remains frozen.
- Restore Help and EMI book replay in WPF order: close the Help overlay and restore the browser, or close the book, then call the existing first-show service. All paths keep the existing `first-show` panic registration and presentation cleanup.
- Restore deterministic bleepese, independent voice/cue replacement, master/output-suppression gating, per-show WAV caching/cleanup, landed-card timing, full warning speech, and the 1.4 s speaking bob/turn. Reuse CoreModArt for mod cue overrides.
- Give only live bubbles native input regions using the existing Win32/X11 overlay helper. Left presses pop through the stage; gather and the guide empty the regions. Reuse X11Pointer for unfocused Linux Esc. Sandbox profiles never read desktop input.
- Add synthesis/audio behavior tests and window coverage for replay, speech timing/emphasis, bubble region/pop/gather, Esc and actual pointer dwell/reset on the guide help target.
- Import credit: pure score and synthesis adapted from trunk commit `e5ea65403839f2f6e8ce6b0fe543f39171e4da22` (`CCP.Core/Services/EmiDesk/EmiVox.cs`), read at fetched trunk SHA `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. The audio cache/playback pattern is adapted from `cffbc262021e8eaeea36d380c042154287c98b5d` (`FirstShowAudio.cs`). Existing base files were merged in place. No unused interface, playback instance or typing-tick synthesis was imported.

## WPF gap audit

Reference pinned to base SHA `db079bf91474c77a5ea67ec306f6337a7de44b83`: all files in `ConditioningControlPanel/Services/FirstShow`, `Services/EmiDesk/EmiVox.cs`, `MainWindow/MainWindow.xaml.cs` (first-run and Help replay), `Windows/EmiDesk/EmiBookWindow.xaml.cs` (book replay), and `App.xaml.cs` (DEBUG preview).

| Gap on entry | Result |
| --- | --- |
| Bleepese and speech-linked bob/turn; warning must include stop-key text | Implemented from the WPF score/synthesis and landing clock; cues stay independent. |
| Help and book replay doors | Implemented through the existing service and its panic surface. |
| Bubble pops on the click-through stage | Implemented with existing native input regions; no desktop polling for clicks. |
| Unfocused Linux Esc when the panic key is disabled | Implemented using the existing non-consuming X11 reader. Native Wayland still refuses the show before borrowing EMI. |
| Guide help dwell was only exercised by calling FoundHelp | Test now moves the headless pointer, leaves to reset dwell, then waits the full Core duration. Execution deferred below. |
| Brain-drain blurred capture then moving melt, with WPF blur alpha | Still missing. Depends on the unported Brain Drain capture/overlay (`views-studio-brain-drain-feature`). A milky veil is not a blur. Trunk's capture pump alone is 459 lines and Windows-only; importing it plus its rules, renderer and coverage cannot fit this unit's 1,000-added-line cap. |
| Production effect artwork | Still approximate: direct-draw bubbles omit WPF's shipped sprite/glow, and picture frames omit the production FlashLayer glow. Existing sparks are direct-draw equivalents, without a side-by-side fidelity claim. |
| Virtual-desktop guide | This head selects the shell's monitor at guide entry; WPF spans the virtual desktop and can follow the shell across monitors throughout the tour. Mixed-DPI/moving-monitor parity remains unproven. |
| DEBUG preview launch | Still missing; WPF's live caller is in App.OnStartup under DEBUG. It is a developer-only path, not dead code or a P122 exemption. |
| Composite screenshot assumes shell at screen origin; no WPF side-by-side | Evidence limitations, not a production screenshot feature. This unit adds no capture/compositing path and claims no visual verification. |

The ledger remains **stub**, with one row for the ID. This is not a dependency-only NOOP: replay, speech and input had independent reachable gaps, so they were completed first. No new window or overlay surface was introduced.

## Verification limits

- SDK: .NET 10.0.402 installed with the prescribed dotnet-install command (exit 0; the optional ci.dot.net latest-version probe returned 403 before the builds feed succeeded).
- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `dotnet build <test-csproj> -c Release` for every Linux test project named in `.github/workflows/build.yml`: Core, Core.Settings, Core.Content; Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Migration, Avalonia.Report, Avalonia.Scheduler, Avalonia.Snapshot. All exit 0 with 0 errors; existing warnings remain.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors (554 existing warnings). WPF compiled but cannot execute on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exit 0; 3,556 passed, 5 existing Vosk skips, 0 failed. Vosk model download is blocked by the stated environment limit; no test was weakened or disabled.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter FullyQualifiedName~FirstShowAudioTests`: final exit 0; 4 passed, 0 failed. Reinstating the pre-fix suppression/override behavior first produced exit 1 (2 failed, 2 passed): suppressed output played twice, and the mod cue resolved to the stock asset. Fixes were restored before the green run. These device-free tests do not initialize Avalonia fonts or windows.
- `FirstShowStageTests` and the rest of the window tests compiled with 0 errors but were **not run**, following the task's measured font-creation limitation. Their new UI regressions could not be fail-proven in this environment; the supervisor's GitHub rcheck must run them. No real desktop or profile was used by tests.
- No native Windows/X11/Wayland input run, audio-device run, mixed-DPI live run, WPF side-by-side or render-all was performed. Existing panic registration was checked at `Views/Windows/PanicSurfaces.cs` (`first-show` -> FirstShowService.Stop).

## Assumptions

- Keep the first-show row honest as `stub` while capture, production-effect visuals, multi-monitor guide and DEBUG preview remain; do not call it wired based on compilation.
- Reuse the regular bubble overlay's rectangular native input-region strategy rather than introducing another mouse hook. Esc uses the shared X11 state reader; an Esc press shorter than a frame can be missed, as with the existing polling seam.
- Keep a separately named internal pure `FirstShowVoice` to avoid changing or shadowing the frozen WPF EmiVox type; keep all synthesis constants and recipes unchanged.
