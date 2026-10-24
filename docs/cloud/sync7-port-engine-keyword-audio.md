# sync7-port-engine-keyword-audio

- Adapted WPF `42c1696a191b01fdd3d2e9d8a2cde170ef28c17e` engine-off audio diagnostics to CoreSession + Serilog. Successful Avalonia one-shot, Mind Wipe single/overlap-loop and mandatory-video starts report the player and filename, once per player per ten seconds. Running engines/sessions and explicitly requested sounds are excluded by the shared rule. Both separator styles are stripped on Linux.
- No new surfaces; existing playback lifecycle and PanicSurfaces registrations remain in use. No ledger or parity document changes.
- Reference `parity/trunk` fetched read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; the diagnostic is adapted directly from the listed WPF commit, with unused keyword-only helpers omitted.

## Commit disposition

| WPF commit | Disposition and evidence | Test |
| --- | --- | --- |
| `42c1696a1` | **Ported, available players:** `CCP.Core/CoreEngineOffAudioLog.cs:21` (Core session state), `:31` (Serilog), `:48` (throttle), `:64` (private filename). `CCP.Avalonia/Platform/LibVlcAudio.cs:126` (one-shot), `:186` (Mind Wipe single/loop), `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:202` (video). | `EngineOffAudioTests`: state combinations, filename privacy, throttle/rollback, asked-for exemption, actual Serilog emission. |
| `42c1696a1` | **Blocked-by-host, keyword guard:** `CCP.Avalonia/Platform/KeywordTriggerHead.cs:3` is a demo-only stub. There is no keyword action dispatcher, legacy keyword clip player, keyword Mind Wipe dispatch, or keyword preview player to guard. **Blocked-by-host, remaining named players:** Brain Drain audio service is absent (`CCP.Avalonia/Views/Controls/Studio/BrainDrainFeatureControl.axaml.cs:19`); dedicated Flash whisper, ambient sound scheduler and Mantra chant player are absent. Existing generic one-shots receive their `oneshot:<tag>` diagnostic. | No speculative host imported or test-only guard added. |
| `4bcb62934` | **Already-covered, stay ceiling:** Core `CCP.Core/Services/Flash/FlashStayRule.cs:43` exempts stay flashes from the slider. Avalonia `CCP.Avalonia/Views/Overlays/FlashOverlay.cs:368` already computes a 40 shared-host stay cap without trimming it. No change. | Existing `FlashPopTests.Stay_until_popped_sets_the_safety_lifetime_and_cap_for_ambient_clickable_flashes_only` and `A_full_screen_of_staying_flashes_skips_the_ambient_tick`. |
| `4bcb62934` | **Blocked-by-host, bubble single flash:** `CCP.Avalonia/App.axaml.cs:457` seeds a logging-only `EffectPayload.Firer`; no bubble payload effect runner exists. Core `CCP.Core/Services/Chaos/EffectPayload.cs:100` still has the full-burst resolver. Do not change dormant shared behavior ahead of the host or re-port sibling revert `1feb49131`. **Blocked-by-host, OCR regex:** no keyword OCR scanning/dispatch loop or hit/highlight surface exists; keyword head is the same stub cited above. | No new surface invented. |

## Verification limits

- The conditional WPF test build initially found a same-name public diagnostic type collision (exit 1, CS0433); renamed the portable type to `CoreEngineOffAudioLog` without changing the frozen WPF files.
- SDK: `10.0.402` (channel installer discovery failed; direct version install succeeded).
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia and all twelve Linux-compatible test projects referenced by `.github/workflows/build.yml` passed, exit 0, zero errors (existing warnings).
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: final exit 0, 557 warnings, 0 errors. Compiled only; Windows code was not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. An initial invocation in the read-only sandbox could not create its temporary directory; rerunning with writable temporary storage passed.
- `test-plan-coverage.sh` is absent on this base (exit 127); no ledger rows were added or promoted.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~EngineOffAudioTests`: exit 0, 11 passed. Replacing the production `if (line != null)` emission with `if (false)` produced exit 1, 1 failed / 10 passed (`Assert.Single`, empty log sink). Production line restored; after the portable-type rename all eleven focused tests passed again (exit 0).
- Full `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1, 3963 passed, 5 skipped (Vosk), 1 failed: `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` expected Loops, got Clips. Focused rerun of that test plus all eleven new tests: exit 0, 12 passed; the failure reproduces in a second final full-suite run with the same totals, but passes in isolation; its media-selection code is outside this diff.
- A relevant Avalonia filtered test command was stopped during compilation, before test execution, to honor the supplied compile-only font exception. No Avalonia test result is claimed; the supervisor must run the relevant CoreAudioTests, MindWipeTests and FlashPopTests on GitHub. Native audio/video playback and Windows execution cannot be exercised here. Vosk model download is blocked by the supplied environment policy; no model fetch attempted.

## Assumptions

- Missing hosts are blockers for the corresponding sub-behaviors, not authorization to import sibling feature engines.
- Serilog's `[VideoDiag AUDIO]` marker is the portable equivalent of the WPF VideoDiag AUDIO sink.
- Generic Avalonia one-shot users retain their existing tags; standalone WPF-only player categories are not invented.
