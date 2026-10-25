# fin4-shell-quests

- Restore the completion voice caller using the existing CoreAudio stoppable backend. Adapt FlashVoicePool from read-only parity/trunk commit `80857b2b2d0c2d370747194ff845765ccad71f43`; retain only called pool/volume rules. Existing base files win; no trunk files overwrite them.
- Preserve WPF completion semantics: perk-announcement opt-out gates voice/popups; ambient flash-audio and avatar mute switches do not gate PlayRandomSound; master zero stays zero. New celebrations replace the prior voice. Panic, Stop everything and exit stop playback.
- Restore the header quest host: three daily seats and weekly seat, live progress/completion/roll refresh, empty slots, cached mod-aware/remote art, language/mod updates, localized detail copy, XP and bonuses, remaining/done, hover transitions, completion pop and navigation. Popup closes on move, deactivation, leave, navigation, panic and close. Subscriptions and finite motion timers are released on close.
- Use the existing ModArt and Fluent icon map, finite UI-thread transform tweens, and a transient gold border instead of an animated shadow Effect. Keep the pinned header height and overlapping stamp geometry.
- Add focused Core voice tests, headless shell roster/details/completion tests, ledger updates and numbered manual tester steps.

## WPF gap analysis at the required base

At `983315dd6a755fdd714463f9ac277c99f9800886`, MainWindow.Quests.cs had two missing caller groups: PlayRandomSound and RefreshQuestStamps. Banner, popup, five-second dismissal, quest repaint, event burst, logging and profile sync already had live paths. The stamp file had only unreachable navigation and empty hover handlers. Its missing behavior comprised roster/ghost rendering, progress and completed state, art resolution, service/language/mod subscriptions, hover zoom/details, scaled reward and remaining copy, transition-only completion pop, hover retention across repaint, deferred leave, popup dismissal and teardown. This unit restores these paths; neither ledger row is marked verified.

## Assumptions

- Port the celebration audio slice of FlashService, rather than its unrelated ambient image host. The base already supplies CompanionContentResolver, phrase IDs, settings and CoreAudio.
- Use existing Fluent mappings for quest icon fallbacks. Keep Avalonia popup composition native; exact visual equality requires manual review.
- Use .NET SDK 10.0.100 after the channel installer endpoint returned HTTP 403.

## Verification limits

Verification results are recorded below after completion. Windows desktop behavior cannot be executed on this Linux worker. No PR is opened.

- `dotnet test Tests/CCP.Core.Tests -c Release` (DiffEngine_Disabled=true): exit 0; 2,820 passed, 5 skipped, 0 failed. The five skips are the unavailable Vosk model; no tests were weakened or removed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~FlashCelebrationVoiceTests`: exit 0; 7 passed, 0 failed.
- `bash .github/scripts/core-guards.sh`: exit 0; all Core guards passed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~QuestShellCompletionTests|FullyQualifiedName~QuestsTabFxTests|FullyQualifiedName~QuestsTabLiveTests'` (DiffEngine_Disabled=true): compiled, then exit 1; all 9 cases failed during font creation (`Could not create glyphTypeface`, Segoe UI Black), before behavior assertions. UI behavior therefore remains unvalidated here and needs the font-enabled runner. The pre-caller regression attempt hit concurrent build output contention, so it is not claimed as a fail-proof.
- `dotnet build -c Release` succeeded with zero errors for Core, Avalonia and every Linux test project in build.yml: CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests, CCP.Avalonia.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests and CCP.Core.Tests. Final verification builds ran serially after resolving output-file contention.
- `git diff --check`: exit 0. Test-plan coverage script is absent from this base, so that script could not be run; numbered coverage steps are supplied in docs/avalonia-test-plan.md.
- No live desktop, audio-device, Windows runtime or manual visual check was performed. WPF build result follows below.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; 0 errors (512 existing warnings). WPF and its tests compiled on Linux but were not run.
- Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release -m:1 -nr:false -p:UseSharedCompilation=false`: exit 0; 224 warnings, 0 errors. This includes the final WPF hover timing adjustment. The single-worker retry followed an MSBuild child-node crash; no source or tests were weakened to recover it.
