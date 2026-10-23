- Added holder cards to the existing shared Leash drawer section. The page and friends popup now show incoming offers, the own card, then held accounts in snapshot order. Cards and open sheets survive snapshot and receipt repaints; vanished accounts disappear, and hidden hosts unsubscribe.
- Adapted WPF holder actions and copy: assignment presets/replacement warning, intensity-filtered punishments, three-per-day cap and waiting count, DND controls, stickers/praise/pardon/linked Chaster credit, ten-second tug throttle, video picker with digit-only Hypnotube ids and the held account's duration ceiling. Good replies close sheets; errors leave them open; feedback lasts three seconds. Receipt support controls the newest four step timelines and honest send wording.
- Added a mouse-confirmed holder release window; keyboard presses cannot release. Panic closes the confirmation without sending. Added the surface to the panic registry and safety-critical registry test.
- Added own-card sticker shelf (newest fourteen, sender tooltips, Fluent icons) and a read-only holder preview using Core's last-sent report. Preview state survives updates; no week history is fabricated.
- Added ten focused `LeashHolderTests` covering page reachability/state retention, preset calls/replies, DND/intensity/caps/link rules, video grammar/ceiling, receipt lifecycle, tug/release/panic, shelf/preview, off/failed service feedback, feedback expiry, and report/week/assignment rendering. Existing tests are preserved.
- Sources: frozen WPF files at `67181df5b4b5695ee5c30bc407f715daf142e3f7`: `LeashTabView.xaml(.cs)`, `LeashDrawerSection.cs`, `LeashHolderCard.cs`, `LeashSelfCard.cs`, `LeashCutConfirmWindow.cs`; gap audit also read `LeashSurfaces.cs`, `LeashSnapCard.cs`, and `Explain/LeashExplainer.cs`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code was imported or overwritten.

## Gap audit and ledger decision

The base omitted held-account cards entirely: report figures, assignment state, week strip, DND line, Assign/Reward/Punish sheets and sends, video selection/cap, tug throttle, receipt rows/repaints, release confirmation, replay and help. Its own card also omitted the sticker shelf and holder preview. This branch implements those functional holder paths except replay/help, and adds shelf/preview.

Still missing relative to live WPF paths (row remains **stub**, never verified):

- Friend-card Offer chip, eligibility/sent/holding/greyed states and receipt step, with the holder's first-offer explainer/read clock and recorded completion.
- Page/self/ask/holder `?` role explainers, standalone explainer dismissal/return behavior, drawn four-step art and entrance. The existing ask retains its first-ask read clock and intensity/answer behavior.
- Accepted-offer snap card on both sides and holder-menu replay (including animation, recap, close/Escape and sounds).
- App-wide newly arrived offer queue/show-on-return/rebind handling, accepted/declined/ended/reward/task/tug event reactions, held notices/seen accounting and window wobble from `LeashSurfaces`.
- WPF visual parity: downloaded avatars, avatar heart tag, chain/card/sticker artwork (shelf uses Fluent icons and reward presets use localized labels), minutes goal ring, full marked week cells, receipt item icons, action icons/layout and LeashFx/sound cues. Basic report values, assignment state and seven-day labels/colors are present.
- Panic-key five-second hold-to-cut route and confirmation/ring; the existing immediate unpriced Cut path stays available, and the unsupported hold hint stays hidden.
- Conditioning-minute reporting/Minutes assignment completion still depends on the unported `feature-day-log-core` lane, as documented in `LeashHead.DayInputs`; other gaps above are independent, so this is not a dependency-only NOOP.

WPF live callers are `LeashTabView` -> `LeashDrawerSection`, `FriendsDrawer` -> section/OfferChipFor, `LeashSelfCard` -> Shelf/Preview, holder menu -> release/replay, and `MainWindow.Leash` -> `LeashSurfaces.Init/Rebind`. P122 does not apply. No claim of render-all or visual parity is made.

## Verification limits

All commands used .NET SDK 10.0.402. `DiffEngine_Disabled=true` was set for tests.

- `dotnet build <project> -c Release -m:1`: exit 0, zero errors for all thirteen required Linux projects: `CCP.Core`, `CCP.Avalonia`, `Tests/CCP.Core.Tests`, `Tests/CCP.Core.Settings.Tests`, `Tests/CCP.Core.Content.Tests`, `Tests/CCP.Avalonia.Tests`, `Tests/CCP.Avalonia.CatalogueStartup.Tests`, `Tests/CCP.Avalonia.Consent.Tests`, `Tests/CCP.Avalonia.GeneralSettings.Tests`, `Tests/CCP.Avalonia.Language.Tests`, `Tests/CCP.Avalonia.Report.Tests`, `Tests/CCP.Avalonia.Scheduler.Tests`, `Tests/CCP.Avalonia.Snapshot.Tests` (each project used its same-named csproj). The final main Avalonia test build reported 50 warnings, 0 errors. An earlier CatalogueStartup build hit MSB4166 (worker exited); its final single-worker retry passed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings, 0 errors. This also compiled the WPF app. Neither was run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.` The XAML tripwire checked 424 references against 1591 app and 1880 Core types.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; `Failed: 0, Passed: 2812, Skipped: 5, Total: 2817`, duration 58 seconds. The existing five speech/Vosk tests skipped for the unavailable model; tests were not altered or disabled.
- `git diff --check`, single-row check, and added-content privacy scan: exit 0. Frozen WPF and Core source files are unchanged.

Avalonia tests were **compiled, not run**, following the supplied cloud font-creation limitation. The shared headless harness at this base does not contain the named `cloud-test-fonts` fix. Required supervisor filter: `FullyQualifiedName~LeashHolderTests|FullyQualifiedName~LeashPageTests|FullyQualifiedName~LeashAskTests|FullyQualifiedName~PanicSurfacesTests`. No runtime pass or fail-before-fix claim is made for these new surfaces. Tests use the existing assembly's temporary `CCP_USERDATA_DIR` profile and headless windows; no real desktop or profile is used.

Not run: Windows runtime, live server, render-all, visual comparison, full Avalonia suite, or Vosk recognition with a downloaded model. Generated test binaries were removed between builds to fit disk limits; no tracked assets or source were removed. The original checkout's fetch failed on a read-only filesystem; the authorized fresh-clone fallback was verified at the exact required base before edits.

## Assumptions

- Prioritized holder interaction paths, receipt lifecycle and own-card shelf/preview under the 1,000-added-line cap. Kept the ledger stub with the remaining live WPF behaviors listed above.
- Reused existing Friends catalogue picker source, Core grammar/rules and the last report sent by Core. No new service abstraction or configuration.
- Used Fluent sticker icons and localized reward labels; WPF's bespoke artwork remains an explicit visual gap.
