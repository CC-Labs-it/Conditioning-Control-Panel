# fin5-shell-profile-spiral

- Base verified at `fc8602234c89b9d0abdbddfcbdc4571f411b8587`; snapshot initially differed and was corrected before edits. Unit branch: `avalonia-port/cloud-fin5-shell-profile-spiral`.
- WPF reference gaps on entry: silent server-offer restore/apply and live offer/choice repaint; map state/ready/latest-state protocol, navigation success/failure and origin fence; room entry/splash audio; companion first-open greeting. The retained first-light ceremony has no live migration caller: at the pinned base, `DescentMigrationService.ApplyOfferNow` calls `ApplyChoice` and `RestoreChromeAfterMigration`, never `BeginSpiralFirstLight`/`BeginFirstLight`. This is analysis only, not a P122 wired claim.
- Adapted the WPF silent restore ledger into Core: server-priced curve-v2 restore, keepsakes, permanent unlock preservation, bonus, server-stage queue, anchor, watermark clear, pending submit armed last, idempotence and ack-only stage drip once per local day.
- Connected sync opt-in/submission to the shell host. UI-posted restore is fenced against account changes and window close; save precedes the cooldown-safe retry nudge. Ack/choice repaint uses the existing reader signal. No new surface: the existing `spiral-room` panic registration still tears down the map and prevents background reconstruction.
- Implemented token-free map payload forwarding (unknown fields/date strings preserved), exact `spiral:ready`, latest-state refresh, HTTPS origin allowlist, successful-navigation splash handoff and entry-latched navigation failure. Preserve generic WebHost completion notifications for existing callers.
- Connected WPF room cue fallback order, volume scales, active-mod audio overrides through the already-seeded CoreModArt seam, mute handling and entry latches; spiral-only companion greeting uses the existing bark engine's lifetime rule.
- Added focused Core/headless tests and numbered tester steps. Ledger remains `stub` with explicit remaining gaps.
- Read-only source branch fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` (`parity/trunk`). No trunk file was overwritten/imported wholesale; adaptations above are credited to its WPF SpiralEmbedView, DescentMigrationService, DescentRoomSfx and DescentBarkWatcher behavior, also present at the pinned base. Existing head/Core files won.

## Verification limits

- SDK channel discovery returned HTTP 403; pinned .NET SDK 10.0.100 installed successfully.
- `dotnet build -c Release`: Core, Avalonia, and all eleven Linux workflow test projects passed with 0 errors. Test projects: Core.Tests, Core.Settings.Tests, Core.Content.Tests; Avalonia.Tests, CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests, Language.Tests, Report.Tests, Scheduler.Tests, Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: passed, 560 warnings / 0 errors. Windows behavior was compiled, not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. The initial sandbox-only attempt could not create temporary files; rerunning with authorized workspace writes passed. `test-plan-coverage.sh` is absent on this base (attempt exit 127); manual check confirms one ledger row and numbered coverage.
- With `DiffEngine_Disabled=true`, focused Core ProfileSpiralTests + SpiralFinishTests: exit 0, 24 passed. Focused headless Avalonia ProfileSpiralTests: exit 0, 3 passed (includes greeting and audio); fonts worked in this runtime. Earlier combined ProfileSpiralTests/CompanionBarkHeadTests run also passed, 7 tests, before moving the greeting assertion onto the live-reader path.
- Full Core suite: one failure in `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` (null scheduled handle), five existing Vosk skips. The same test fails at the same assertion on the untouched base in a detached worktree (exit 1, 1 failed), so it is not caused by this unit. No tests were weakened or skipped by this change. Final full rerun: exit 0, 2,835 passed / 5 skipped / 0 failed (2,840 total); this scheduling failure is intermittent.
- Native desktop audio, remote browser JS/handshake, browser process crash, Windows execution and render-all were not exercised. Headless tests use existing sandbox settings/secret fixtures and fake network.

## Remaining gaps and assumptions

- `WebHost`/NativeWebView has no shared process-death notification or document-created script hook. Navigation failure is covered, but browser crashes and early WebKit ready messages cannot be handled with WPF fidelity by the available shared API. Replacing/forking the browser backend exceeds this unit's minimal host adaptation; ledger stays stub.
- Initialization failures still use WebHost's legible fallback panel. A real native engine is needed to check arranged browser loading behind the splash.
- Reused the existing CoreModArt audio-override seam rather than porting another resolver; built-in/content-pack cues fall back through ContentLocator. Missing files remain silent.
- Transient in-hand offers/depth-counted fuse holds are not ported: this host commits silent restore on the next UI dispatch and notifies after commit/ack. Persisted offer gating remains intact. The retired ceremony is not resurrected.
- Fuse chrome restore depends on the unported DescentFuseChrome/palette dimming host. No dimming is introduced here.
- Assume the closest small adaptation is the head's existing reduced-motion/performance policy, shared browser carrier, content roots and cooldown-safe sync nudge. No PR is opened; only the authorized unit branch is pushed.
