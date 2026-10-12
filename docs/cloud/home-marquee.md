# home-marquee: Home actions, browser card, marquee and server banners

Ports the WPF 7.1.5 Home actions, the Home browser card, the scrolling marquee and the server update banner to the
Avalonia head. Trunk commits used as hints and adapted, not copied: 7dce857a8, e3109afdf, 376305e62, d30fcf576, cab1eb98f.

- **Server banners** (`MainShellWindow.ServerBanners.cs`, WPF `MainWindow.Marquee.cs:649-826`): the update banner 5 s after the window opens (newer build only: `UrgentUpdate`, "Update to v…", download or install tooltip), the marquee line at 3 s and then every 5 min. The fetches are new `V2AuthService.GetUpdateBannerAsync` / `GetMarqueeMessageAsync` in Core, on the same client the announcement already uses, so tests run against a fake feed (cab1eb98f, d30fcf576).
- **URL-first update pill** (`MainShellWindow.AccountShell.cs`, WPF `MainWindow.AccountShell.cs:482-505`): a banner URL opens through `ExternalOpener` first; the updater runs only when there is none or it fails to open.
- **Home marquee** (`SettingsTabView.Marquee.cs`, WPF `StartMarqueeAnimation` :1139): the saved message, doubled and scrolled at 80 px/s on a `BeatLoop` (shared 30 fps beat, never an infinite Animation); parked under the ambient gate. The placeholder `{loc:Str}` text and the two blur Effects over the moving strip are gone (a `BoxShadow` on the border instead) (d30fcf576).
- **Home actions** (`SettingsTabView.axaml.cs`): ? box -> today's free feature's page (Presets.cs:1084); intake pass face -> the intake page's gated launch, never a logo poke, "(?)" excluded (UiUpdates.cs:1911); Webcam pill -> bark + Settings > Devices (:1475); System pill gains its bark (:1450); App info -> one feature popup (:1482); Rich Presence quick switch refused without a linked Discord (AccountShell.cs:279) (7dce857a8).
- **Browser card** (`SettingsTabView.BrowserCard.cs`, `Views/Controls/WebHostMedia.cs`): mute flips/saves the flag, the glyph follows and the mute script is re-sent after every navigation over `WebHost.InvokeScriptAsync` (a string answer, no `chrome.webview`); webcam pill with consent, calibration prompt and calibration; BambiCloud override; the load prompt loads the selected site behind the offline gate (e3109afdf, 376305e62).
- Not ported (greyed with "not on this build"): Pop out (NativeWebView cannot be re-parented live) and Enhance if possible (no enhance bridge). Not ported: the marquee interludes, the Profile tab's twin Rich Presence switch re-sync.

## Verification limits

- `dotnet test Tests/CCP.Avalonia.Tests --filter HomeMarqueeTests`: 11 passed. `dotnet test Tests/CCP.Core.Tests --filter ServerBannerFeedTests`: 2 passed.
- Fail-first: with each ported handler put back to the base's no-op (and the Core fetches returning null), 9 of the first 10 Home tests and both Core tests fail; the one that passes is the "older build stays hidden" guard. The intake-face test (added after) fails the same way against a no-op launch (expected 1 owned window, got 0).
- Built (0 errors): CCP.Core, CCP.Avalonia, CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests. `bash .github/scripts/core-guards.sh`: all passed.
- Not run: the real network fetches (tests use a fake feed), a real web engine (headless has none, so the mute script and the load prompt's visible state are exercised only through the no-engine path), the webcam start/calibration path, and any Windows-only path (`OperatingSystem.IsWindows()` branches compile here, cannot run). No `--render-all` frame was inspected.
