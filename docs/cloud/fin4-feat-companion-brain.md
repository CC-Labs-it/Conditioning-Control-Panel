# Companion brain host wiring

- Base: `df5a59167c58ab3735c2a5295db3b3b7243af475`; branch `avalonia-port/cloud-fin4-feat-companion-brain`.
- Session asks now enumerate the same available catalogue shown by the Sessions rack, keep its order and mode-aware labels, recheck availability, and enter the existing start confirmation path. Reuses the billboard's launcher; no new launch surface or panic registration is needed.
- Ask busy checks now include the actual session runner, startup quiet, mantra, connected remote controller and video grace pause, alongside the existing video, lockdown, lock-card, bubble-count and quiz checks.
- User-message emits now credit the WPF first-message and pillow-talk achievements on the UI thread while preserving send-time memory/bark ordering. Persistent conversation recall raises the existing provider signal and credits the entitled recall achievement.
- A successfully started one-shot mind-wipe clip records the existing memory favourite signal. Missing/failed clips do not count; memory's existing cooldown and storage moderation remain unchanged. Continuous loops do not emit this event in WPF.
- Replaced the repeated historical ledger narrative with one honest `stub` row and added four numbered manual tester steps. No imported trunk code: `parity/trunk` was fetched read-only, but all implementation reuses this base and its WPF reference.

## WPF comparison and remaining scope

Reference pinned at the base SHA above: `App.xaml.cs` (`SeedAskSeams`, `WireMemorySignalSources`, brain construction), `MainWindow/MainWindow.CompanionAsks.cs`, `Services/GamificationBridge.cs`, `Services/LockCard/MindWipeService.cs`, `App.AiEffects.cs`, and `Services/Companion/CompanionIdleVoice.cs`. The row's canonical brain, memory, persistence, prompt, recall, maintenance, delivery and relationship implementations already live in Core; their head seams are the comparison points, rather than duplicating those implementations.

Gaps found before this slice:

1. Session options/start delegates unseeded despite an existing session host: addressed.
2. Busy gate omitted the real runner, startup quiet, mantra, controller and grace pause: addressed. Embedded browser playback/takeover remains absent.
3. Mind-wipe favourite omitted despite an existing player: addressed for WPF's one-shot event.
4. Brain-drain favourite: still missing. The head has a feature editor, not the WPF playback/capture service.
5. Message achievements and persistent-recall signal/achievement: addressed. WPF's historical achievement-count backfill remains part of the unported GamificationBridge host; this slice counts new sends only.
6. Possession prompt context (haunting rung and escape attempts): still missing. Core has a director, but the Avalonia runtime explicitly lacks the shell host/lifecycle, pointer, scenes and tripwires; reading its default state would pretend to provide context.
7. Unprompted idle voice policy and corresponding bark-echo voiced flag: still missing; this head's bark mouth has no trigger-aware idle-voice gate. Bark echo itself is already attached by `Platform/BarkHead.Start` on this base.
8. AI spiral surface and WPF overlay bypass semantics: still missing. There is now a spiral overlay, but simply seeding `Refresh` would falsely report success during asynchronous decode and still omit WPF's engine-off/level bypass. Pink likewise retains its documented engine-off refusal.
9. Embedded browser/media AI routing and playback/takeover busy checks: still missing; WPF has a remote-guarded browser media route, whereas this head uses external companion links.
10. Awareness-v2 reaction callers: still missing; the head has legacy awareness, not the observer frames those callers consume.
11. Historical notes claiming no Live actions panel are obsolete: this base already has `App.AiLiveActions` and `CompanionEffects.Seed` seeds its sink. Legacy-local-history clearing, effect cancellation, activity offers, moderation, memory maintenance and bark echoes are also present; they were preserved.

The remaining hosts cannot all fit this unit's 1000-added-line cap: the WPF brain-drain service is 497 lines, shell possession host 704, and awareness routing/speech/arbiter 763, before adapting platform dependencies, tests or host composition. The row remains `stub`; no dead-WPF/P122 claim or `verified` promotion is made. Assumption: prioritize existing host connections and keep overlay control/idle voice with their own host work rather than introducing partial substitutes.

## Verification limits

Windows-targeted code can only be compiled here; no real desktop, notifications, account, AI backend, audio device, remote controller or possession session is exercised. Headless tests use the repository's sandboxed profile/dispatcher. No new desktop surface was introduced; existing session and mind-wipe panic teardown is reused.

- SDK: .NET 10.0.402.
- `dotnet build -c Release`: Core, Avalonia, and all 12 Linux test projects named by the workflow passed, each with zero errors. Existing warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; the script is absent on this base. Four explicit numbered WPF/row test steps were added and the ledger contains exactly one row for this id.
- Negative proof: removing the companion-message increment makes the new threshold test fail (expected 1, actual 0; exit 1). The increment was restored.
- Relevant headless classes: exit 1, 9 passed / 5 failed. Four failures explicitly report inability to create the Inter glyph typeface; the fifth is the existing lock-card busy assertion (possibly downstream of font failure; unresolved here). No tests were weakened or removed. These window paths still need the supervisor's font-equipped runner.
- Focused new host/memory coverage plus the existing memory-seed test: exit 0, 6 passed / 0 failed (`CompanionBrainHostTests`, `PlayedClipFeedsMemoryButFailedPlaybackDoesNot`, `MemorySignalSeedTests`).
- Initial full Core run: exit 1, 4117 passed / 1 failed / 5 skipped. The failure was the unrelated board frame-budget timing test (42.88 ms) during simultaneous compilation; the Vosk skips are the suite's existing unavailable-model behavior. A full rerun after builds is recorded below.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 559 warnings / 0 errors. WPF and its tests compiled, but were not run on Linux.
- Full Core rerun after builds: exit 1, 4117 passed / 1 failed / 5 skipped. The same board frame-budget test measured 16.37 ms against its 12 ms ceiling. No board code or test was changed.
- Isolated `AchievementEngineTests` plus `A_frame_fits_the_budget`: exit 0, 17 passed / 0 failed. This confirms the new milestone test and the board test pass in isolation; it does not erase the full-suite timing failure.
- `git diff --check`: exit 0. No Windows runtime or real-desktop behavior was run. Remaining host dependencies and the unresolved full-suite/window validation limits prevent a wired/verified claim.
