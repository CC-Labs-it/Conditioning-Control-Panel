# shell-browser, row2

Base: a58c5d0d1fab4bc93bb21d2eb45da41180ddd148.
WPF references below use `ConditioningControlPanel/`. Avalonia references use `CCP.Avalonia/`.

## Base audit and part B

The old row notes are partly stale. This audit reads the base, not the other open lanes.

| Item in missing notes | Base evidence and remaining work | WPF specification |
| --- | --- | --- |
| Fullscreen, Escape, topmost rental, overlay reassertion | Missing from the Home browser. `Views/Windows/MainShellWindow.Browser.cs:127` explicitly accepts but ignores fullscreen; `Views/Controls/WebHost.axaml.cs:34` documents the absent fullscreen-element signal. Other video windows do not implement this browser path. | `MainWindow/MainWindow.Browser.cs:2818`, `:2863`, `:2939`, `:2953`; `Services/Notifications/OverlayService.cs:121`, `:3104` |
| Leash and stop hooks | Partly present: `Views/Windows/MainShellWindow.LeashGate.cs:227` hides the browser and `:239` restores it. Browser fullscreen teardown and remote-owned page stop remain absent. | `MainWindow/MainWindow.Leash.cs:184`; `MainWindow/MainWindow.StartStop.cs:570`; `MainWindow/MainWindow.Browser.cs:3228`; real callers `Services/RemoteControlService.cs:1001`, `:1131`, `:1164`, `:1410` |
| Mod switch and site sync | Partial: `Views/Tabs/SettingsTabView.BrowserCard.cs:60` only repaints the card; `:160` handles hiding a selected BambiCloud radio. The live-known-site guard and exact mod-default radio selection are missing. | `MainWindow/MainWindow.Browser.cs:518`, `:547`; real mod-switch caller `MainWindow/MainWindow.xaml.cs:3252`, `:3259` |
| Popup and ad rules | Missing at base: native subscriptions in `Views/Controls/WebHost.axaml.cs:327` forward navigation, messages and resource headers, but not new windows. Ported below. Request blocking and CSS remain part B. | `Services/Browser/BrowserService.cs:538`, `:580`, `:711`, `:1452` |
| HTTPS external fallback | General shell failure path missing: `Views/Windows/MainShellWindow.Browser.cs:57` only returns false on navigation failure; `:130` returns that result. | `MainWindow/MainWindow.Browser.cs:487` |
| Monitor mirror and zoom | Missing browser implementation; `Views/Controls/WebHost.axaml.cs:34` states no zoom API. | `MainWindow/MainWindow.Browser.cs:2828`, `:3131`; `Services/Browser/BrowserService.cs:107` |
| DOM avatar autoplay and playlist takeover | Missing: `Views/Windows/MainShellWindow.Browser.cs:127` accepts and ignores the request. Navigation completion in `Views/Controls/WebHost.axaml.cs:82` is later than DOM parsing. | `MainWindow/MainWindow.Browser.cs:793`, `:823`, `:1017` |
| Reporter, ownership, audio sync and rearm | Missing Home browser wiring: `Views/Windows/MainShellWindow.Browser.cs:76` completion only sets status and catalogue lookup. Leave to the media lane, including #3637 need-host-audio-sync. | `MainWindow/MainWindow.Browser.cs:81`, `:113`, `:147`; `Services/Browser/BrowserMediaService.cs` |
| Remote takeover stop | Missing shell stop API, as above. Plain navigation at `Views/Windows/MainShellWindow.Browser.cs:130` does not claim remote ownership. | `MainWindow/MainWindow.Browser.cs:3147`, `:3228`; `Services/RemoteControlService.cs:1001` |
| Native crash and readiness retry | Missing retry: `Views/Controls/WebHost.axaml.cs:310` catches constructor failure into a fallback, but does not rebuild a crashed adapter. | `MainWindow/MainWindow.Browser.cs:178`, `:202` |
| Document-created and iframe mute/fullscreen | Partial mute already exists: `Views/Controls/WebHostMedia.cs:21` traverses same-origin frames, and `Views/Tabs/SettingsTabView.BrowserCard.cs:35` reapplies after completion. Parse-time and cross-origin coverage remain missing; `Views/Controls/WebHost.axaml.cs:257` explains the absent document-created seam. | `Services/Browser/BrowserService.cs:259`; `MainWindow/MainWindow.Browser.cs:793` |
| Request ad blocking and ad CSS | Missing: `Views/Controls/WebHost.axaml.cs:113` stamps request headers only. Popup policy does not claim to filter subresources or hide page elements. | `Services/Browser/BrowserService.cs:580`, `:711`, `:1452` |

