# Shell marquee re-cut onto 7.2

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad. Old lane: 75bfcb6b1ec2.
The full old lane is +846/-40 before adapting to 7.2, so this cut prioritizes safe links and header behavior.
No WPF files or existing tests changed. No new localization keys; the browser strings and signed-in/no-name string already exist in all ten Core language files.

| Behavior | WPF specification | This cut or unchanged base | Old lane evidence |
| --- | --- | --- | --- |
| Motion Off swaps | MainWindow/MainWindow.Marquee.cs:432 | CCP.Avalonia/Views/Windows/MainShellWindow.Marquee.cs:166 | MainShellWindow.Marquee.cs:160 |
| Welcome identity, blank provider fallback, sign-in repaint | MainWindow/MainWindow.Marquee.cs:125; MainWindow/MainWindow.Login.cs:57,123,328 | MainShellWindow.Marquee.cs:186; MainShellWindow.Login.cs:53 | MainShellWindow.Marquee.cs:194; MainShellWindow.Login.cs:53 |
| 31px/744px/14px banner, support link | MainWindow/MainWindow.xaml:1102,1133,1143 | MainShellWindow.axaml:952,968 | MainShellWindow.axaml:941,959 |
| Browser failure copies URL and explains | Helpers/BrowserLauncher.cs:63 | CCP.Avalonia/Controls/SafeHyperlinkButton.cs:51; MainShellWindow.axaml:1015 | SafeHyperlinkButton.cs:41 |
| Sandbox refusal has no clipboard or dialog side effect | External opening remains gated; stricter than WPF BrowserLauncher.cs:63 | SafeHyperlinkButton.cs:70 | SafeHyperlinkButton.cs:47 |
| Panic closes fallback prompt | BrowserLauncher.cs:75 modal; stricter cleanup here | CCP.Avalonia/Views/Windows/PanicSurfaces.cs:107 | PanicSurfaces.cs browser-prompt registration |
| Rotation, retirement, authored pool, server feeds | MainWindow/MainWindow.Marquee.cs:51,504,746,881 | Already on 74ac6ce3d: MainShellWindow.Marquee.cs:120,261; MainShellWindow.BannerPool.cs:17; MainShellWindow.ServerBanners.cs:25; MainShellWindow.Announcement.cs:33 | Preserved by the old lane |
| 750ms sheen, 20s throttle, force bypass | MainWindow/MainWindow.ChromeFx.cs:766 | Already on 74ac6ce3d: MainShellWindow.ChromeFx.cs:49 | MainShellWindow.BannerFx.cs:115; retained newer base implementation |

WPF paths above are relative to ConditioningControlPanel/. Shell partials are relative to CCP.Avalonia/Views/Windows/ unless stated otherwise. Old lane paths/lines refer to 75bfcb6b1ec2.

## Part B

- Weekly 14s intake nudge, launch arbitration, action and art: old MainShellWindow.IntakeNudge.cs:31,41,110,118; WPF MainWindow/MainWindow.Marquee.cs:998. Reuse the base IntakePassCardImage helper at MainShellWindow.IntakePassTile.cs:56. Extend stand-down gates to the newer base hosts before wiring.
- Full drum roll, Reduced pop, ring flash, sibling glow, support sparkles and motion/focus cleanup: old MainShellWindow.BannerFx.cs:29,96,125,206; MainShellWindow.axaml:934. WPF MainWindow/MainWindow.BannerFx.cs:49,72,349. Keep the newer base sheen clock.
- Pre-existing old-lane gaps remain in the ledger: depth/glass/rim/clip/text glow, white support run, Windows browser fallback processes, What's New/version stamping, season rollover sync and remaining host gates. These were not implemented by the old lane.

## Validation

