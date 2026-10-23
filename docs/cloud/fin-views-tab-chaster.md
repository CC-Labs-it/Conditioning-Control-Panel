# fin-views-tab-chaster

- Compared the checked-out WPF reference at `888cdbffe4d2ecf3967cee4a4334557ee533e54d` with the actual Avalonia head. The ledger's first missing list was stale: LockTitle, menu, help and price editing were already wired. Retained these files and merged behavior into them.
- Added day/backlog limits with the WPF ranges and steps, effective captions, pending raise values and local landing times, immediate decreases, backlog carry, preset refresh and relock opt-in. Reused Core TabLimits/LimitChange; no engine change.
- Added the real row trailer user path: 150 ms hover delay, 140 ms leave grace, pointer transfer onto the card, per-row fallback art/flavour/tier/explanation/live figure, edit/reset refresh, and hide/detach/panic dismissal. Registered `chaster-trailer` in PanicSurfaces.
- Added hero art/fade, clickable paper balance with landing line and CLEAR/UNPAID/CREDIT stamp, tonight's positive-balance calendar tag, and the four footer facts with Fluent icons. Added the nothing-counts hint's scroll-to-keys action.
- Added static spiral/glow ground with the existing fog/dust canvas on its shared clock. Replaced the inherited ladder scrap Effect with a sibling BoxShadow so no Effect sits beneath the looping layer.
- Adapted imports from read-only `parity/trunk` tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`: limits and paper/art presentation from `d5052645908a78cf990a3b80f8c6d1bcb5c1cc4f`; trailer interaction from `2997ed7bcecc1f412714d51477aa2d52794e92b0`. Dropped calls to unimported Fx; existing head implementations win.
- Added eight focused headless tests for limit requests/settlement/presets/relock, trailer content/timing/edit/reset/lifetime/panic, paper balance/stamps/click, footer and ground composition, raffle-day/account changes, lock-number refresh and hint navigation. The fixture uses the existing sandbox and memory-only tokens; it now reads real effective Core limits and restores the settings it changes.

## WPF gap inventory and scope decision

References at the pinned base: ChasterTabView.xaml (ground :257-313, hero/tag/calendar :417-580, footer/limits/trailer :986-1112), ChasterTabView.xaml.cs (RefreshTag :448, limits :760-848, trailer :1376-1460), all of ChasterTabView.Fx.cs, plus Help/PriceEdit/Ladder/Scrap/Mood partials and Controls/ChasterTrailerView.cs. All remaining effects have live callers; P122 does not apply.

Initial missing behaviors:

- Ground gradient, spiral, glow and ambient dust/fog; hero image/fade; paper amount, landing text, stamp and receipt click; tonight's balance tag; limits controls and pending captions; linked footer facts; nothing-counts hint navigation. Implemented, apart from ground vignette and motion details listed below.
- Trailer hover/grace/transfer, contents, live figures, edit/reset refresh, lifetime, browser scenes and visual motion. Implemented the interaction and WPF still-image fallback. Browser vignettes and motion remain.
- Calendar raffle counted-day dots/key and in-place refresh: implemented, including account-epoch filtering. Bent crosses, foil sticker, double ring and all associated marker/sheet/tag motion remain.
- FxInit/FxOnShown/FxPark: hover lifts, press squish, card stagger, clock glow/count-up, spiral turn, sheen, art drift, paper swing, key breathing. Only ambient fog/dust parking and pre-existing LockTitle motion are present.
- FxBooked, FxPreset/FxKeyTurned, FxRow, FxSwitch/FxConsentShown/FxConsentOk, FxLinking/FxLinked, FxBill/chevron/tag tug/cap bloom, calendar draw-in/bob/pulse/comet/flutter/nudge, trailer drift/pan and 1.7 s floating figure. Remaining, including title booking sparks.
- The head's existing lock-change callback omitted WPF's RefreshNumbers after a push: implemented and covered by a focused lock-change test.

The remaining live visual timelines and browser scene host cannot fit faithfully alongside these user-visible controls and behavior tests under the 1000-added-line cap. The ledger stays `stub` with an explicit Missing list; this is not a dependency-only NOOP and no dead-WPF exemption is claimed.

## Verification limits

- `dotnet --version`: 10.0.402, installed with the requested channel-10.0 installer. Its latest-version probe returned 403 first; the SDK archive then installed successfully.
- `dotnet build <project> -c Release --no-restore`: exit 0 and 0 errors for CCP.Core, CCP.Avalonia, and all 13 workflow-listed test projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests and ConditioningControlPanel.Tests. The WPF test project used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; Windows code compiled but was not run on Linux. Restores and initial builds also succeeded.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 3545 passed, 5 skipped by the existing Vosk tests, 0 failed. The model is unavailable in this environment; no test was changed to skip it.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~ChasterMenuTests`: exit 1 on the initial 8-test run. All stopped at the known cloud `Could not create glyphTypeface` font failure before behavior assertions (including the two pre-existing tests). The final 10-test class compiled with 0 errors; it was not rerun after that environment diagnosis. No behavior pass or fail-before-fix proof is claimed; supervisor headless execution is still required.
- Font-independent Avalonia filter covering `AmbientBeatLoopsTests.NoNew*`, `TheScanSeesEverySpelling`, and PanicSurfaces registration/order/starter checks: exit 0, 8 passed, 0 failed. The first run correctly failed because the removed Chaster Effect left a stale ratchet entry; removing that entry tightened the existing guard.
- `dotnet run --no-build --project CCP.Avalonia -c Release -- --render-view ChasterTabView <temporary-output>`: exit 0, 1100x780 PNG, 281004 bytes, no reported binding errors. The existing render sample scrolls to the menu; it does not provide visual proof of the new popup, hero tag or expanded limits. Those paths require the headless interaction tests on a runner with fonts.
- `git diff --check`: exit 0. No full Avalonia suite, live desktop, actual Chaster account, Windows runtime, browser vignette or Vosk model test ran. No tests were weakened or deleted; the Effect allowlist only shrank.

## Assumptions

- Follow WPF's still-art trailer when a browser runtime is unavailable; keep the browser-scene gap explicit instead of adding a second web host in this capped unit.
- Preserve the existing Fluent calendar symbols and straight marker strokes until the calendar detail/motion work can be ported together.
- Prioritize remaining controls, copy, limits and user paths before the larger decoration timelines; keep `stub` until every live WPF behavior is reachable.
