# fin5-feat-flash-exit-stay

- Add Glitch's red/cyan modulated image ghosts under the seven slices, matching WPF `Services/Compositor/FlashLayer.cs:DrawGlitch`: half alpha, opposite 4%-width offsets, shown above a half physical pixel. Cache two immutable tinted pictures per leave; dispose them with the flash. The existing `flash` panic surface closes the layers.
- Restore the black backing on plain cards; its Border follows the picture's rounded corner setting.
- Forward lucky bubble rolls to the existing reused lucky toast with the mod-aware bubble label and 20x multiplier. Match WPF `SkillTreeService.RollLuckyBubble`: announce before applying the daily XP budget, even when the budget pays zero. No extra lucky roll.
- Add three regression tests and numbered manual tester steps. Keep the single parity row `stub` with explicit remaining gaps.
- Base and WPF reference pinned at `35b3144821459a729320205cd2cb2bb11d7f2f21`. Fetched and inspected read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported from it in this unit.

## Gap audit and scope

The row's pure `FlashExit`/`FlashStayRule` sources already live in Core; the WPF head uses the same rules. The feature rack already exposes click, stay and all leave choices in WPF order. This unit preserves those paths.

| Remaining WPF behavior | Evidence and required host work |
| --- | --- |
| Natasha's favourite | `FlashService.cs:1865-1882, 2808-2852`: eligible original/clickable/active-user roll, red halo/wash, 4-second draining ring, minimum lifetime, click/fling cancellation, location-aware unprompted Chaster booking. Core Chaster rules exist, but this flash host lacks the idle-aware eligibility and dodge lifecycle. |
| FlashClicked avatar reaction | `FlashService.OnFlashClicked`, `AvatarTubeWindow.Reactions.cs:OnFlashClicked`: FlashClicked event and 10% category giggle through the companion phrase selection path. Avalonia has generic speech, but no category reaction path to call. |
| firstFlashEver | `FlashService.cs:2196-2206`: after TrackFlashImage increments to exactly one, call EmiDesk.Fire. Avalonia EmiDeskService explicitly ports summon/dismiss only and has no Fire/moment bus. |
| Voice pool/audio/ducking | `FlashService.BuildVoiceLinePool`, flash sound selection/playback: phrase-library pool, mute filtering, sound-length lifetime and playback/duck ownership. Existing Avalonia flash XP intentionally assumes no sound. |
| Remote flash pool | `FlashService.cs:3835-4090`: channel selection, fetch coordinator, ready queue and remote media load path; requires media delivery integration, not just importing an unused pool helper. |
| Shared rule delegation debt | WPF FlashService/PerformanceProfile/SkillTreeService inline rules and AmbientFxCanvas.Env glow gating remain as recorded on the base. WPF is frozen reference for this task. |
| Hydra and native proof | Existing child disk loading and queued CloseAll need integration proof. Native multi-monitor behavior needs an actual desktop. |

These are live WPF paths, so P122 does not apply. The voice/remote delivery hosts plus EmiDesk moment bus and companion category reaction pipeline cannot all fit in the 1000-added-line unit; the self-contained visual gaps and existing-toast caller were prioritized. No placeholder hosts or unused imported helpers were added.

## Verification limits

- `dotnet build <project> -c Release`: exit 0, zero errors for CCP.Core, CCP.Avalonia and all 12 test projects (CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot, Avalonia, Core.Content, Core.Settings, Core, and WPF). The WPF test build used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; warnings remain on the base. WPF compiled but was not run.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.” The test-plan coverage script is absent on this base; manual steps were added regardless.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 2850 passed, 5 existing Vosk tests skipped, 0 failed. The Vosk model is unavailable here; no tests were edited or disabled to obtain this result.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~FlashPopTests|FullyQualifiedName~FlashLuckyTests'`: exit 0; 15 passed, 0 skipped/failed.
- Same command with filter `FullyQualifiedName~FlashWireTests|FullyQualifiedName~FlashShatterWiringTests`: exit 0; 17 passed, 0 skipped/failed.
- The broader five-class selection hit a fatal Inter glyphTypeface creation error when FlashDriftTests initialized Avalonia without AppFonts. Stopped the process after the fatal error; its launcher reported exit 0 without a completed test summary, so this is explicitly NOT a green result. Run that class on the supervisor's supported runner. The four font-configured classes above completed in isolated processes; no assertions were changed to accommodate the environment.
- Before the fixes, the three new regressions ran to completion and failed (exit 1): plain backing was absent (`Single` found no black Border), Glitch had no ghost pair (expected 2, actual 1), and lucky bubbles emitted no toast callback (empty collection). After the fixes all three pass as part of the 15-test selection. The Glitch test additionally checks the actual red/cyan pixels, half alpha, opposite offsets and panic cleanup.
- `git diff --check`: exit 0. No live desktop, native multi-monitor, Windows runtime or render-all run was performed. Glitch caches two tinted bitmaps per leave; desktop stress behavior remains unmeasured.

## Assumptions

- Preserve the base's partial integrations and `stub` status; remaining host work is explicit rather than claiming full parity.
- Bake two tinted pictures once per Glitch leave on the UI thread; this avoids a new renderer abstraction, effects and mutable cross-thread SKBitmap publication.
- Pin the SDK installer when the channel discovery endpoint returns 403.