SDK: .NET 10.0.403, installed with dotnet-install --channel 10.0.
Core suite: 4,635 passed, 6 existing skips, 0 failed (4,641 total).
Core guards: All Core guards passed.
Docs: avalonia_docs self-check OK.
The test file uses the suite dispatcher, Skia headless setup and assembly-wide isolation hook.

Selected Avalonia tests: 54 passed, 0 failed, including BannerMotionTests, BannerPoolShellTests, FavoritesRailShellTests, HomeMarqueeTests, PanicSurfacesTests and TestIsolationTests.

Commands used (dotnet is /tmp/ccp-dotnet/dotnet in this environment):

```sh
dotnet test Tests/CCP.Core.Tests --no-build --no-restore
dotnet test Tests/CCP.Avalonia.Tests --no-restore --filter 'FullyQualifiedName~BannerMotionTests|FullyQualifiedName~TestIsolationTests|FullyQualifiedName~BannerPoolShellTests|FullyQualifiedName~FavoritesRailShellTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~HomeMarqueeTests'
bash .github/scripts/core-guards.sh
python3 .github/scripts/avalonia_docs.py
```

## Mutation fail-proofs

Only production source was mutated; test assertions stayed intact. Every mutation run compiled successfully and returned exit 1 with test failures. Production source was restored in a finally block.

| Mutation | Focused failures |
| --- | --- |
| Disable EnsureFadeTransition's Off guard; remove login welcome refresh; change height/max width back to 26/620; remove the support link name; disable ExplainFailure | 8 failed / 1 passed: both motion tests, dimensions, support link, three welcome sources and web failure. Sandbox refusal still passed. |
| Copy "broken" instead of the URL; write "broken" to the clipboard on sandbox refusal | 2 failed: WebFailureCopiesTheLinkAndPanicClosesThePrompt and ASandboxRefusalSkipsTheClipboardAndPrompt |
| Make CloseBrowserPrompts iterate an empty array | 1 failed: WebFailureCopiesTheLinkAndPanicClosesThePrompt, at the assertion that the prompt is no longer visible |

The temporary runner called dotnet test Tests/CCP.Avalonia.Tests --no-restore with the BannerMotionTests filter (then just the affected clipboard/panic cases). Restored builds and final green results are recorded below.

Restored final results:

```text
dotnet build CCP.Core --no-restore: Build succeeded, 0 warnings, 0 errors.
dotnet build CCP.Avalonia --no-restore: 201 warnings, 0 errors.
dotnet build Tests/CCP.Avalonia.Tests --no-restore: 145 warnings, 0 errors.
dotnet test Tests/CCP.Avalonia.Tests --no-build --no-restore --filter 'FullyQualifiedName~BannerMotionTests|FullyQualifiedName~TestIsolationTests': Test Run Successful, 21 passed.
```

For the original re-cut, the entire Avalonia suite was compiled; only the 54 selected tests (and the final 21-case subset) were run. WPF, native Windows browser fallbacks and an interactive live browser were not run. docs/avalonia-progress.html was not changed.

## Review follow-up

Both findings accepted. No WPF or Core files changed, and no existing assertions were weakened.

- P2: SafeHyperlinkButton.cs:44 increments the panic generation before closing registered prompts. Each request captures that generation and watches its owner's Closed event (:56-60), checks validity after launcher and clipboard awaits (:64,74), and unsubscribes on completion (:85). WPF BrowserLauncher.cs:68-80 performs these operations synchronously; the Avalonia checks prevent an async continuation from opening a late modal. Four new headless cases delay either await, then trigger panic or owner closure, and assert no prompt, visited-state write or later clipboard call.
- P3: MainShellWindow.axaml:1015 carries the exact WPF purpose, "open the CC Labs web app" (WPF MainWindow.Marquee.cs:489). SafeHyperlinkButton.cs:77-80 uses the existing msg_browser_no_default_for key (WPF BrowserLauncher.cs:71-74). The existing fallback test now asserts the complete localized message including its URL. Required browser keys were verified in all ten language files; no new key was needed.

