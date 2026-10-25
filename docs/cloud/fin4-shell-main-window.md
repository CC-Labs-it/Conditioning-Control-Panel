# fin4-shell-main-window

- Base verified at `09eac3fa3f674f9965bb1bcc946706a09d295ff0`; independent cloud snapshot, clean before changes. Branch: `avalonia-port/cloud-fin4-shell-main-window`.
- Restore authoritative external-mod repaint of selector, logo, palette and Home labels/art, and localized Home/takeover text. Refresh is visual only: pending choices and default presets are unaffected by an external event. Subscribe/unsubscribe the language hook with the shell lifetime.
- Restore single-media Play / Edit / Add to Library / Cancel order, using the existing Deeper player/editor. Preserve copy/cancel behavior and host-open error dialogs. Register prompt/player panic cleanup in `PanicSurfaces`.
- Restore enhancement drops through existing Core `EnhancementLibrary` and import rules: schema sniff, duplicate detection, promotion, source-directory persistence, reload and result toasts. No new shared engine abstraction.
- Add focused tests and update existing real-window drag/drop tests from the superseded library-only behavior. Add numbered manual tester steps. Keep one aggregate ledger row, honestly `stub`.
- No new parity/trunk imports. Existing `MainShellWindow.ModTileArt.cs` attribution to `07ced25ff67e634d7efd67d4060a9a2a74ac651e` is retained; the new logic is adapted directly from the frozen WPF reference at the pinned base.

## WPF audit and remaining gaps

Read the row and its cited `MainWindow.xaml` / `MainWindow.xaml.cs`, plus the referenced `MainWindow.UiUpdates.cs`, `MainWindow.SessionIO.cs`, `MainWindow.Presets.cs` and `MainWindow.DeeperTab.cs`. Compared constructor, loaded/mod-change subscriptions, feature-image/name repaint, drop dispatch and window-state hooks against the Avalonia partials.

The base already implements the header manager action, stock/user sorting, mod persistence and pending-choice precedence, browser radio/default-page synchronization, personality restoration, per-mod presets, window-wide drop classification, session/preset/mod/asset imports, exit/tray routes, work-area fitting, rail door art and skill-tree hooks. Several old partial comments overstate what is absent: takeover, Haptics and other feature plates already have tab-owned mod-art hooks; those remain authoritative. Studio also has its own mod hook.

Gaps found before this change:

1. External mod activation refreshes tile pictures but leaves shell palette/logo/selector stale. Fixed by the visual-only refresh.
2. Home feature-card and split-card captions remain static localized/English text. Fixed for the WPF mosaic captions; Mystery's dynamic offer/tier logic remains separate and incomplete.
3. Takeover header, lock/unlock captions, feature toggle labels and start/stop tooltip do not all follow the active mod. Those labels are restored; the settings/account support inline takeover copy remains missing.
4. Single playable media drops offer Library/Cancel only despite existing player/editor hosts. Fixed, including panic cancellation and host-open errors.
5. Enhancement drops always report library-not-ready despite the Core writer being available. Import and duplicate behavior restored. WPF's exact row reveal/flash still needs a hub selection seam; this unit opens the refreshed hub instead.
6. WPF's wider stale-surface sweep remains incomplete: visible achievement repaint, profile avatar/banner/accent/title/showcase pins, quest image cache invalidation and rendered quest stamps, preset/detail labels and state chips, mod-aware rail/stat tooltips, rank/bonus and Mystery refresh. The head contains parts of those hosts, but not the complete sweep and render contracts. Quest stamps alone have a 1,034-line WPF builder/popup partial, beyond this unit's remaining budget before tests/adaptation.
7. Description/rail/play hero art exists in several tab-owned ports, but full WPF per-surface framing metadata (crop/offset/zoom), description and rail-chip framing is not reproduced by the shell aggregate.
8. The constructor/loaded/state/foreground audit still finds head-specific omissions: full avatar/tube state reattachment and taskbar thumbnail integration, desktop-wide chat/camera/quick-recalibration hotkey chain, and service-driven refresh/lifecycle pieces documented in `MainShellWindow.SessionIO.cs`, `UiUpdates.cs`, `QuestStamps.cs` and `WindowChrome.cs`. Existing panic, chrome, tray, screen fitting, session and startup implementations are retained; this unit does not claim all 3,800+ WPF shell lines are wired.

These are live WPF paths, so P122/dead-caller promotion does not apply. Completing the aggregate and its dependent profile/stamp/framing hosts cannot fit 1,000 added lines. User-visible bounded gaps were implemented first, as requested; this is not a pure-dependency NOOP.

## Verification limits

- .NET 10 installed via the requested installer. Channel discovery initially returned HTTP 403; a pinned 10.0.100 install succeeded, and channel installation subsequently supplied 10.0.402 (used for final builds).
- `dotnet build -c Release`: Core, Avalonia, all Linux test projects named in `.github/workflows/build.yml` (Core, Core.Settings, Core.Content; Avalonia, Consent, Language, CatalogueStartup, Scheduler, Report, GeneralSettings, Snapshot): 0 errors. A Report build raced an intermediate edit and failed; the stable-source recheck passed with 0 errors.
- WPF test project cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: 0 errors (512 warnings). WPF app is compiled transitively; Windows runtime was not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `git diff --check`: exit 0. Base has no `.github/scripts/test-plan-coverage.sh`; numbered `docs/avalonia-test-plan.md` steps were added anyway.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 2,817 passed, 5 skipped, one failure in `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch`. This is the clock failure already recorded in the base row. Isolated recheck: exit 0, 1 passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~ModChoiceTests`: final exit 0, **17/17 passed**, including language refresh, external repaint, media Play/Edit, prompt/panic cancellation and enhancement import/deduplication. The final test-project build has 0 errors.
- Regression check with the original two mod-event partials from the pinned base: exit 1, actual behavior assertion fails (`Expected: #ff00ff41`, `Actual: #ffe84393`). The implementation was restored afterward. The prompt test separately exposed a missing name scope on the code-built dialog; that was fixed rather than weakening its named-button assertions. A language test expectation was corrected to WPF's icon-free visible caption before the final green run.
- `--filter FullyQualifiedName~WindowDropTests`: exit 1, 8 failed during shell construction with the known environment `Segoe UI Black` glyphTypeface failure. `--filter FullyQualifiedName~PanicSurfacesTests`: exit 1, 4 passed, 1 failed for the same font reason. Combined execution also hits this initialization-order-dependent failure (4 passed, 25 failed). No tests were skipped/deleted to hide it. Earlier 90-second ModChoice attempts timed out; the final complete run used a sufficient bound and finished successfully in 1.67 minutes. These two other classes and render-all still require the supervisor's font-capable runner; no final render claim is made.
- No real desktop/profile, Windows runtime, Vosk download or full Linux packaging smoke was used. Tests use the existing isolated profile setup. The complete headless suite is not claimed green.

## Assumptions

- Preserve the existing Deeper hub and Core library writer. A short-lived library host is disposed after import; this path explicitly reloads the hub rather than introducing a second persistent watcher host.
- External mod and language events are repaint-only; only the existing explicit activation path changes pending choices, settings/default presets and personality.
- Retain `stub` and enumerate remaining live behavior rather than claiming aggregate completion.
