# Weekly intake reminder recut

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad.
Read the own-work diffs from 7f07005f935f to fin2 608f09dc52c5, fin4 75bfcb6b1ec2 and wire-b 6de330ff7830.
Reuse: fin2/wire-b reminder and tests; base card-art helper and intake Start handler; Core eligibility rule.
No WPF or safety code changed. No new localization keys; all four reminder keys exist in all ten languages.

WPF paths below are relative to ConditioningControlPanel/. Head partials are relative to CCP.Avalonia/Views/Windows/.

| Behavior | Status | WPF file:line | Avalonia/Core file:line |
| --- | --- | --- | --- |
| Reminder 14 s after open | Ported | MainWindow/MainWindow.Marquee.cs:717 | MainShellWindow.IntakeNudge.cs:29; MainShellWindow.axaml.cs:128; CCP.Avalonia/App.axaml.cs:661 |
| Preference, pass, dismissed week, session and game gates | Ported | MainWindow/MainWindow.Marquee.cs:1007 | MainShellWindow.IntakeNudge.cs:47; CCP.Core/Services/Banner/IntakeNudgeRule.cs:8 |
| One launch slot; announcement claims it before routing | Ported | MainWindow/MainWindow.Marquee.cs:913,1019 | MainShellWindow.Announcement.cs:43; MainShellWindow.IntakeNudge.cs:52 |
| Quiet Inbox, independent weekly dismissal | Ported | MainWindow/MainWindow.Marquee.cs:1046,1084 | MainShellWindow.IntakeNudge.cs:60,81 |
| Current niche art and punch-free copy | Ported | MainWindow/MainWindow.Marquee.cs:1042,1063 | MainShellWindow.IntakeNudge.cs:54,72; MainShellWindow.IntakePassTile.cs:56 |
| Existing intake Start path and its gates | Ported | MainWindow/MainWindow.Marquee.cs:1110 | MainShellWindow.IntakeNudge.cs:101; GradedIntakeTabView.axaml.cs:167 |
| Welcome identity and account repaint | Covered by PR #3466, a4ad95f7a | MainWindow/MainWindow.Marquee.cs:125; MainWindow/MainWindow.Login.cs:57 | PR head MainShellWindow.Marquee.cs:186; MainShellWindow.Login.cs:53 |
| Header sizing, support link, no-browser clipboard prompt | Covered by PR #3466 | MainWindow/MainWindow.xaml:1102,1133,1143; Helpers/BrowserLauncher.cs:62 | PR head MainShellWindow.axaml:952,968,1015; CCP.Avalonia/Controls/SafeHyperlinkButton.cs:51 |
| Message sheen, throttle and force bypass | Already on base | MainWindow/MainWindow.ChromeFx.cs:766 | MainShellWindow.ChromeFx.cs:49; MainShellWindow.Marquee.cs:212,243 |
| Rotation, retirement, pool and feeds | Already on base | MainWindow/MainWindow.Marquee.cs:51,504,746,881 | MainShellWindow.Marquee.cs:120,260; MainShellWindow.BannerPool.cs:17; MainShellWindow.ServerBanners.cs:25 |

PR #3466 was matched by git ls-remote refs/pull/*/head to the active cloud-r72-fin4-shell-marquee head.
The GitHub GraphQL PR listing returned Forbidden; coverage was read directly from the fetched branch and its validation.md.

## Part B

- Pop, drum, ring, breathing glow and sparkle lifecycle: WPF MainWindow/MainWindow.BannerFx.cs:166,236,295,322,346; old fin4 MainShellWindow.BannerFx.cs:29,96,125,206. Current MainShellWindow.Marquee.cs:201 only crossfades and sweeps; MainShellWindow.axaml:963 has no sparkle/flash layers. Keep the base sheen rather than replacing it. Old fin4 FX plus layout and its 199-line focused tests alone exceed the remaining 600-line budget after this reminder cut.
- Windows browser launch strategies: WPF Helpers/BrowserLauncher.cs:45-51; old fin2 CCP.Avalonia/Platform/ExternalOpener.cs:54. The base ExternalOpener.cs:107 only uses the platform launcher then shell. The visible failure prompt belongs to PR #3466 and must not be duplicated.

## Validation

SDK: .NET 10.0.403, installed with dotnet-install --channel 10.0.
Existing tests and skip conditions were not weakened. The new fixture uses bundled Inter and app font mappings on Linux, and follows TestIsolationTests.cs (suite dispatcher, Skia headless drawing, process isolation).

Commands and result tails:

- `dotnet build CCP.Core`: 50 existing warnings, 0 errors.
- `dotnet build CCP.Avalonia --no-restore`: 201 existing warnings, 0 errors.
- `dotnet build Tests/CCP.Avalonia.Tests --no-restore`: 145 existing warnings, 0 errors.
- `dotnet test Tests/CCP.Core.Tests`: Failed 0, Passed 4644, Skipped 6, Total 4650.
- `dotnet test Tests/CCP.Core.Tests --no-restore --filter FullyQualifiedName~IntakeNudgeRuleTests`: restored Failed 0, Passed 9.
- `dotnet test Tests/CCP.Avalonia.Tests --no-restore --filter FullyQualifiedName~IntakePassNudgeTests`: Failed 0, Passed 4.
- `bash .github/scripts/core-guards.sh`: All Core guards passed (429 XAML references).
- `python3 .github/scripts/avalonia_docs.py`: avalonia_docs self-check OK.
- `git diff --check`: clean.

