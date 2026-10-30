# Shell marquee re-cut onto 7.2

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad. Old lane: 75bfcb6b1ec2.
The full old lane is +846/-40 before adapting to 7.2, so this cut prioritizes safe links and header behavior.
No WPF files or existing tests changed. No new localization keys; the three browser strings and signed-in/no-name string already exist in all ten Core language files.

| Behavior | WPF specification | This cut or unchanged base | Old lane evidence |
| --- | --- | --- | --- |
| Motion Off swaps | MainWindow/MainWindow.Marquee.cs:432 | CCP.Avalonia/Views/Windows/MainShellWindow.Marquee.cs:166 | MainShellWindow.Marquee.cs:160 |
| Welcome identity, blank provider fallback, sign-in repaint | MainWindow/MainWindow.Marquee.cs:125; MainWindow/MainWindow.Login.cs:57,123,328 | MainShellWindow.Marquee.cs:186; MainShellWindow.Login.cs:53 | MainShellWindow.Marquee.cs:194; MainShellWindow.Login.cs:53 |
| 31px/744px/14px banner, support link | MainWindow/MainWindow.xaml:1102,1133,1143 | MainShellWindow.axaml:952,968 | MainShellWindow.axaml:941,959 |
| Browser failure copies URL and explains | Helpers/BrowserLauncher.cs:63 | CCP.Avalonia/Controls/SafeHyperlinkButton.cs:41; MainShellWindow.axaml:1015 | SafeHyperlinkButton.cs:41 |
| Sandbox refusal has no clipboard or dialog side effect | External opening remains gated; stricter than WPF BrowserLauncher.cs:63 | SafeHyperlinkButton.cs:47 | SafeHyperlinkButton.cs:47 |
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

The entire Avalonia suite was compiled; only the 54 selected tests (and the final 21-case subset) were run. WPF, native Windows browser fallbacks and an interactive live browser were not run. docs/avalonia-progress.html was not changed.
