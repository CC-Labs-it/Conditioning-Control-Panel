# fin-shell-multi-monitor

- Audited `shell-multi-monitor` against WPF at base commit `265a563dfa8e99071c6c272bb1d7b149620a57f2`.
- Kept the ledger row `stub` and made its remaining dependency explicit: the unported fullscreen signal and borderless host in `views-deeper-enhancement-player`.
- Made no application or test changes and added no surfaces or panic registrations: this is the task's dependency-only NOOP case.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; imported no code. Existing base implementations were retained.

## Remaining WPF behavior

All live behavior still absent from this row belongs to the Deeper EnhancementPlayer fullscreen lifecycle:

1. On the page entering HTML5 fullscreen, inspect `DualMonitorEnabled` and the active screen count; with the setting on and at least two screens, enable clone mode **before** entering the player's fullscreen host. Remember whether clone succeeded (`EnhancementPlayerWindow.xaml.cs:2001-2028`).
2. On the page leaving fullscreen, restore Extend only if this player enabled cloning, clear that ownership flag, then force-exit its fullscreen host (`:2030-2037`).
3. On player close, synchronously tear down the fullscreen host and restore Extend if this player still owns the clone operation (`:2616-2643`). App shutdown also restores Extend through the already-ported mirror service.

The Avalonia player's `OnVideoFullscreenChanged` is an uncalled logging stub (`CCP.Avalonia/Views/Deeper/EnhancementPlayerWindow.axaml.cs:1168-1192`); it has no fullscreen event subscription or host to enter/exit. Its closing path explicitly records that the fullscreen host is absent (`:1684-1701`). The separate `views-deeper-enhancement-player` ledger row also lists fullscreen as missing. The main browser already has its own script-based fullscreen signal and reparenting host; adopting that lifecycle in the player belongs to the player unit before its mirror calls can have a real user path. This is a feature dependency, not a claim that fullscreen is impossible with Avalonia.

Adding mirror calls to an uncalled stub would not wire the behavior. Implementing the player's missing fullscreen lifecycle would expand this unit into the separately tracked feature. Therefore the row stays `stub`; P122 cannot mark it `wired` while this live WPF caller remains absent.

## Existing coverage and dead WPF service

| WPF behavior | Existing Avalonia/Core implementation |
| --- | --- |
| Screen enumeration, primary/fallback selection, physical placement, read-only result mirrors and final typist activation | `ScreenList`, `BubbleCountResultWindow`, `LockCardWindow`; `MultiMonitorResultTests` |
| One decoded video shared across targeted screens | `MandatoryVideoOverlay` and one `VlcFrameSink`; `MultiMonitorVideoSurfaceTests`, `MandatoryVideoOverlayTests` |
| Skip clone with fewer than two active screens; idempotent clone/extend; failed-clone and failed-restore state; error descriptions; restore on dispose | Core `DisplayMirror`; `DisplayMirrorTests`; Windows-only `ScreenMirrorHead` |
| Browser fullscreen clones only when the content picker covers multiple screens; restore on page exit, Esc, outside close and app exit | `MainShellWindow.BrowserFullscreen`, `App` shutdown; `BrowserScreenMirrorTests` |

`App.GetAllScreensCached` and invalidation are WPF's enumeration mechanism. `ScreenList.Enumerate` reads the attached Avalonia TopLevel's current `Screens.All`; no additional application cache is needed.

Pinned caller proof (repository-relative commands):

```sh
git grep -n -E 'DualMonitorVideo|DualMonitorVideoService' 265a563dfa8e99071c6c272bb1d7b149620a57f2 -- ConditioningControlPanel/App.xaml.cs ConditioningControlPanel/Services/Flash/FlashService.cs
git grep -n -E '(App\.)?DualMonitorVideo[?]?\.(Play|PlayFile)' 265a563dfa8e99071c6c272bb1d7b149620a57f2 -- ConditioningControlPanel
git grep -n 'ScreenMirror' 265a563dfa8e99071c6c272bb1d7b149620a57f2 -- ConditioningControlPanel/Views/Deeper/EnhancementPlayerWindow.xaml.cs
```

The first command finds only the service property (`App.xaml.cs:1160`), creation (`:3141`), disposal (`:6304`) and an `IsPlaying` read (`FlashService.cs:1044`). The second has no matches (exit 1). A scan of every `DualMonitorVideo`/`DualMonitorVideoService` reference likewise finds no playback invocation outside the service's own implementation; its `PlayFile` only delegates internally to `Play`. The third finds the live player clone/restore calls (`:2025`, `:2034`, `:2639`). Do not invent a UI trigger for the unused service. This dead-service finding does not erase the live player dependency or justify P122 for the whole row.

## Verification limits

All commands used .NET SDK 10.0.402 and Release configuration; test execution exported `DiffEngine_Disabled=true`.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors (existing warnings).
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors (existing warnings).
- `dotnet build <test-project> -c Release`: exit 0, 0 errors for every Linux test project named in `.github/workflows/build.yml`: Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Report, Avalonia.Scheduler, Avalonia.Snapshot (all names end in `.Tests`).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors (512 existing warnings); compiled only, not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.` The first attempt was blocked by read-only temporary storage; the writable retry passed.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0, **2841 passed, 0 failed, 5 skipped**, total 2846. The existing Vosk integration tests self-skipped because the external model is unavailable (the supplied infrastructure note records its download as HTTP 403). No tests were altered or disabled.
- The first build sweep exhausted the worker's disk while copying native test dependencies (exit 134). After deleting only generated test outputs, the remaining Linux builds were rerun one at a time and all passed. No build settings or source were changed to obtain these results.

No new behavior was introduced, so no fail-before-fix test applies.

Windows topology switching and WPF UI cannot run on this Linux worker. Real mixed-DPI hardware, X11/Wayland placement, and any wrong-screen flash remain unobserved. The existing Windows SetDisplayConfig seam is compiled as part of the Avalonia build. The relevant headless classes are `BrowserScreenMirrorTests` and `MultiMonitorResultTests`; the supplied environment limitation requires compiling them without executing until the cloud test-fonts fix is available. No render-all claim or P122 promotion is made.

## Assumptions

- The player fullscreen lifecycle remains owned by `views-deeper-enhancement-player`; the smallest correct outcome here is the explicitly requested dependency-only documentation change.
- The named follow-up in the base row is the remaining live gap; OS hardware checks are verification limits, not an invitation to invent a cross-platform clone switch.
