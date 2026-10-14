# wire-shell-keyword-triggers

- Branch: `avalonia-port/cloud-wire-shell-keyword-triggers`; base: `avalonia-port/goongame-core-c` (`0e1aa0473e7a5b6120544923e94bb04cc62a7e50`).
- Added-line count against that base: **670**, below the 1000-line cap.
- Extracted WPF rolling typed buffer, whole-word/regex matching, preset-first collection, app-scope policy, mute expiry and audio volume curve into Core. WPF calls those same implementations.
- Awareness master/keyboard switches now arm the Windows keyboard/audio slice through the existing panic hook, retaining its panic binding. Typed text stays in memory; blocked app focus drops the partial buffer. Sandbox profiles never install the hook.
- Configured trigger audio uses the existing CoreAudio/LibVLC sink, first preset audio wins, repeats cancel, and panic stops the source/clip and restores ducking. Queued key work is invalidated on stop. Added focused Core and headless tests; headless fixtures use the production bundled-font setup.
- Read-only source review: `parity/trunk` commit `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk files were copied. WPF source is the behavior reference.
- The remaining runtime exceeds the 1000-added-line cap. Row stays **stub** with follow-up **wire-shell-keyword-triggers-b**: Linux input source, clipboard/OCR scanner + confirmation, highlights/capture visibility, all non-audio actions, trigger-fired/pulse/recent-app feeds, full multi-action dispatch and source/entitlement lifecycle. Audio-only matching currently consumes cooldowns even when a trigger has only deferred actions.

## Verification limits

Commands ran with .NET SDK 10.0.402 in Debug, `DiffEngine_Disabled=true`, `CCP_REQUIRE_VOSK=1`, and the downloaded small English Vosk model. A local, uncommitted wrapper limited validation to two concurrent commands and added `-m:1 -p:UseSharedCompilation=false -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=false -p:CreateHardLinksForCopyLocalIfPossible=false` to builds/tests.

- `dotnet build CCP.Core/CCP.Core.csproj --nologo -v:minimal`: exit 0, 48 warnings, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj --nologo -v:minimal`: exit 0, 172 warnings, 0 errors. Normal-copy output was rebuilt after discarding earlier invalid generated artifacts.
- Workflow Linux test-project builds: CatalogueStartup, Consent, Language, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content.Tests, Core.Settings.Tests and Core.Tests all exited 0. GeneralSettings initially failed copying assets with ENOSPC; its retry exited 0 with 0 warnings and 0 errors in 6.38s. All 13 required Linux project builds (two heads and eleven test projects) have successful final results.
- `dotnet test Tests/CCP.Core.Tests --no-restore --nologo -v:minimal`: **2830 passed, 1 skipped, 0 failed**, 2831 total, 1m24s. The existing `BoardTilesTests.A_frame_fits_the_budget` selected its existing runtime skip; no test was skipped or weakened by this change. The 15 new keyword Core cases ran in this suite.
- `dotnet test Tests/CCP.Avalonia.Tests --no-restore --nologo -v:minimal --filter 'FullyQualifiedName~KeywordAudioRuntimeTests|FullyQualifiedName~KeywordTriggersPanelTests|FullyQualifiedName~PanicSurfacesTests'`: **14 passed, 0 skipped, 0 failed**, 16s. Six new runtime cases cover audio playback/volume, entitlement/master gates and preset precedence, repeat cancellation, cooldown/mutes, focus isolation, and the UI-to-panic path.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed` (424 XAML references, 1591 app types, 1884 Core types).
- `git diff --cached --check`: exit 0.
- Optional `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false --nologo -v:minimal`: **failed**, 411 warnings and 16 MSB3021/MSB3027 asset/localization copy errors because the shared 32GB filesystem filled, after 8m28s. This is not a successful WPF build, and WPF tests were not run. Its generated outputs were removed after the command exited.

Earlier overloaded/space-exhausted build attempts and a generated-output hardlink experiment were discarded; only normal-copy builds and the actual test runs above count. Completed generated `bin` directories were pruned between checks to stay within disk limits; source assets and test sources were retained. The focused test output temporarily used a scratch-directory symlink and was removed after its successful run; no output-path configuration was committed.

No visual layout changed, so no new render PNG is claimed. Windows hook/ToUnicodeEx/foreground resolution cannot run in this Linux container. No real desktop, user profile, microphone, or hardware was used by tests.

Git transport returned HTTP 403 for the configured ClearlyNotMich connection. The connected Personal account (ObviouslyNotMich) has repository write access. Claim and final fast-forward publication use GitHub commit/tree/ref APIs on this cloud branch only; no PR, merge, force update, or write to the queue branch.
