# Chaster trailer re-cut onto 7.2

Base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`.
Old lane: `7cf2cee4dcfa`, compared with `f7be538c73f0`.
Row: `ctrl-chaster-trailer-view`, wired through menu row pointer events.

Part A ports the still-picture card. The complete old lane is 854 additions plus
12 deletions, so native browser playback remains part B under the 600-line cap.
No WPF, Core, localization, Scrolller or existing stop-order changes.
No new copy keys. Existing localized flavour and explanation keys are reused.

All WPF paths below are relative to `ConditioningControlPanel/`.
All final paths below are relative to `CCP.Avalonia/`.

| Behavior | WPF specification | Re-cut implementation |
| --- | --- | --- |
| Menu user path, already on base | Views/Tabs/ChasterTabView.xaml.cs:1256 | Views/Tabs/ChasterTabView.Menu.cs:52; base :52 |
| Effective prices, already on base | Views/Tabs/ChasterTabView.xaml.cs:1348 | Views/Tabs/ChasterTabView.PriceEdit.cs:33; base :34 |
| Row copy/art/tier, already in Core | Views/Tabs/ChasterTabView.xaml.cs:1425 | CCP.Core/Services/Chaster/TabMenuCopy.cs:13, :84, :116, :148 |
| Open/close delays | Views/Tabs/ChasterTabView.xaml.cs:58 | Views/Tabs/ChasterTabView.Trailer.cs:27 |
| Hover, immediate retarget, card hold | Views/Tabs/ChasterTabView.xaml.cs:1378 | Views/Tabs/ChasterTabView.Menu.cs:146; ChasterTabView.Trailer.cs:71 |
| One 320 px card, bottom placement | Views/Tabs/ChasterTabView.xaml:1076 | Views/Tabs/ChasterTabView.axaml:677 |
| Art, flavour, why, badge, effective price | Views/Tabs/ChasterTabView.xaml.cs:1425 | Views/Tabs/ChasterTabView.Trailer.cs:106 |
| Edit refresh | Views/Tabs/ChasterTabView.PriceEdit.cs:158 | Views/Tabs/ChasterTabView.PriceEdit.cs:155 |
| Reset refresh | Views/Tabs/ChasterTabView.Help.cs:48 | Views/Tabs/ChasterTabView.Help.cs:43 |
| Drift/pan, motion gate, 1700 ms figures | Views/Tabs/ChasterTabView.Fx.cs:65, :593, :626 | Views/Tabs/ChasterTabView.Trailer.cs:146; reuses Helpers/BeatLoop.cs:18 and Controls/ChasterBookedFlash.cs:41 |
| Hide cleanup | Views/Tabs/ChasterTabView.xaml.cs:103, :1449 | Views/Tabs/ChasterTabView.Trailer.cs:56, :128 |
| Detach/unlink/panic cleanup | Views/Tabs/ChasterTabView.xaml.cs:1449 | Views/Tabs/ChasterTabView.Trailer.cs:58, :139; ChasterTabView.axaml.cs:132; Views/Windows/PanicSurfaces.cs:87 |

The old lane's matching card implementation is
`CCP.Avalonia/Views/Tabs/ChasterTabView.Trailer.cs:26-216`.
The 7.2 base already owns menu, price editor, artwork loading, shared animation
clock and booked-flash rendering. Only the missing card connections are ported.
Detach, unlink and panic also cancel pending hover, a stricter cleanup than WPF.

Part B: native scene player and latest-request mount, including edited scene
figures, failure fallback, URL fence and popup suppression. WPF reference:
`Controls/ChasterTrailerView.cs:74-263` and
`Views/Tabs/ChasterTabView.xaml.cs:1420-1439`.
Reuse candidates at `7cf2cee4dcfa`: `CCP.Avalonia/Controls/ChasterTrailerView.cs:41-166`,
`CCP.Avalonia/Platform/TrailerBrowserPolicy.cs:14-102`,
`CCP.Avalonia/Views/Controls/WebHost.axaml.cs:209-252`, and
`CCP.Avalonia/Views/Tabs/ChasterTabView.Trailer.cs:113-163` for prewarm/reparenting.
Native Windows/Linux browser settings, crash handling and reparenting need live validation.

Tests: `Tests/CCP.Avalonia.Tests/ChasterTrailerTests.cs:53` (hover), `:100`
(card content/geometry/figures), `:128` (edit/reset), `:145` (motion), `:167`
(hide/detach/unlink/panic and pending hover). Shared dispatcher, Skia and suite isolation.
Headless tests execute the user pointer path and step grace timers without sleeping.
Cloud fonts use shipped Fredoka plus system TTF fallbacks via a temporary fontconfig
file outside the repository; the image's WOFF2 default cannot be decoded by Skia.

Fail-proofs: 18 deliberate regressions each produced assertion failures, then were
restored: hover wiring, 150 ms delay, 140 ms grace, immediate retarget, pointer hold,
art, flavour, tier, first booked figure, edit refresh, reset refresh, motion gate,
drift amplitude, hide, detach, unlink, panic and pending-hover cancellation.
The retarget proof checks displayed content and placement, as well as the row id.
Core suite: 4635 passed, 6 existing skips, 0 failed. No existing tests were changed.
Native playback is neither compiled nor run in this slice. The full Avalonia test
project is compiled; execution covers the new/affected suites and TestIsolationTests.
