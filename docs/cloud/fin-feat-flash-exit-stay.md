# Flash exit/stay: remaining visual behavior

- Add Glitch's red/cyan modulated ghosts behind the existing seven slices, using an immutable `SKImage` and recorded render operations. Preserve WPF's split distance, tint, alpha and threshold.
- Drive lucky gold blur/opacity through the existing shared frame clock and `FlashFxRules.LuckyPulseAt`; unsubscribe on leave, Shatter and close. Add the WPF lucky toast: one reused passive window, primary-work-area placement, 200 ms in, three-second hold after the latest proc, 300 ms out, refresh during fade, notice/game suppression and panic/Stop cleanup. Use sibling BoxShadows.
- Add the owned Rounded corners switch with WPF copy/order, settings persistence and rebind. Clip the letterboxed image/white layer and round the glow using Core `FlashCorners` (7% with the owned switch; 12 DIP glow baseline).
- Apply Core `FlashStayRule.OnScreen` to ordinary spawns: honor the 1..20 slider; ambient staying flashes retain forty (#1452). Update two stale cap expectations without dropping their timing/clickability assertions.
- Make the existing hydra spawn awaitable through its final scheduled callback. Prove real disk decode, parent-screen/motion/lifetime inheritance and queued-second-child cancellation by CloseAll. No replacement media loader or interface.
- Delegate the head's duplicated glow permission/blur limits to `FlashFxRules`. Keep the frozen WPF head unchanged.
- Retain exactly one `feat-flash-exit-stay` ledger row, honestly `stub` with remaining gaps below.

Reference credit: reviewed `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Existing Core rules were introduced by trunk `7271b4ac7a3c4ea8630e367d19e08f55bc4e0944`; compositor behavior is also represented by `70d3fea8b47ea5ae15f3388f14523ffbb717cfad`. This unit adapts WPF behavior into the existing head; it does not overwrite existing files with trunk versions or import additional unused code.

## WPF gap audit

Pinned reference/base: `affe27c56fd7d658cafb8052720bdcbec1816d8a`. The row's historical `Services/Flash/FlashExit.cs` and `FlashStayRule.cs` citations have moved to `CCP.Core/Services/Flash/`; their live WPF callers remain in `ConditioningControlPanel/Services/Flash/FlashService.cs`. Read those rules, the cited FlashFeatureControl XAML, its code-behind and FlashLayer.

| WPF behavior absent or incomplete on this base | Result |
| --- | --- |
| FlashLayer.DrawGlitch: red/cyan ghosts in addition to slices | Implemented and pixel-tested; live BeginExit builds and updates the layer. |
| FlashService lucky glow pulse; MainWindow.Enhancements.OnLuckyProc toast | Implemented through the real Show/Paid path; suppression does not remove XP. |
| FlashFeatureControl RowRoundedCorners; ApplyCornerClip / FlashCorners.Resolve | Implemented through the real spawn and owned settings row. |
| FlashService:1704 ordinary on-screen slider cap; stay exception | Implemented; scheduler/burst/hydra use the shared cap method. |
| Hydra disk-load and queued-CloseAll evidence previously absent | Tested against real temporary images and the existing scheduler. |
| AmbientFxCanvas repeats AllowGlow/MaxGlowBlurRadius (P13) | Head now delegates to Core. WPF's frozen PerformanceProfile/SkillTreeService/FlashService still inline the rules. |
| Natasha's favourite: gated roll, red halo/wash, four-second dodge ring, lifetime boost and delayed booking; click/fling cancels booking | Missing. Core NatashasFavourite exists, but this flash renderer has no cue/dodge booking lifecycle or idle-activity gate. Compare FlashService:1870 onward, StartNatashaDodge and FlashLayer.DrawDodgeRing; trunk `0f88c2dbb6635dadad65bb1bbf9d009cca13e99d`. |
| FlashClicked avatar reaction: one-in-three category pick, enabled/custom phrases, overrides and event audio | Missing. Core's enabled-category helper and tube Giggle now exist, so the old claim that there are no phrase categories is stale. The flash event subscription and complete GiggleFromCategory audio route are still absent. See AvatarTubeWindow.Reactions.OnFlashClicked and Speech.GiggleFromCategory. |
| EmiDesk firstFlashEver after the achievement increment reaches exactly one | Missing; depends on the unported EmiDesk moment bus (`EmiDeskService.Fire`), not its already-ported summon/dismiss service. FlashService:2201; trunk `218c98423`. |
| Flash voice pool, removed/disabled voice filtering, shuffle/variant selection, playback/ducking and authored audio-driven lifetime/XP | Missing; part of the remaining `feat-flash` audio delivery. The ledger's FlashVoicePool name is historical: this pinned WPF tree implements it in FlashService.BuildVoiceLinePool and related playback methods. |
| Remote image source selection/prefetch/reuse and displayed attribution | Missing; part of `feat-flash` source delivery. The historical RemoteFlashSource label maps to WPF RemoteFlashPool and its FlashService callers here. Existing local/content-pack `PackImages` delivery is already wired. |
| Passive Flash XP acceptance | Still blocked by the absent idle tracker/progression acceptance path; the head pays through CoreProgression but ProgressionBank drops passive Flash XP. |
| Gentle visuals override of styled/Shatter exits and minimum fade | Missing; depends on the unported GentleVisuals integration. Motion Off/Reduced behavior in FlashExit itself is already wired. See FlashService.SafeCloseFlashWindow / GentleFadeThenClose. |
| Gaze pop/keep, Pendulum, drag/fling, Jackpot Remix, animated GIF frames and avatar preannouncement displayed by the wider WPF flash rack | Still absent from the wider `feat-flash` renderer. This unit does not imply that the pre-existing gaze switches or other v2 features have become wired. |
| Live multi-monitor/native desktop proof | Not performed. Headless tests exercise lifecycle and geometry, not native input/focus or compositor behavior. |

These are live WPF behaviors, not dead callers. P122 does not apply. This is not a pure dependency NOOP: several user-visible gaps were independent and are fixed here. The remaining feature work exceeds this unit's small-diff scope; the ledger stays `stub`, never `wired` or `verified`.

## Verification limits

All tests use their existing `CCP_USERDATA_DIR` sandboxes; no desktop or real profile is used. `DiffEngine_Disabled=true` was set for test commands.

- `dotnet build <project> -c Release`: Core, Avalonia and all twelve Linux-compatible test csproj files referenced by `.github/workflows/build.yml` passed with zero errors. Existing warnings remain. Builds used `-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true` and discarded completed generated test outputs to fit the runtime disk.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` plus the same hardlink flags: passed, zero errors. WPF/Windows native code was compiled, not run.
- `bash .github/scripts/core-guards.sh`: passed; 429 XAML references checked, three existing API baselines unchanged.
- `dotnet test Tests/CCP.Core.Tests -c Release`: 3,545 passed, zero failed, five existing Vosk tests automatically skipped for the unavailable model. No test was weakened, removed or newly skipped.
- Exact-base regression run: temporarily restored only FlashOverlay and FlashOverlayWindow from the pinned base, ran the two live corner/ghost regressions and five cap cases, then restored the implementation. Expected exit 1: six failures (missing clip, missing ghosts and four ordinary caps), one passing forty-cap exception. Initial test-isolation error was corrected before this evidence run.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~FlashFinishTests'`: 19 passed, zero failed. Includes ownership, settings persistence, pulse timing/stop, ghost pixel colors, toast timing/refresh/suppression/panic, cap and real hydra decode/cancellation.
- Final filtered run: FlashFinishTests, FlashWireTests, FlashShatterWiringTests and PanicSurfacesTests.{EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered,TheOrderingConstraintsHold,EveryStarterIsRegisteredOrAllowlisted}: 39 passed, zero failed.
- Separate FlashPopTests run excluding The_flash_rack_offers_stay_and_the_leave_picker_greyed_with_click: eight passed. The broader combined FlashDrift/FlashPop run encountered the pre-existing Inter glyphTypeface initialization failure and aborted (exit 1); the rack/font-dependent path needs the shared test-font lane or CI. No test source was skipped or changed to hide this limitation.
- `--render-view` for FlashOverlayWindow, FlashFeatureControl, FlashGlitchGhosts and LuckyFlashToast: all four passed. Inspected the generated images. Full render-all, live native overlays, Windows execution and Vosk recognition were not run.
- Initial checkout fetch failed on a read-only snapshot; used the authorized independent fallback clone and verified the exact required base. Disk exhaustion interrupted an initial build sweep/Core invocation; generated-output cleanup and successful reruns resolved it.

## Assumptions

- Preserve the frozen WPF reference and reuse Core rules already on the base; do not import whole trunk files over existing head code.
- Keep the row `stub` while any reachable WPF behavior is missing, even after completing independent visual work.
- Preserve WPF's single reusable toast and hardcoded LUCKY!/Nx XP! copy. The new surface belongs to this flash unit; bubble lucky delivery remains separate.
