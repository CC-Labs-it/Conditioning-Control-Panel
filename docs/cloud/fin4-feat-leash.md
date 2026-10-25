# fin4-feat-leash

- Base verified: `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. Independent cloud snapshot; branch names were not used to establish identity.
- Adds the leashed player's own card on Social > Leash, preserving the card across poll updates and releasing the subscription on detach. Cut is first, immediate and unconditional; assignment watches reach the existing panic-registered task runner. Player-owned intensity, DND and video maximum use the Core service.
- Reuses the existing FeatureDayLog for report minutes and local-day assignment completion, matching WPF LeashServiceApp.AppDayInputs.
- Defers the punishment gate during tutorial/startup dialogs using existing head state.
- Replaces the repeatedly concatenated feat-leash ledger history with one current stub row and numbered manual checks.
- Source: WPF reference at the pinned base. Fetched read-only parity/trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## WPF gap audit

The base already has poll/report wiring, receipt primitives, gate actions/pardons/unplayable handling, task runner (lines/Pink/bubbles/Detention/video), caged punishment window, cut-confirm/hold ring, panic-off/Lockdown routes and cut safety. Old ledger claims that these are absent were stale.

The following live WPF behavior remains absent or incomplete; this row must remain **stub**, never wired:

- Drawer/Leash page offers before self before held cards; friend Offer/Sent/Holding/Greyed chips, result/cooldown text and offer receipts. Incoming offers are not yet automatically queued and presented when idle.
- Ask card: standard intensity default, allowed-punishment chips, level guide, mandatory first-read clock, yes/no with busy guard, retryable failure/off/gone messages, escape deferral and successful-answer snap.
- Holder card: identity/presence, figures, assignment status, seven-day strip, DND constraints, punish/assign/reward sheets, caps and video picking, send-result feedback, sent receipts, release and replay snap. The new self card therefore cannot yet offer its read-only holder preview.
- Self-card avatar pictures, intensity guide and graphical sticker discs (textual newest-first 14 records are present). The slider uses native Avalonia interaction with release/key-up commit; WPF's colored cap bands/value styling remains absent.
- Explainer/help surfaces and persistent read acknowledgements; snap-card chain animation/replay; ask/snap passive overlay host; tug wobble and event notification presentation.
- Gate fade/thud/stamp swing/pip sparks, holder avatar pictures and help button; shared LeashFx sounds and motion-level behavior across the cards.
- Full StartupLadder held/modal pump remains a separate unported host; this unit reads the available startup-dialog/update/tutorial flags. A Deeper catalogue watch lacks a completion event in the existing host.

These are live features, not P122 dead callers: WPF LeashDrawerSection constructs self/holder cards and OfferChipFor; LeashSurfaces.ShowAsk/ShowSnap open the overlay; App startup initializes LeashSurfaces; MainWindow.Leash reads tutorial/startup states. The remaining UI/host work exceeds the unit's 1000-added-line ceiling. User-visible self-card/report/gate gaps were prioritized; this is a partial implementation, not a pure dependency NOOP.

## Safety and assumptions

- The self card is embedded in the existing page, not a new screen-owning window. Watch it uses LeashTaskHost.Runner and its existing leash-task panic registration; Cut cancels the task before calling offline-first Core CutAsync.
- Follow WPF's immediate optimistic intensity/video-cap updates and quiet service failures. DND updates arrive on the next poll.
- Use existing Friends palette and text records for stickers until the shared artwork is ported; no new icons or emoji icons.

## Verification limits

- .NET 10.0.402: Release builds of CCP.Core and CCP.Avalonia passed (0 errors). CCP.Avalonia.Tests compiled with 0 errors; all eleven other Linux test projects named in build.yml compiled against those outputs (`-p:BuildProjectReferences=false`).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release -p:BuildProjectReferences=false --filter FullyQualifiedName~LeashMinutesTests`: passed 1/1. Before restoring the minutes fix, the regression failed with expected 17 / actual 0. The final fixture uses the server's valid 15-minute assignment and asserts it parses, then verifies 17 reported minutes complete it, yesterday does not complete, and an unavailable log reports zero.
- Headless gate regression attempted before the fix: blocked before its assertions by the known `Segoe UI Black` glyph-typeface creation error in the existing shell PremiumSpark. Therefore the self-card and gate UI tests were compiled but not successfully executed here; CI with the font-fix lane must run LeashSelfCardTests, LeashGateCardTests and the existing LeashPunishTests. No tests were weakened or disabled.
- Full Core test run under concurrent compiler load: 3952 passed, 5 existing Vosk skips, 1 board frame-budget failure (25.42 ms vs 12 ms). No Core or board sources changed. Idle full-suite rerun passed: **3953 passed, 5 Vosk skips, 0 failed**, exit 0 (39 s).
- `bash .github/scripts/core-guards.sh`: passed (429 XAML references checked). `git diff --check`: passed. `test-plan-coverage.sh` is absent on this pinned base (attempt exits 127); numbered steps were added nonetheless.
- Windows desktop behavior, live account/network leash flows, native video playback and the remaining UI gaps were not run in Linux. WPF test project cross-target build passed with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: 0 errors (554 existing warnings), exit 0; compiled only, not run.

- Added-line count against the pinned remote base: 473 (below 1000); no Core, WPF reference or workflow source changes.
