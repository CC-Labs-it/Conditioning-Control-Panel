# fin5-feat-friends-feed

- Finish `feat-friends-feed`: fresh feed dots have the WPF pink glow (6 px blur, 0.9 opacity), drawn as a static sibling `Border.BoxShadow` behind the crisp 6 px dot. No Effect, new clock, surface, or panic registration is needed; existing `friends-motion` registration remains in force.
- Preserve the existing feed storage, folding/read rules, More/Less, count, click-to-card reveal/scroll, new-top tween, sender trails, and five-second result hold. Promote the single ledger row to `wired`, never `verified`, and extend numbered manual step 1.
- Add a headless regression for glow size, color, opacity, zero offset, stacking, input transparency, absence of Effect, persistence through repaint after reading, and removal on fold/reopen. Use an empty authenticated friends snapshot so the focused test does not measure request-row fonts.
- WPF comparison: base `66550597cc762797d361073392552637cb0d951b`, `FriendsDrawer.Feed.cs` (`FeedRow`) and `FriendsLook.cs` (`Glow`). Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; its feed uses the same glow. No trunk files imported or overwritten; the existing adapted head wins.

## Gap audit

Read all cited WPF drawer feed, trail and honesty files and the shared Core feed, seen, receipt, event and sent-book implementations. The only remaining user-visible gap in this row on the required base was the new-dot glow. Feed persistence/account isolation and receipt state/copy are already shared Core behavior; startup creates the app feed. The existing Social > Friends page reaches the feed, line opener, per-friend trails and worded results. The base's completion tests cover feed folding/read timing, live updates, card reveal/scroll and result hold. No host dependency needs porting for this row.

## Assumptions

- WPF's static DropShadowEffect is represented by a sibling rounded Border shadow per P82, retaining its authored color, blur and opacity. Cross-renderer pixel identity is not claimed.
- A static decorative child of the existing drawer is not a new panic surface. It has no work to stop; existing drawer tweens remain on the panic-stopped shared clock.

## Verification limits

- SDK: .NET 10.0.402. All commands below used Release; tests used `DiffEngine_Disabled=true` and the repository's sandbox profile setup.
- `dotnet build <project> -c Release`: exit 0, zero errors for CCP.Core, CCP.Avalonia, and every Linux test project listed in build.yml: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests. Existing compiler/analyzer warnings remain.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. Windows/WPF code compiled but was not run on Linux.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~FreshFeedGlowMatchesWpfAndClearsOnFold`: before implementation, exit 1 (rendered dot exists, expected one glow, actual zero). After implementation with `--no-build`: exit 0, Passed 1 / Failed 0 / Skipped 0.
- Initial attempt to run the existing `FeedReadsOnlyVisibleLinesAndKeepsDotsUntilFolded` regression: exit 1 on the documented environment issue, `Could not create glyphTypeface`, `$Default`, in request-row measurement. Broader drawer tests were not run after that infrastructure failure; they require the supervisor's GitHub test environment. No tests weakened or skipped in source.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed (424 XAML references checked). The first invocation lacked temporary-file write access; rerunning with the build execution permissions passed.
- `git diff --check`: exit 0. The base does not contain `.github/scripts/test-plan-coverage.sh`; numbered manual coverage was updated directly.
- No live server, real desktop, Windows runtime, Vosk model download, full Avalonia suite, or manual visual comparison was exercised. The change adds no OS-specific code. Cross-renderer glow appearance remains a manual check, so the row is wired, not verified.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; Passed 2820, Failed 0, Skipped 5, Total 2825. The five existing Vosk tests skipped because the speech model is unavailable in this environment; no test definitions were changed.
