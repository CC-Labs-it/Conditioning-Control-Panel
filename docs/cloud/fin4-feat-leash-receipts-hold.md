# FIN4: leash receipts and hold completion

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; task branch `avalonia-port/cloud-fin4-feat-leash-receipts-hold`.
- Add the WPF hold countdown cue at whole seconds 1–4, using CoreAudio, master volume times 0.10, output suppression, mandatory-video silence, mod audio overrides and existing Core LeashSfxRules.
- Connect GlobalShortcuts Deactivated and session teardown to release the hold. Activated starts the shared Core flow; native Wayland holds use portal state instead of XQueryKeymap. Retain a bound session while leashed so panic stopping effects does not tear down the hold.
- Match WPF No/Yes hover colors, including Fluent template presenter colors.
- Extend focused headless tests for whole-second cadence, portal early release, audio volume/suppression, and both hover/rest brushes. Existing five-second Yes/No and receipt rendering assertions remain.
- Replace repeated historical ledger text with one current, honest `stub` row and add numbered tester steps.
- No trunk code imported. `parity/trunk` fetched read-only; implementation adapts the pinned WPF reference and existing Core/head seams.

## WPF gap inventory and disposition

Reference is the pinned base above. The ledger's Services/Leash/LeashReceipts.cs has moved to CCP.Core/Services/Leash/LeashReceipts.cs. Read alongside WPF LeashHoldRing, LeashCutConfirmWindow, LeashHolderCard SentSection/HeadMenu, MainWindow.Leash hold methods and AppLeashTaskHost.

1. Missing whole-second tick sound: implemented with WPF clip, level and sound rules.
2. Missing punish-window close on Yes: stale ledger claim. Existing MainShellWindow.CutLeashAsync calls LeashTaskHost.EndTask, which cancels the runner, calls LeashPunishWindow.CloseNow and hides the gate before CutAsync. No duplicate close path added.
3. Missing portal key-up: Deactivated now ends the hold; unbind cancels it too. Bound portal sessions support five-second holds with native Wayland focus. Standalone portal binding while leashed with no effects, including panic disabled, remains missing: the existing compositor-grab policy binds only from effect start.
4. Missing confirm hover brushes: implemented (No purple, Yes pink, rest restored on exit).
5. Missing holder Let go Confirm caller: still depends on `feat-leash` holder-card/menu hosting. WPF calls Confirm from LeashHolderCard.HeadMenu; the Avalonia tab is still an empty state. No unused Confirm API added.
6. Receipt rules and row rendering already exist, but the real holder SentSection host is absent. This shares dependency 5. WPF's holder card is 797 lines and drawer section is 285 lines before dependencies (preset sheets, avatars, help and other cards). Porting the complete WPF host faithfully exceeds this unit's 1000-added-line cap; a reduced replacement would invent a different holder UI. The row remains `stub` with both gaps explicit.

No new window or surface is introduced; existing ring/confirm/punish windows and leash-task panic registration are reused.

## Verification limits

All 15 requested builds completed with exit 0 and 0 errors using `dotnet build <project> -c Release`:

- CCP.Core and CCP.Avalonia.
- Tests/CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests.
- Tests/CCP.Avalonia.Consent.Tests, Language.Tests, CatalogueStartup.Tests, Migration.Tests, Scheduler.Tests, Report.Tests, GeneralSettings.Tests, Snapshot.Tests, and CCP.Avalonia.Tests (each with the CCP.Avalonia prefix).
- Tests/ConditioningControlPanel.Tests, with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; compiled, not run on Linux.

With `DiffEngine_Disabled=true`:

- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 1, 3952 passed, 5 skipped, 1 failed. The failure is the unrelated BoardTilesTests.A_frame_fits_the_budget timing assertion (16.98 ms under compilation load); no Core code changed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~Leash`: exit 0, all 131 passed.
- Initial focused `LeashHoldTests` run: audio, receipt list and X11 routing passed; two tests that show windows failed at font creation. The hover test now exercises unshown controls to avoid measuring text; the full shell flow still needs the supervisor's font-capable runner.
- Hover regression proof: temporarily remove the hover handlers and run `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~ConfirmButtons`; exit 1 with expected purple `#FF3A1F66`, actual rest `#FF1C1233`. Restore the handlers for the final build/run.
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~LeashHoldTests`: exit 1, 4 passed, 1 failed; hover, audio, X11 routing and receipts pass. The shell flow fails before its assertions at existing PremiumSpark font creation (`Segoe UI Black`), so portal and full shell flow execution remain for the supervisor. Compilation completed with 0 errors. No tests were skipped or weakened.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127, script absent on this pinned base.
- `git diff --check`: exit 0.

The first regression-run attempt collided with a concurrent build of Avalonia output (AVLN9999); subsequent verification is serialized. The initial SDK channel discovery hit a 403, so a fixed .NET 10 SDK install was used; the channel install also eventually succeeded. No installed SDK files enter the commit.

 No real desktop, notification service, account, portal session or user profile is used by tests. Windows native input is compiled but cannot run on Linux. The full WPF application and tests are cross-target compilation only.

## Assumptions

- Preserve the existing portal binding policy; receiving key-up does not authorize an additional permanent compositor grab.
- Reuse the existing cut cleanup rather than adding a second cleanup path.
- Defer the complete holder-card feature rather than replace its WPF layout with a receipts-only substitute.
