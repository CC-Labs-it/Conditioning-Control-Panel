# wire-views-tab-studio: Studio rack, stub -> wired

Ledger row `views-tab-studio` moves from **stub** to **wired**. WPF reference:
`ConditioningControlPanel/Views/Tabs/StudioTabView.xaml.cs` and `StudioTabView.Haptics.cs`.

- **Haptics row right-click.** It now flips the hosted Haptics page's own `ChkHapticsEnabled`, as
  WPF `StudioTabView.xaml.cs:532` does. The page's handler is real on this head now
  (`HapticsTabView.Connection.cs`), so the premium/free-day gate and its revert run with the
  write. Before this, the gesture was deliberately inert (`toggle: null`). Adapted from trunk
  **7a6735e8c**; only the Studio hunk was taken.
- **The Haptics dot follows the box live.** New `CCP.Avalonia/Views/Tabs/StudioTabView.Haptics.cs`
  ports WPF `HookHapticsModule`. The enable sits on the nested `Haptics` settings object, which
  raises no INPC, so the shared listener never saw it change. The hook listens to the page box's
  `IsCheckedChanged` and repaints one beat late at Normal priority, after any gate revert. It is
  hooked once from `OnLoaded`, as on WPF.
- **v2 pills on the Flash and Bubble Pop rows.** These follow WPF `:717`, `:864`, `:965` and
  `:1505`. Both the resting strip and the active tile get the pill. It has its own grid column,
  so it never draws under the state dot (the WPF Tock fix), and the tile caption inset is measured
  (`ApplyTileCaptionInset`). Ownership comes from `PrizeOwnership.FlashV2Owned` and
  `BubbleV2Owned`, which mirror WPF `V2Badges` and gain the four missing wire ids.
  - ponytail: the pills are read on Loaded only. WPF re-reads them on
    `PrizeGrants.GrantsChanged`, but this head has no ownership service yet (row
    svc-prize-ownership), so ownership cannot change at runtime.
- **Pill builder reused.** `FlashFeatureControl.NewV2Badge(margin, compact)` replaces that
  picker's inline pill (WPF `FeatureCard.NewV2Badge`). The motion picker and the rack now share
  one pill.
- **Stale notes fixed.** The ponytail notes that called the Haptics half "blocked on a stub" and
  the class header's "stubbed" paragraph are now accurate.
- **No new panic surface.** Nothing added here opens a window, overlay or timer.

Device connect and drive belong to row `shell-haptics-device`, not to this row. On WPF the box
also only writes the flag.

## Verification limits

- **New tests:** `StudioRackWireTests` (Tests/CCP.Avalonia.Tests, headless, real pointer on the
  shell). 4/4 pass.
  - `HapticsRowRightClickGoesThroughThePageBoxAndTheDotFollows` covers three cases: premium on,
    the page box turned off directly, and the gate refusing when not premium.
  - `V2PillsFollowOwnershipAndSitBesideTheDot` has 3 cases: bubbles owned, flash owned via Jackpot
    Remix, and nothing owned.
- **Fail-proven.**
  - Setting `toggle: null` and skipping `RefreshV2Pills` makes 3/4 fail.
  - Skipping `HookHapticsModule` makes the haptics test fail.
- **Regression filter (Tests/CCP.Avalonia.Tests):** the
  Studio|Flash|Haptics|PlayWall|PremiumGates|GateFx|Panic|FeatureIntroWiring|HelpButtons|LockdownVeil|SessionIo|AmbientBeatLoops
  filter gives 146 passed, 0 failed, 5 skipped. The skips were already there.
- **Tests/CCP.Core.Tests:** 2777 passed, 2 failed, 6 skipped. The branch has no Core diff, and
  both failures come from this container:
  - `LoopbackOAuthTests ... [::1]`: the container has no IPv6 loopback.
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`: the container runs as
    root, so the test cannot make the folder undeletable.
- `bash .github/scripts/core-guards.sh` passes.
- **Builds:** `dotnet build` of CCP.Avalonia and Tests/CCP.Avalonia.Tests succeeds with 0
  errors. The WPF head was not built (it is Windows-only), and no WPF file changed.
- **Render:** `dotnet run --project CCP.Avalonia -- --render-all` was run in a Debug build with
  `CCP_PRIZE_GRANTS='fx.*'` and a sandboxed `CCP_USERDATA_DIR`. All 221 views rendered (exit 0).
  `StudioTabView.png` shows the v2 pill on the checked Flash Images tile and beside the dot on the
  Bubble Pop strip. A Release build never reads that override, so with no grants the rack
  looks exactly as before.
- **Not run:** nothing Windows-specific was touched.
