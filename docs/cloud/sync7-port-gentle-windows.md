# sync7-port-gentle-windows

- Adapted WPF `46582ed13fdcf20f47f9e354adf0f25f703fb863` (original trunk commit `9637852184b57799ada935de5522876c93d91856`): slower video playback, black-to-video entry, natural-end desktop fades, lock-card fades, stable Chaos bolts/residue, slower impact rise, and suppression of bouncing-text corner rings.
- Read-only source fetch: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Existing files on the base were preserved and behavior merged; no sibling commits imported.
- Pending video fades are registered with `PanicSurfaces` and dropped on idle engine stop. Closed video windows cannot dismiss a replacement clip.
- No sync ledger or parity ledger changes.

## Commit disposition

`46582ed13`: **ported for available surfaces; blocked-by-host for the missing surfaces below**.

| Behavior | Implementation / disposition | Focused test |
| --- | --- | --- |
| Video rate, wall-time length guard and attention spread | `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:135`, `:499`, `:575`; shared rules `CCP.Core/Services/Safety/GentleWindowRules.cs:16` | `GentleWindowRulesTests` |
| Black entry, Playing event and 3-second backstop; natural-end curtain; forced exit immediate | `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:234`, `:858`; finite fade `CCP.Avalonia/Views/Overlays/GentleFade.cs:10`; idle stop `CCP.Core/Services/Video/MandatoryVideoScheduler.cs:415` | `GentleWindowsTests.Video_curtain_survives_natural_teardown_but_idle_engine_stop_and_panic_drop_it`; `GentleWindowRulesTests.SoftExit_OnlyANaturalEndWithGentleOn` |
| Lock-card rise and solved dismissal, panic wins | `CCP.Avalonia/Views/Windows/LockCardWindow.axaml.cs:310`, `:1040` | `GentleWindowsTests.Solved_card_fades_but_panic_closes_immediately_and_old_fade_cannot_close_next_card` |
| Chaos pulse, residue, stable bolts | `CCP.Core/Services/Chaos/ChaosFxEnvelope.cs:53`; `CCP.Core/Services/Chaos/ChaosFieldFxModel.cs:87`; `CCP.Avalonia/Views/Chaos/ChaosEStimOverlay.cs:159` | `GentleChaosWindowsTests`; `GentleWindowsTests.Gentle_bolt_starts_dark_and_rises_without_rejitter` |
| Bouncing-text corner ring | `CCP.Avalonia/Views/Overlays/BouncingTextOverlay.cs:236` | `GentleWindowsTests.Corner_burst_is_suppressed_without_losing_corner_event` |
| Bouncing-text speed and stable color | **already-covered**: `CCP.Core/Services/Subliminal/BouncingTextEngine.cs:145`, `:251`, `:374` | No production change |
| Chaos color flashes | **already-covered**: `CCP.Core/Services/Chaos/ChaosModels.cs:189` | No production change |
| Possession director photosafe and Gentle setting exemption | **already-covered**: `CCP.Core/Services/Possession/PossessionDirector.cs:280`; `PossessionReactiveRules.cs:18` | Existing `PossessionPureRulesTests` |
| Browser player page rate/fade | **already-covered**: `Assets/web/player/player.js:898` | No production change |
| Browser engine load message, URL video and queue timeout extension | **blocked-by-host**: no Avalonia BrowserVideoEngine / VideoService URL-window host or WPF InteractionQueue timeout surface. Existing file-video host adapted; no new browser host invented. | Not executable on this head |
| Possession attachment/remember EmberAttribution photosafe | **blocked-by-host**: EmberAttribution/Ghost charge rendering is absent from the head (`CCP.Avalonia/Possession/PossessionRuntime.cs:18`); Core remember uses a head callback. | No absent renderer invented |

## Assumptions

- Reuse the existing black video window after decoder teardown as the unowned desktop curtain, rather than add an extra native window. If compositing or click-through is unavailable, close immediately.
- Clip-length guards and attention scheduling stretch with rate. User max-duration caps and lost-output watchdogs remain wall-clock limits, as in the WPF change.
- The listed commit slows videos; the later frozen WPF tree changes that policy again. This unit follows `46582ed13` and excludes that sibling change.
- Avalonia lock cards are not pooled and refuse overlapping shows; per-window fade generations and visibility checks prevent stale callbacks from touching a later card.

## Verification limits

- .NET SDK `10.0.402`. The channel installer could not discover a version (403); explicit version installation succeeded.
- `dotnet build -c Release` for Core, Avalonia, and all 12 Linux workflow test projects: all succeeded (exit 0, zero errors) in the final single-node sweep. GeneralSettings initially hit MSBuild `MSB4166`; a single-node retry passed with zero warnings/errors.
- WPF test build attempted with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. It exposed a Core/WPF type collision; the Core rule was moved into the safety namespace, leaving frozen WPF unchanged. Final retry succeeded (exit 0, zero errors; warnings remain). WPF tests were not run on Linux.
- `dotnet test Tests/CCP.Core.Tests -c Release`: 3,969 passed, five existing Vosk skips (model unavailable), zero failures. A repeat reported one failure in the untouched `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` (expected Loops, actual Clips); its isolated class retry passed all nine tests. Final full confirmation passed: 3,969 passed, five Vosk skips, zero failures (exit 0).
- Focused Core Gentle tests: 19 passed. Negative proof: temporarily replacing `GentleVisuals.RiseMs(40)` with `40` in production made the pulse test fail (expected 560 ms, actual 300 ms; exit 1). Restoring the line returned all 19 tests to green (exit 0).
- Headless filter: `GentleWindowsTests|ChaosFxWindowTests|ChaosFieldFxOverlayTests|ChaosEStimOverlayTests|LockCardTypingTests|PanicSurfacesTests|TextOverlayTests` (FullyQualifiedName matching). First run: 27 passed. Repeat: 14 passed, 13 failed after the known `Could not create glyphTypeface` Inter bootstrap error and resulting window-creation failures. This worker cannot provide a stable combined headless gate; supervisor GitHub rcheck is still needed.
- Isolated `GentleWindowsTests`: five passed, including normal fade completion; final isolated repeat passed all five tests (exit 0). Tests use the suite's `CCP_USERDATA_DIR` sandbox and headless windows.
- `bash .github/scripts/core-guards.sh`: passed. `git diff --check`: passed. `test-plan-coverage.sh` is absent on the required base, so that optional gate is unavailable; a numbered manual test step was added.
- Windows platform code is compiled, not run on this Linux worker. Real LibVLC rate/compositor playback, browser-video delivery, and EmberAttribution rendering were not exercised. No Vosk download was attempted (known 403).
