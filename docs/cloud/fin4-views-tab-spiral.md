# fin4-views-tab-spiral

- Restore the Spiral room's fog-entry swell and splash chime using the WPF candidate order, mod override resolution, countdown-audio/master gates, 0.42/0.34 mix, and CoreAudio playback. Missing assets and playback errors remain silent and cannot break navigation.
- Spend the two sound latches once per visible tab entry, reset only on Suspend. Fuse ticks, phase changes, block repaints and embed retries cannot repeat a cue.
- Match WPF explicit same-tab failure retry without spending a second sound, and cap Spiral ambient motion on the Windows animation setting or GentleVisuals through a small head-only user32 seam.
- Add focused tests for ordering, fallback, mix, mute/disabled audio, missing/throwing resolvers, repaint deduplication and re-entry. The entry test fails against the original view (expected fog cue; actual empty), then passes with the fix. Restoring the old OS/gentle gate and same-tab retry behavior produces three additional expected failures (ambient motion stays enabled twice; browser attempts stay at one instead of two).
- Keep the single ledger row at `stub` with its actual remaining dependencies and add a numbered WPF-linked tester step. No new window/surface was added; the existing shell panic registration is unchanged.
- No trunk code imported. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation adapted from the frozen WPF reference on base `3b0666b52be8e51ea6fea3b3a4cc72845b174462`.

## WPF gap audit and scope

References read: SpiralTabView.xaml and .xaml.cs, DescentRoomSfx, DescentService, DescentMigrationService, DescentShowDirector, DescentBarkWatcher, MotionFx, and the existing Avalonia/Core implementations and tests.

1. Live Spiral state: Avalonia Refresh still supplies `hasBlock: false` and false live-offer/ceremony inputs. WPF reads DescentService.Current and DescentMigrationService.SpiralWithheld. Persisted migration gates and countdown are already shared and observed. Missing: authenticated block acquisition, account-generation reset, refresh throttling/deferred retry, post-sync/startup polling and UI-thread BlockChanged delivery, plus live migration offer/ack ownership. These must be ported together, rather than inventing a block from local XP.
2. Live embed: WPF posts Current on construction and subsequent BlockChanged events; Avalonia still posts null. Navigation/failure/watchdog/fallback and stale-callback protection exist and have headless tests, but a user cannot reach them with a real server block. Native browser rendering/process-loss proof remains outside these tests.
3. FIRST LIGHT: WPF has a reduced/full reveal snapshot, handoff hold, 12-second initial-block wait, null-block race handling and cancellation on leave. The canvas/timeline are already ported; the director handoff is absent. This is dead in the shipped WPF user path: at the pinned base, `rg -n 'RunFirstLightReveal\(|BeginSpiralFirstLight\(|BeginFirstLight\(' ConditioningControlPanel --glob '*.cs'` finds only the RunFirstLightReveal definition, its call to main.BeginSpiralFirstLight, that definition/call to the view, and the view definition. DescentShowDirector explicitly documents the capture rig as the only caller since ceremony retirement. No P122 wired claim is made for the entire row because the other live gaps remain.
4. Entry audio: missing fog/splash cues are implemented here. WPF's crack cue belongs to the zero-show surface, not this tab. Splash audio is tested through the real tab's ApplyState path with a fake browser starter; its live user path awaits item 1.
5. Entry bark: WPF AnnounceEntry only calls NotifySpiralOpened after resolving Spiral; BarkService owns the once-ever lifetime rule and uses the real block as context. Avalonia CoreBark has no Descent entry dispatch and no real block host. Missing: that host/context and bark notification; do not spend a welcome while displaying fog.
6. Feature intro: already implemented in MainShellWindow.TabNavigation (`descent-spiral`, only when IsShowingSpiral). No duplicate caller added. User reachability depends on item 1.
7. Motion: WPF MotionFx caps Full to Reduced on Windows ClientAreaAnimation=false (also GentleVisuals). Avalonia ambient Env currently reads the raw setting. This cap is implemented for this tab through ClientAreaMotion; existing motion Off, shared BeatLoop, fog/splash/mote timing and teardown are retained. The Windows OS query compiles here but cannot execute on Linux.
8. Same-tab explicit embed retry: WPF OnTabShown clears the failure latch; Avalonia currently clears it on Suspend only. This unit now clears the latch on explicit navigation too, without resetting sound spending; a focused test asserts a second browser attempt and only one chime.

The three WPF host services alone span 1,330 reference lines, excluding account/profile-sync integration, bark host, tab handoff and focused tests. A correct host port with that lifecycle and its tests is larger than this unit's 1,000-added-line allowance. This unit prioritizes the audible entry, motion and retry gaps and preserves an honest stub rather than adding a fake live-data provider. The first-light dead-call evidence does not retire the block service dependency.

## Verification limits

Build/test results are recorded below. Windows desktop, live authenticated Descent payloads and native browser/audio devices cannot be exercised by headless tests. No credentials or secret files were inspected. The test-plan coverage script is absent on this base (invocation exit 127); the numbered step was added directly. Tests use the repository's isolated CCP_USERDATA_DIR bootstrap and fake audio sink/browser startup.

## Assumptions

- WPF's per-visible-entry sound spending is authoritative, including spending a cue when muted or assets are absent; enabling audio midway through the same entry does not replay it.
- Existing feature-intro navigation is retained. FIRST LIGHT is documented as rig-only rather than creating a new user-facing entry path absent from the reference.

Validation command forms (Release):

- `dotnet build <project> -c Release` for CCP.Core, CCP.Avalonia and every Linux test project referenced by build.yml.
- WPF test compile attempt: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; no Windows tests executed.
- `export DiffEngine_Disabled=true`; `dotnet test Tests/CCP.Core.Tests -c Release --no-build`.
- Focused Avalonia filter: SpiralRoomAudioTests, SpiralMotionTests, SpiralRoomShellTests, SpiralMigrationGateTests, SpiralHelpPopoverTests, SpiralEmbedViewTests and AmbientBeatLoopsTests.SpiralTabLoopsRideTheBeat.
- `bash .github/scripts/core-guards.sh`; `git diff --check`.

Results (exit 0 unless stated):

- CCP.Core and CCP.Avalonia Release builds: 0 errors.
- Linux test-project Release builds: CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests, CCP.Avalonia.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests, CCP.Core.Tests: all 0 errors.
- Windows-targeted ConditioningControlPanel.Tests compile: 557 pre-existing warnings, 0 errors. Not run on Linux.
- Core tests: Failed 0, Passed 3990, Skipped 5, Total 3995. Existing Vosk model-dependent tests skipped because the model is unavailable; no test was changed or disabled.
- Final focused Spiral tests: Failed 0, Passed 21, Skipped 0. Includes seven OS/user/gentle gate combinations and the real view's entry/repaint/retry paths. The headless font limitation did not reproduce here.
- Before-fix audio entry test: exit 1, expected cue versus empty list. Before-fix motion/retry tests: exit 1, three expected assertion failures. Fixes restored before final tests.
- Core guards: all passed. `git diff --check`: clean.
- Test-plan coverage script: unavailable on this base, exit 127. Manual numbered WPF/Rows coverage added.
- Not run: Windows desktop/user32 execution, real audio hardware, live authenticated Descent service, native browser rendering, full Avalonia suite and render-all. The row remains stub; no P122 wired promotion is claimed.
