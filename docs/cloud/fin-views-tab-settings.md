# fin-views-tab-settings

- Audited the final views-tab-settings row and its cited WPF SettingsTabView.xaml,
  SettingsTabView.xaml.cs, Features/FeatureCard.xaml.cs, and SplitFeatureCard.xaml.cs
  at base 9b3dcafcb9894cac629d52a990ef81157e46b741.
- This branch preserves the gap analysis and verification limits after the execution
  environment became unavailable. It contains no application changes and no trunk imports.
- The row must remain stub. Several remaining gaps are actionable presentation work;
  this is not a dependency NOOP and no wired/verified claim is made.

## Remaining WPF behavior

| Gap | Pinned WPF reference and required behavior |
| --- | --- |
| Deeper page door | SettingsTabView.xaml:601-636; MainWindow.Presets.cs:1110-1121. Replace the retired teaser with the localized Deeper tile, deeper_editor.png and ordered two-line tooltip. Navigate to ShowTab("deeper"), never launch an editor. No toggle, dimming or price tag. |
| Mystery gift presentation | MainWindow.Presets.cs:1164-1272. Keep the offer on the box; reveal today's localized feature name, mod art/fallback and tier/free stamp only on the reverse. Premium accounts carry no price sign. Both faces navigate to the daily pick. |
| Mystery hover and notice | MainWindow.DashboardFx.cs:277-727. Reversible 150ms half-turns, 6-degree skew, phantom-leave handling, persisted DailyGiftLastRevealDate, NEW TODAY notice and 900ms half-cycle breath; hide/detach/motion/focus gates. Reuse the existing daily-free and entitlement seams and shared frame clock. |
| Weekly intake pass face | MainWindow.UiUpdates.cs RefreshIntakePassTile, face/help/CTA/spin helpers. The existing click relay does not expose the hidden face; presentation must follow the existing intake-pass state. |
| Card resting appearance | FeatureCard.xaml.cs and SplitFeatureCard.xaml.cs: inactive dim/grayscale, muted title, per-half state, lock/hover precedence and dashboard depth. Existing click tests do not prove visual parity. |
| Owned-style v2 badges | SettingsTabView RefreshV2Badges and MainWindow.Presets.cs:1144-1147 use WPF-only Services.Prizes.V2Badges/PrizeGrants. Port ownership truth before displaying these badges; do not infer ownership from settings. |
| Mod/unlock/live presentation | MainWindow.UiUpdates.cs ApplyModFeatureNames, RefreshModAwareSurfaces and UpdateUnlockablesVisibility, including dashboard art/labels/locks and their mod/account/day refresh triggers. Some Core input seams already exist. |
| Dashboard decoration | MainWindow.DashboardFx.cs: wordmark sheen/custom-logo drift and browser frame sweep/status pulse. Existing fog, favorites, fold, card and vault clocks do not cover these behaviors. |
| Browser pop-out/enhancement | MainWindow.Browser.cs: pop-out/reparent/return and BrowserEnhanceBridge. Avalonia's SettingsTabView.BrowserCard.cs disables these controls because the native-host reparent seam and bridge are absent. |
| Once-ever note voice | MainWindow.UiUpdates.cs:1959-1968 calls PlayNoteClip and latches NewYearNoteReactionSeen only after success. note_newyear.wav ships on neither head; preserve the unlatched silent fallback until the clip exists. |

The old tease is dead in the pinned live WPF dashboard: SettingsTabView.xaml's
handler search has CardDeeperEditor_Click at line 628 and no CardJustDrop/Tease caller;
FeatureCard.xaml.cs documents TeaseTier as currently unworn. P122 can explain that
retired path, but cannot cover the live missing behavior or justify marking this row wired.
No new surface is introduced by this analysis.

## Verification limits

- Original checkout verification: clean work branch at
  41d34136d3e3d4225ad48da419d3943184da8fbe, different from the required base.
- Fetch of the requested base exited 255: read-only FETCH_HEAD.
  The authorized fallback clone completed, then its work checkout was verified clean
  at exactly 9b3dcafcb9894cac629d52a990ef81157e46b741 before the output branch was created.
- Installed .NET SDK 10.0.402 from the permitted Microsoft builds feed after channel
  discovery encountered a blocked ci.dot.net endpoint.
- dotnet build CCP.Core/CCP.Core.csproj -c Release: exit 0,
  48 warnings, 0 errors, elapsed 00:00:28.76. Core was unchanged.
- dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release: started, final result unavailable.
  The execution service disconnected and subsequently reported no usable environment.
- Not completed: remaining Linux test-project builds, optional Windows-targeted WPF test
  build, core-guards.sh, Core tests, relevant Avalonia classes, negative-control tests,
  and render-all. No Windows-only behavior was compiled or run.
- The supplied environment limit also requires compile-only Avalonia validation until
  the font lane lands. No tests were weakened, skipped or deleted.
- Local application edits were not committed or published and their survival cannot be
  verified while the environment is offline. Do not treat them as a finished implementation.
- A GitHub tree publication attempt containing the existing ledger was rejected because
  that ledger includes prohibited local filesystem paths. This safer artifact leaves
  the ledger untouched and publishes only this neutral analysis document.
- Runtime startup was retried after the environment was supplied again, but no recovered
  execution session was available when this artifact was prepared.

## Assumptions

- The frozen WPF reference wins over stale port comments about the retired teaser.
- The Deeper tile opens the existing page, never a new enhancement editor.
- Keep views-tab-settings stub until its live paths and focused tests are complete.

BLOCKED: Execution environment unavailable; implementation and required verification could not be completed.