Initial follow-up validation: 33 affected/isolation tests passed. Full Core suite: 4,635 passed, 6 existing skips, 0 failed. Core guards and docs self-check passed.

Review fail-proof: temporarily removed both post-await validity checks and the XAML FailurePurpose. dotnet test Tests/CCP.Avalonia.Tests --no-restore --filter FullyQualifiedName~BannerMotionTests compiled and returned exit 1: 13 cases, 5 failed, 8 passed. The four PendingBrowserFailureStopsOnPanicOrOwnerClose cases failed, and WebFailureCopiesTheLinkAndPanicClosesThePrompt failed its complete-copy assertion. Production sources were restored in finally; no test was modified for the fail-proof.

Restored review validation commands and result tails:

```text
dotnet build CCP.Core --no-restore: Build succeeded; 0 errors.
dotnet build CCP.Avalonia --no-restore: 201 warnings; 0 errors.
dotnet build Tests/CCP.Avalonia.Tests --no-restore: 145 warnings; 0 errors.
dotnet test Tests/CCP.Core.Tests --no-build --no-restore: Failed 0, Passed 4635, Skipped 6, Total 4641.
dotnet test Tests/CCP.Avalonia.Tests --no-build --no-restore --filter 'FullyQualifiedName~BannerMotionTests|FullyQualifiedName~TestIsolationTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~FavoritesRailShellTests': Test Run Successful; Total tests 33; Passed 33.
bash .github/scripts/core-guards.sh: All Core guards passed.
python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.
```

## Local second-gate follow-up

Accepted P2: the nameless header now gates only on CoreAccount.IsLoggedIn (MainShellWindow.Marquee.cs:204). WPF MainWindow/MainWindow.Marquee.cs:144 accepts a stored settings id; the Avalonia head deliberately requires proven identity (MainShellWindow.Login.cs:36; Platform/AccountSeed.cs:88-103). This keeps the banner consistent with the Settings Login button when a stored id survives without a token or authenticated provider. Core and WPF code remain unchanged.

WelcomeFollowsIdentityChangesAndProviderFallback now seeds CoreAccount.UnifiedUserId for authenticated cases and adds stale-id-only coverage with CoreAccount.IsLoggedIn false. It asserts the complete expected banner and Settings Login button visibility. Existing assertions were retained; no localization key was added.

Fail-proof against a4ad95f7a's gate, with the new tests in place: dotnet test Tests/CCP.Avalonia.Tests --no-restore --filter FullyQualifiedName~BannerMotionTests.WelcomeFollowsIdentityChangesAndProviderFallback returned exit 1, 4 total, 3 passed, 1 failed. Only source=stale-id failed. The production gate was then fixed, without modifying the test expectations.

Ledger: shell-marquee is stub because the retained notes list missing WPF behavior. All prior a4ad95f7a notes remain intact, this gate's evidence is appended, and the original 74ac6ce3d notes have been restored as explicitly historical evidence. Current coverage above supersedes that historical missing list. Deferred part B remains unchanged.

Local second-gate final commands and result tails:

```text
dotnet build CCP.Core --no-restore: Build succeeded; 0 warnings; 0 errors.
dotnet build CCP.Avalonia --no-restore: 201 warnings; 0 errors.
dotnet build Tests/CCP.Avalonia.Tests --no-restore: 145 warnings; 0 errors.
dotnet test Tests/CCP.Core.Tests --no-build --no-restore: Failed 0, Passed 4635, Skipped 6, Total 4641.
dotnet test Tests/CCP.Avalonia.Tests --no-build --no-restore --filter 'FullyQualifiedName~BannerMotionTests|FullyQualifiedName~TestIsolationTests|FullyQualifiedName~AccountSeedTests|FullyQualifiedName~ShellSettingsWiringTests': Test Run Successful; Total tests 80; Passed 80.
bash .github/scripts/core-guards.sh: All Core guards passed.
python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.
Ledger structural check: one stub row; a4ad95f7a and 74ac6ce3d notes preserved in full.
```

