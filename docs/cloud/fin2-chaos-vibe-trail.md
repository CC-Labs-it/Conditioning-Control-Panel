# fin2-chaos-vibe-trail: dependency-only audit

- Audited the existing trail against WPF at base `a65dbbe2f4935a4797b32efd87513054dd39e1d2`; no application code changed.
- Kept `chaos-vibe-trail` **stub** and replaced repeated historical ledger prose with the remaining dependencies. The old assertion that the entire bubble field is absent is stale.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No code imported; no trunk implementation credit applies. Its WPF trail last changed at `89e13b45adedd56128e2b7c0d55cf082137b9582`.

## WPF behaviour and remaining gaps

All source locations below refer to the pinned base above.

| WPF behaviour | Existing Avalonia implementation / missing dependency |
| --- | --- |
| One hidden keep-alive stage window, click-through/no activation, primary or multi-monitor bounds | Window and stage sizing exist. WPF pre-creates/realizes it when VibePopping is equipped (`ChaosModeService.cs:454-455`); head has no EnsureCreated caller. Birth belongs to the unported VibePopping effect lifecycle. |
| Show at buzz start, snap to pointer, raise, start following | Overlay Start/BeginFollow exist. WPF UseToy `:2697-2708` starts the actual sweep, sets duration to max(1, power), resets afterglow, marks active, plays buzz and shows banner before its feed line. Head UseToy only calls ChaosToyRules.Use (`ChaosRunHost.cs:544-553`). Missing owner: VibePopping toy effect. |
| Hold-and-sweep pops; capstone hover pops | WPF BubbleService.SetVibePop `:263-271`, pointer sampling `:546`, bubble sweep `:4329` supply the effect being telegraphed. No SetVibePop/VibePop equivalent exists in Avalonia BubbleOverlay or Core AmbientBubbleField. The bubble field is present; this mode is unported. |
| Trail remains through one Afterglow interval, then stops/hides, retaining window | WPF TickActiveToys `:2924-2947` uses real time, extends once by AfterglowSec, enables hover popping and stops trail/banner only after that interval. Head TickToys `:519-526` drops every toy's active flag next tick; no buzz/afterglow clock or Stop caller. This must follow the effect owner rather than a cosmetic cooldown timer. |
| Re-raise above mandatory video while descending and pinning is enabled | WPF RaiseGameLayerAboveVideo `:745-774` includes trail `:762`; VideoService `:5821` and run tick `:951` reach it. No equivalent gameplay-band restacker exists in ChaosRunHost or MandatoryVideoOverlay. Missing dependency: chaos video restacking. WPF callers are live, so P122 does not apply. |
| Desktop pointer tracking, physical pixels to window-local DIPs | Head uses shared X11Pointer. It returns null outside Linux/X11; Windows and native Wayland need the shared desktop-pointer feature before the trail can follow there. X11Overlay already routes window operations to Win32; pointer input does not. |
| Warm 58 DIP glow, 14 amber 20 DIP dots below it; emit at 9 DIP; linear 340 ms fade 0.5 to 0 and shrink 1 to 0.35; sine 0.9 to 1.12 over 180 ms autoreverse; 16 ms following | Already in ChaosVibeTrail and ChaosVibeTrailOverlay, with focused Core/headless tests. No new visual behaviour needed for this row. Head deliberately stops the timer while hidden. |
| Run end/shutdown/panic clears and closes window | Existing run cleanup/EndRun calls plus PanicSurfaces `chaos-vibe-trail`; existing panic test. No new surface or panic registration required. |

Reproduction searches: `rg -n 'ChaosVibeTrailOverlay\.' ConditioningControlPanel/Services/Chaos/ChaosModeService.cs CCP.Avalonia`; `rg -n 'SetVibePop|VibePop|RaiseGameLayerAboveVideo' CCP.Avalonia CCP.Core`; inspect ChaosRunHost.UseToy/TickToys and BubbleService sweep above. Render-all discovers parameterless Controls by reflection in RenderProof.Views; the trail has a parameterless constructor and PaintSample.

## Assumptions

- Treat VibePopping's actual sweep/hover/afterglow lifecycle, shared desktop-pointer support and chaos-video restacking as dependencies, per the unit's explicit dependency-only rule. Starting a decorative trail on cooldown alone would misrepresent WPF's effect lifetime.
- Keep the ledger stub: WPF callers are live and the real user path still cannot reach the trail behaviour. No P122 exemption or verified claim.

## Verification limits

No application or test code changed, so no new fail-before-fix test is applicable. Commands used SDK 10.0.402 and Release configuration; test commands exported `DiffEngine_Disabled=true`.

| Command | Exit / result tail |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 errors (48 warnings) |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors (192 warnings) |
| `dotnet build <test.csproj> -c Release` for every Linux test project in build.yml | 0 each; 0 errors. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests; CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests. Initial native-asset copies exhausted disk; removed generated test outputs and retried affected builds successfully one at a time. |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0; 0 errors (554 warnings); compiled only, Windows runtime unavailable. |
| `bash .github/scripts/core-guards.sh` | 0; All Core guards passed. Initial read-only sandbox attempt could not create temporary files; rerun with writable temporary storage passed. |
| `dotnet test Tests/CCP.Core.Tests -c Release` | 1; 3952 passed, 1 failed, 5 skipped, 3958 total. Unrelated existing DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls failed at line 185 (expected Loops, actual Clips). Speech/Vosk tests skipped because the external model is unavailable; no tests modified or disabled. |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~ChaosVibeTrailTests` | 0; 4 passed, 0 failed, 0 skipped. |
| `git diff --check` | 0 |

Avalonia ChaosVibeTrailOverlayTests were compiled, not executed, under the task's measured cloud font-creation limitation (the shared test dispatcher has no font bootstrap). Supervisor/rcheck must run them. No live render-all, desktop input/composition/stacking checks, Windows execution or native Wayland checks were run. Existing Windows interop was compiled as part of the Avalonia build but cannot be run here.

The default SDK installer queried a denied CI metadata endpoint; installation succeeded using its supported builds release feed. The supplied checkout was read-only and had the wrong HEAD; used the requested fallback clone, with existing read-only snapshot Git objects as a cache, and verified the exact pinned commit before editing. No local paths or credentials are included in the committed files.