Already on base: initial mod URL `Views/Windows/MainShellWindow.Browser.cs:94`,
site/reload/offline gate `:108`, `:117`, popout/focus/reparent/return
`Views/Windows/MainShellWindow.BrowserPopout.cs:19`, and document mute
`Views/Tabs/SettingsTabView.BrowserCard.cs:75`.
WPF: `MainWindow/MainWindow.Browser.cs:225`, `:589`, `:3264`, `:2704`, `:2603`.

## Delivered part

`CCP.Core/Services/Browser/BrowserPopupRule.cs:54` copies WPF's domain list,
host-boundary matching, URL patterns and HypnoTube document bypass.
Source: `ConditioningControlPanel/Services/Browser/BrowserService.cs:31`, `:538`, `:627`.
No WPF code is changed.

`CCP.Avalonia/Views/Controls/WebHost.axaml.cs:329` handles native new-window events
through `:370`. `Views/Tabs/SettingsTabView.BrowserCard.cs:35` opts in only the Home
browser. Allowed targets navigate the same host, blocked targets leave it alone,
and both are handled so neither creates an OS window. The live completed URL wins
over the originally requested URL when deciding the partner bypass.
WPF: `Services/Browser/BrowserService.cs:538-562`, `:665-675`.

The full row needs the independent work above and remains stub. Native desktop
popup delivery is not proven by headless tests; the native event hookup is compiled.
No new copy or localization keys.

## Validation

SDK: .NET 10.0.403, installed with dotnet-install channel 10.0 using the release feed
(the default CI feed returned 403).

| Command (dotnet at /tmp/dotnet/dotnet) | Result tail |
| --- | --- |
| `dotnet build CCP.Core --no-restore` | 0 warnings, 0 errors |
| `dotnet build CCP.Avalonia --no-restore` | 201 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.Tests --no-restore` | 144 warnings, 0 errors |
| `dotnet test Tests/CCP.Core.Tests --no-restore` | 4651 passed, 6 existing skips, 0 failed |
| `dotnet test Tests/CCP.Avalonia.Tests --no-build --filter 'FullyQualifiedName~BrowserPopupTests\|FullyQualifiedName~TestIsolationTests'` | 15 passed, 0 failed |
| `dotnet test Tests/CCP.Avalonia.Tests --no-build --filter 'FullyQualifiedName~Browser\|FullyQualifiedName~TestIsolationTests'` | 20 passed; 19 stopped at missing Segoe UI Black glyphTypeface |
| `bash .github/scripts/core-guards.sh` | All Core guards passed |
| `python3 .github/scripts/avalonia_docs.py` | avalonia_docs self-check OK |

Fail-proofs: invert the Core popup decision, run `BrowserPopupRuleTests`: all 16
cases fail assertion checks. Restore. Disable the Home card's RouteBrowserPopups
hook, run `BrowserPopupTests`: all 3 fail Assert.True. Restore. Final runs above
use restored production code. New tests: `Tests/CCP.Core.Tests/BrowserPopupRuleTests.cs:8`
and `Tests/CCP.Avalonia.Tests/BrowserPopupTests.cs:29`. No existing tests changed.

The 19 font-blocked shell tests were compiled, not successfully run through their
behavior assertions. The new card tests and isolation tests ran successfully.
Native desktop popup delivery remains unproven.