The first affected UI attempt compiled but could not run behavior assertions: four reminder cases failed at shell construction on missing Segoe UI Black, and two existing notification cases failed on Consolas; 12 isolation checks passed. Bundled font mappings in the new fixture resolved the reminder failures. The imported legacy patron fixture also needed HasPremiumAccessProvider, matching current IntakePassService.cs:96, instead of its obsolete Lab seam.

Production-only mutation fail-proofs, with restoration in finally:

- Core eligibility bypassed with `true ||`: eight gate cases failed, the positive case passed; restored nine passed.

- Replacing the CTA with an empty action: the focused action test failed at its expected intake-gate-dialog assertion (1 failed, 0 passed).
- IconGuardTests: 6 passed; the new Inbox glyph exemption is data-only.

A combined reminder/isolation/notification run remains font-order dependent on this worker: if an existing fixture initializes Avalonia first without portable font options, the four reminder and two notification cases fail in font construction before behavior assertions, while 12 isolation cases pass. The isolated reminder filter initializes bundled fonts and runs all four behavior cases. No existing fixture or assertion was changed to hide this limitation.

- Dismissal/slot mutation: 3 failed, 0 passed. The popup and Inbox tests saw an empty dismissed week; the announcement-priority test saw two rows instead of the sole server announcement. Sources were restored before the final run.

Final restored isolated reminder run: Failed 0, Passed 4, Skipped 0, Total 4. The final Core guard and docs checks passed; git diff --check was clean. No interactive WPF comparison or native Windows browser launch was run.

## Ownership review follow-up at 70c525550

P2 accepted. WPF MainWindow/MainWindow.Marquee.cs:1079 sets Owner to the shell. MainShellWindow.IntakeNudge.cs:87 now opens with Show(this), including when the Inbox row runs the callback. IntakePassNudgeTests.ReminderBelongsToTheShellAndClosesWithIt tests both immediate and Inbox paths: the owner is the shell, the popup receives Closed when the shell closes, and the popup becomes invisible.

Fail-proof: added the regression test before the product fix. Both cases compiled and failed with Assert.Same, expected MainShellWindow, actual null (2 failed, 0 passed). Assertions and existing tests were retained for the fixed run. No localization key or WPF source changed; all four reminder keys remain present in all ten language files. The parity row stays partial.

Follow-up validation:

```text
dotnet build CCP.Core --no-restore: 0 warnings, 0 errors.
dotnet build CCP.Avalonia --no-restore: 201 existing warnings, 0 errors.
dotnet build Tests/CCP.Avalonia.Tests --no-restore: 145 existing warnings, 0 errors.
dotnet test Tests/CCP.Core.Tests --no-build --no-restore: Failed 0, Passed 4644, Skipped 6, Total 4650.
dotnet test Tests/CCP.Avalonia.Tests --no-build --no-restore --filter FullyQualifiedName~IntakePassNudgeTests: Failed 0, Passed 6, Skipped 0, Total 6.
bash .github/scripts/core-guards.sh: All Core guards passed.
python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.
git diff --check: clean.
```

## Hidden-panel review follow-up

P2 accepted. WPF MainWindow/MainWindow.Marquee.cs:1079 owns the popup even when the panel is hidden. Avalonia rejects that owner state, so MainShellWindow.IntakeNudge.cs:92 follows MainShellWindow.NavSearch.cs:67: use the visible panel, otherwise the visible launcher, otherwise no owner. Owner selection happens when the callback runs, including Inbox replay. Visible-panel ownership and owner-close behavior remain covered by the earlier tests.

IntakePassNudgeTests.HiddenPanelStillOffersTheReminder (:200) covers immediate and Inbox opening, each with and without a visible launcher. Fail-proof on the reviewed forced-hidden-owner code: 4 failed, 0 passed, with non-visible-owner exceptions or no opened popup. After the fix: all 10 reminder cases passed. The fixture also restores CoreAi.IsAvailableProvider in finally (:47,56). Runtime scratch notes were archived outside the repository; this report keeps behavior references and validation results. No WPF, safety code, localization keys or existing assertions changed. The existing ledger notes were preserved and the row remains stub.

```text
dotnet build CCP.Core --no-restore: 0 warnings, 0 errors.
dotnet build CCP.Avalonia --no-restore: 201 existing warnings, 0 errors.
dotnet build Tests/CCP.Avalonia.Tests --no-restore: 145 existing warnings, 0 errors.
dotnet test Tests/CCP.Core.Tests: Failed 0, Passed 4644, Skipped 6, Total 4650.
dotnet test Tests/CCP.Avalonia.Tests --no-build --no-restore --filter FullyQualifiedName~IntakePassNudgeTests: Failed 0, Passed 10, Skipped 0, Total 10.
bash .github/scripts/core-guards.sh: All Core guards passed.
python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.
git diff --check: clean.
```
