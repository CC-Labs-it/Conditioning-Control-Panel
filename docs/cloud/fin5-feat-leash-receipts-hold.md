# FIN5 — leash receipts and hold

- Complete the hold tick through CoreAudio: WPF cue, master-volume scale, output suppression, mandatory-video silence, punishment-window allowance and shared minimum gap.
- Subscribe to GlobalShortcuts Deactivated; route native Wayland holds through the existing hold flow without querying the X11 keymap. Unbinding releases a pending hold.
- Add WPF hover colors and the reusable holder confirmation entry point; register that shared window with panic dismissal.
- Host the existing four-row receipt list on Social > Leash, refresh from receipt/snapshot events, and expose confirmed Let go with WPF text, server request and success/failure feedback.
- Keep one ledger row, promote to wired (not verified), and add numbered tester steps.
- Imports: none. Fetched parity/trunk read-only; implemented against the frozen WPF reference at base `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; no trunk files were copied or overwritten.

## Initial gap audit

At the pinned base the row's live gaps were tick audio, portal release/hold support, confirm-button hover brushes, an unhosted receipt list, and holder Let go confirmation. The listed punishment-window-close gap was stale: CutLeashAsync already calls LeashTaskHost.EndTask, which hides the gate and closes LeashPunishWindow before invoking Core cut safety. AppLeashTaskHost has no receipt/hold behavior to import. Shared receipt rules, countdown timing, X11/Win32 paths and the confirmation copy were already ported.

## Assumptions

- The smallest host for this unit is the receipt/release portion of a holder card on the existing Social > Leash page. Full drawer figures, send sheets, offers and other holder tools remain the separate feat-leash row. Existing held accounts arrive through the real friends poll.
- WPF waits for the confirming poll to remove a holder card after release; preserve that behavior.
- Preserve the established portal-grab policy (bind for effects with consent); a held portal key keeps that binding alive after its first panic stops effects. A portal hold follows Activated/Deactivated instead of an X11 physical-key query.

## Verification limits

Native portal/compositor behavior and Windows hooks cannot be exercised in this headless Linux environment. No real desktop, notifications backend or user profile is used by the tests.

- SDK: .NET 10.0.100. The channel installer lookup returned HTTP 403; the versioned SDK download from the supported builds endpoint succeeded.
- Builds: all 15 commands below exited 0, each ending with 0 errors. Existing warnings remain. All used `-c Release -m:1`; WPF additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
  - `dotnet build CCP.Core/CCP.Core.csproj`: exit 0.
  - `dotnet build CCP.Avalonia/CCP.Avalonia.csproj`: exit 0.
  - `dotnet build Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`: exit 0.
  - `dotnet build Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`: exit 0.
  - `dotnet build Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`: exit 0.
  - `dotnet build Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`: exit 0.
  - `dotnet build Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`: exit 0.
  - `dotnet build Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`: exit 0.
  - `dotnet build Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj`: exit 0.
  - `dotnet build Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`: exit 0.
  - `dotnet build Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`: exit 0.
  - `dotnet build Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`: exit 0.
  - `dotnet build Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`: exit 0.
  - `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj`: exit 0.
  - `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`: exit 0.

- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 1; final run 3,951 passed, 2 failed, 5 skipped (3,958 total). Failures: unchanged BoardTilesTests.A_frame_fits_the_budget (17.80 ms) and DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls (Loops versus Clips). Earlier full run: 3,952 passed, 1 timing failure, 5 skipped. Vosk skips are existing model-availability skips; no test was disabled or weakened.
- Isolated Core filter `FullyQualifiedName~LeashHoldFlowTests|FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget`: exit 0, 6 passed. Isolated remote-media failure filter: exit 0, 1 passed. `git diff --exit-code` against the base for CCP.Core and Tests/CCP.Core.Tests: exit 0 (both unchanged).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~LeashHoldTests|FullyQualifiedName~PanicSurfacesTests'`: exit 1; 8 passed, 4 failed at font creation (Segoe UI Black/Fredoka glyphTypeface). The affected tests are the shell hold scenario, holder-page/hover scenario, portal-hold scenario and existing panic-route scenario. Their runtime assertions need the supervisor's font-equipped runner. The earlier run before the final test additions had 11 passes and a sound cooldown test failure; the controlled test clock corrected that isolation issue and the sound test passed in the final run.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127 because this base has no such script. Manual equivalent: exit 0; exactly one ledger row and seven numbered WPF-linked tester steps for the row.
- `git diff --check`: exit 0.
- Not run: WPF tests on Windows, live Windows keyboard hooks, native Wayland portal integration, real audio playback and desktop visual checks. Headless UI tests were attempted but the four font-dependent checks remain blocked; no passing visual or red/green runtime proof is claimed for those checks.
