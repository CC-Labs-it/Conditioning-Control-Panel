# fin5-shell-profile-vat

- Base: `a3d9e984fb2f2c002f3849e485129134e367a6aa` (`avalonia-port/perf-bench-avalonia`). The initial snapshot was different; fetched the intended base and verified the pinned commit before editing.
- Completed the independent profile-poll progression gap: shared `ProfileAdopt.TryAdoptFromProfilePoll` adopts any positive server lead only when the account/season watermark proves the local ledger clean. It preserves WPF's 0.01 tolerance, migration gate, season scope and no-downward rule, records the new agreement, and leaves saving to the head.
- Connected the existing DescentService startup handoff to curve-before-level adoption, persistence and the imperative header repaint. Trainer Card open and accepted-sync refresh already reach this service; no extra polling or network writes were added.
- Added focused pure tests and a headless header test, plus numbered manual steps. No new surface requires panic registration.
- Kept `shell-profile-vat` as `stub` with explicit missing behavior; did not arm a jar that would strand the persisted hold.
- No trunk code imported. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation is adapted from the pinned base's WPF `ProfileSyncService.TryAdoptFromServerProgression` (lines 426–484), using the existing shared curve/watermark helpers.

## WPF gap inventory and remaining dependency

Compared the single ledger row and both heads' `MainWindow.ProfileVat.cs` / `MainShellWindow.ProfileVat.cs`, then the WPF faucet host and shared Descent/profile helpers at the pinned base.

1. **Completed:** full clean-ledger level/XP profile handoff, not just curve following; save and repaint even when the epoch is unchanged. All WPF gates and agreement updates are covered by focused tests.
2. **Missing:** `ApplyDescentToVat` driven on tab entry and block changes. This must fold `VatFillCoordinator` through the existing persisted `VatFaucetHold`, never infer a fill from local XP. Seed shows the hold; cap retunes rescale silently; midnight/corrections drain silently; deltas during a pour extend it without restarting.
3. **Missing:** tab entry snaps to the held display, leaving the card cancels faucet activity; withdrawal/logout disarms the live jar and faucet. Current startup keeps the jar hidden.
4. **Missing:** live fill-percent subscription and unconditional readout refresh after cap changes; cap must be assigned before synchronous fill notifications. Existing geometry/readout methods have no live apply caller.
5. **Missing:** MAX tick glyph positioning follows the lip, and faucet geometry/presentation follows arm/disarm.
6. **Missing:** `descent-vat` intro only after a jar is armed and the Trainer Card is visible. Content and intro host already exist; calling it now would describe a jar the user cannot see.
7. **Dependent:** recurring 60-second requests become reachable only after the real apply path arms the jar. Existing poll gate tests cover visible/active/minimized/tab/close conditions, but an internal test arm is not a real user path.

Items 2–7 require `shell-profile-faucet`. Its head partial is wholly a placeholder; the WPF host is 1,145 lines including comments and defines 71 dropped members. Core already contains `VatFaucetHold` and `AppSettingsVatPourLedger`, so those are not blockers. The remaining host includes pointer capture, keyboard charge/release and cancellation on focus/capture/tab loss; 700–1,400 ms scaled charge; six audio rungs; charge ring, thud/shiver/drip feedback; daily compact cut; haptic/audio routing; motion-aware ambient presentation and settle-watch cleanup. The head's available motion gates must be reused/adapted; the stale faucet header is not treated as proof that Core hold is absent.

The estimated faithful host port, VAT wiring and focused behavior tests exceed the remaining budget under the 1,000-added-line cap. A reduced click-to-pour substitute or automatic fill would violate the WPF persisted-hold contract. This unit completes the independent progression behavior and documents the dependency rather than claiming full parity. No P122 exception: WPF `ApplyDescentToVat` has live tab-entry and block-change callers.

## Verification limits

- .NET SDK `10.0.402` installed with the requested installer (its preliminary latest-version probe returned 403; SDK installation succeeded).
- `dotnet build -c Release` succeeded (exit 0, zero errors) for CCP.Core, CCP.Avalonia, and all 12 Linux test projects named in the workflow: Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Migration, Avalonia.Report, Avalonia.Scheduler and Avalonia.Snapshot.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. The base does not contain `test-plan-coverage.sh`; numbered row steps were added manually.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~ProfilePollAdoptTests`: exit 0, 15 passed, no skips.
- Final full Core suite (`--no-build`, Release, DiffEngine disabled, no concurrent builds/tests): exit 0, 4,490 passed, five existing Vosk skips, zero failures (42 s). Two earlier contended runs exited 1; 4,489 passed, five existing Vosk skips, one failure in unchanged `BoardTilesTests.A_frame_fits_the_budget` (24.61 ms initially, 13.60 ms on retry while Windows compilation continued). No tests weakened or removed.
- Main headless test assembly compiled with zero errors. Selected `ProfileVatPollTests|ProfileBubbleTests` runtime stalled; bounded retry exited 1 after 90 seconds of inactivity in existing `ProfileBubbleTests.LevelUpFlashesTheGoldRing`, before the new VAT test. This timed-out run is not counted as a pass; subsequent isolated and final runs are listed below. No font exception was observed.
- Isolated new headless test: exit 0, one passed (42 s). Negative control using the pre-fix curve-only handoff: exit 1 as expected, server level 46 remained local level 44; restored the final progression handoff afterward.
- Isolated unchanged frame-budget test after Windows compilation stopped: exit 0, one passed (1 s). The complete suite's two failed runs remain reported above.
- Windows-targeted test build with `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false`: original long-running attempt stopped; bounded retry with `--no-restore -m:1 -p:UseSharedCompilation=false` timed out at 240 s (exit 124) during WPF compilation. Core compiled, but no completed WPF/test build is claimed. No Windows code was run.
- First completed combined headless run: exit 1, six passed and the new test asserted during the XP count-up. The test now advances the existing shared clock to the final readout and restores it afterward; production animation behavior is unchanged. Final relevant class run (`ProfileVatPollTests|ProfileBubbleTests`, Release, DiffEngine disabled, 90 s inactivity timeout): exit 0, seven passed, zero skipped (1 m 13 s). Tests use existing temporary profile and XDG sandboxes; no real desktop, notification or user profile is used.

## Assumptions

- Prefer an honest partial `stub` over revealing a jar without its charge-hold host; the independent progression gap is useful through the existing Trainer Card open path.
- Reject nonfinite server XP before ledger mutation. WPF explicitly rejects NaN; rejecting positive infinity also prevents malformed wire data poisoning the ledger.