## Support-link render gate

Accepted P2. MainShellWindow.axaml:961 now uses Stretch and a single measured DockPanel, with the lead docked left and ellipsis on both lead and link text. This reuses the sibling web beat's Avalonia workaround for WPF MainWindow.xaml:1137-1146, whose whole-line CharacterEllipsis includes the hyperlink. URI, copy, fonts and launch behavior are unchanged; Core and WPF files remain untouched.

SupportLinkFitsAndEllipsizesInTheRenderedShell checks attachment, visible link width, text advance staying inside the beat and narrow ellipsis. Its full case is 1661x1002 with the normal 744px cap; its narrow case is 1131x700 with a 440 design-pixel cap on the banner. That additional constraint is deliberate: the shell normally scales a fixed 1585px canvas rather than reflowing it. The tests stop rotation and show the actual support beat. The normal header may also legitimately ellipsize because neighboring chrome takes space.

Fail-proof: temporarily restored the reviewed 1c947d1e5 support block and ran dotnet test Tests/CCP.Avalonia.Tests --no-restore --filter FullyQualifiedName~BannerMotionTests.SupportLinkFitsAndEllipsizesInTheRenderedShell. Both finalized cases failed their link-attachment assertion (exit 1, 2 failed). The fix was restored in finally, without changing the assertions. With the fixed layout, both cases pass.

- [Full shell, 1661x1002](support-full.png): produced through the shipped --render-view MainShellWindow path in an isolated CCP_USERDATA_DIR.
- [Narrow shell, 1131x700](support-narrow.png): captured from the real shell by the focused test with CCP_SUPPORT_PROOF_DIR set to this directory.

Both PNGs were opened and read. The lead and underlined link are rendered, the URL truncates with an explicit ellipsis and stays inside the pill, and no raw localization keys appear in the header. This is Linux drawing/layout proof, not a side-by-side WPF render. The app-wide icon census is outside this header fix. The ledger remains stub and all earlier notes are retained.

Final support-layout commands and tails (dotnet is /tmp/ccp-dotnet/dotnet):

```text
dotnet build CCP.Core --no-restore; dotnet build CCP.Avalonia --no-restore; dotnet build Tests/CCP.Avalonia.Tests --no-restore: each exit 0, 0 errors.
dotnet test Tests/CCP.Core.Tests --no-build --no-restore: Failed 0, Passed 4635, Skipped 6, Total 4641.
dotnet test Tests/CCP.Avalonia.Tests --no-build --no-restore --filter 'FullyQualifiedName~BannerMotionTests|FullyQualifiedName~TestIsolationTests|FullyQualifiedName~FavoritesRailShellTests|FullyQualifiedName~ShellResizeMaximizeTests': Total tests 45; Passed 45.
After baseline comparison, restored BannerMotionTests + TestIsolationTests: Total tests 28; Passed 28. Final focused render cases (with PNG encoder API and equivalent collection assertion): Total tests 2; Passed 2.
CCP_USERDATA_DIR=/tmp/ccp-support-render dotnet CCP.Avalonia/bin/Debug/net10.0/CCP.Avalonia.dll --render-view MainShellWindow evidence/shell-marquee-recut/support-full.png: exit 0; rendered 1661x1002; no binding errors. Narrow frame: 1131x700.
bash .github/scripts/core-guards.sh: All Core guards passed. python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.
```

Navigation limitation: --nav-check exits 1 with "OpenDeviceSettings reveals Devices (saw data)" and "an unknown section still opens the door and changes nothing". Rebuilding the exact reviewed 1c947d1e5 production XAML in an isolated profile reproduces the same two failures. The fixed XAML was restored and rebuilt, and the 28 focused/isolation cases were then rerun green. No unrelated Devices/data routing changes were made.
