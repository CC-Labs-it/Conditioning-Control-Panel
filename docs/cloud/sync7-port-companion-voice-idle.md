# Companion voice level and engine-off idle lines

- Adapted the requested WPF/trunk commits `c34ce92d2c44385130ac41771d24cc27d7a92511`, `20b2ccce0c284e8da54a91e46ec5df3a62e07314`, `f5455ca20ecd0ffbef67f4c38d1983dc5461e35a`, and `e5830315e3f0d1c3ebc98bfcad791656d3ba8e58`. Fetched `parity/trunk` read-only; no sibling feature commits imported.
- Added the voice slider and engine-off idle opt-in to the existing Companion behaviour cell, preserving WPF order, copy, defaults and toggle polarity. Settings are seeded on reveal/profile replacement and saved on interaction.
- Existing bark/event audio uses Core's voice gain. Held idle lines keep their bubble, never start voice/giggle fallback, and complete the speech callback. The unprompted flag survives the preset queue; the brain echo reports a held idle line as unvoiced.
- No new window/overlay: the existing `tube-speech` PanicSurfaces registration continues to stop the single spoken channel and clear its queue. Existing `TubePanicTests` covers it.

## Per-commit disposition

- `c34ce92d2` — **ported for existing hosts; blocked-by-host for remaining paths**. Slider: `CCP.Avalonia/Views/Controls/Companion/Runtime/WorkshopBehaviorCell.axaml:66`, code-behind `:53`. Bark/event gain: `CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.axaml.cs:1433`. Tests: `CompanionVoiceIdleTests.ControlsSeedAndWriteSettings`, `VoiceGainAndHeldPriorityKeepTheBubbleAndCompleteCallbacks`. Already covered on base: gain math/live CoreSettings reader at `CCP.Core/Services/Companion/CompanionVoiceGain.cs:17` / `:62`, setting at `CCP.Core/Models/AppSettings.cs:6089`, localized strings. Missing hosts: idle voiced-affirmation playback, uninterruptible note clip, 50-click clip, phrase preview playback and voiced-flash playback. The phrase editor and flash renderer exist but do not contain those playback paths; no substitute host added.
- `20b2ccce0` — **ported for existing speech/bark routes; blocked-by-host for automatic idle dispatch**. Pure rule/live Core settings at `CCP.Core/Services/Companion/CompanionIdleVoiceRule.cs:27`; classification at `CCP.Core/Services/Bark/BarkEngine.cs:361`; head propagation at `CCP.Avalonia/Platform/BarkHead.cs:97`; playback choke point at `CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.axaml.cs:1281`; queue at `CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.SpeechQueue.cs:86`. Tests: `CompanionIdleVoiceTests`, `BarkEngineTests.MouthReceivesUnpromptedFlagFromTrigger`, `CompanionVoiceIdleTests` priority and queue tests. Setting already covered at `CCP.Core/Models/AppSettings.cs:6076`. Missing host: tube idle timer/DispatchIdle source (explicitly absent on this base).
- `f5455ca20` — **ported**. Opt-in row at `CCP.Avalonia/Views/Controls/Companion/Runtime/WorkshopBehaviorCell.axaml:129`; seed/save at code-behind `:61` / `:99`. Test: `CompanionVoiceIdleTests.ControlsSeedAndWriteSettings`. Localized strings already covered on base. Settings writes remain in the existing cell rather than adding inert shell forwarders.
- `e5830315e` — **ported for brain echo/short-circuit; blocked-by-host for portrait/heard tracking**. Live policy short-circuits prompted lines at `CCP.Core/Services/Companion/CompanionIdleVoiceRule.cs:38`; existing brain echo delegate seeded at `CCP.Avalonia/Platform/BarkHead.cs:55`. Tests: `CompanionIdleVoiceTests.PromptedLinesNeverReadEngineState`, `CompanionBarkHeadTests.StartSeedsTheSeamsAndTheDoorbellReachesTheEngine`, queue/priority regressions. Already covered: Core brain consumes the voiced delegate at `CCP.Core/Services/Companion/Brain/CompanionBrain.cs:619`. Missing hosts: emotive portrait sequences, Circe talk/reaction playback and DtRH NoteVoicelineHeard; no fake lip-sync or heard counter added.

## Verification limits

- .NET SDK 10.0.100 installed; channel discovery failed with HTTP 403, fixed-version installer succeeded.
- `dotnet test Tests/CCP.Core.Tests -c Release` with `DiffEngine_Disabled=true`: exit 0, 3964 passed, 5 skipped (Vosk model unavailable), 3969 total.
- Mutation proof: temporarily replaced the idle rule's return with `true`; focused `CompanionIdleVoiceTests` failed (exit 1; 1 failed, 8 passed). Restored production rule afterward.
- All Linux CI test projects were built with `dotnet build <project> -c Release`, including Core, Core.Settings, Core.Content, Avalonia, Consent, Language, CatalogueStartup, Migration, Scheduler, Report, GeneralSettings and Snapshot suites: exit 0 / zero errors. Final main head suite build recorded below.
- Focused Avalonia filter: `CompanionVoiceIdleTests|CompanionBarkHeadTests|TubeSpeechQueueTests|TubePanicTests`: compiled, attempted execution, exit 1 (4 passed, 8 failed). Window tests fail in environment font creation (`Could not create glyphTypeface`, Inter/Segoe UI Black) before behavior assertions. No passing runtime claim for those tests; run on the supervisor's configured GitHub runner. A duplicate concurrent build attempt also hit an intermediate DLL file lock; subsequent checks are sequential.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. The base has no `.github/scripts/test-plan-coverage.sh`; added numbered WPF/Rows tester steps directly, without editing sync ledgers or parity ledger.
- Windows desktop/audio behavior, actual sound output and missing hosts listed above were not run. Headless tests use the existing isolated test profile and injected audio providers.

- Final `dotnet build CCP.Avalonia -c Release`: exit 0, 0 warnings, 0 errors. Final `dotnet build Tests/CCP.Avalonia.Tests -c Release`: exit 0, 0 warnings, 0 errors.
- Final full Core suite after restoring the rule and renaming it: exit 0, 3964 passed / 5 Vosk skips / 0 failed; focused restored idle/bark tests also passed (17/17).
- WPF test cross-build: `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings, 0 errors. Compiled only; Windows tests were not run. Initial CS0433 collision was resolved by the Core type name described below.

## Assumptions

- Preserve the policy at the four requested revisions; do not import later sibling panic/session-pause policy changes from WPF HEAD.
- CoreSession engine/session providers replace WPF EngineOffAudioLog's combined running-state query; Serilog records held idle lines because that trace service has no Core/head host.
- Keep the head's existing ordinary-voiced-bark routing and text-only delayed preset behavior; carry only the new unprompted flag through it.

- Compatibility decision: Core names the adapted policy `CompanionIdleVoiceRule` to avoid CS0433 with the frozen WPF `CompanionIdleVoice` type in dual-reference WPF tests.
