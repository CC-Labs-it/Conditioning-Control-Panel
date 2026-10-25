# fin4-views-tab-friends

- Base verified at `672a29fe169961328aef68affad9646d0de0ebb9`; original snapshot was clean but at a different commit and read-only. Used the requested independent fallback checkout and created only the authorized unit branch.
- Adapted WPF `FriendsDrawer.Feed.cs`, `.Trail.cs`, `.Blocked.cs` and `FriendsBlockList.cs` from that pinned base. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk files imported or existing head files overwritten. WPF remains unchanged.
- Startup attaches the existing Core feed to the real Friends service. The shared page/popup drawer shows newest-first feed lines, age labels, unread dots held until fold, More/Less, friend-card opening/scrolling, and motion through the existing panic-stopped frame clock. Visible lines alone become read. Feed updates preserve the friend list under the pointer. Long names trim with a full-line tooltip; watch titles retain literal placeholders.
- Receipt trails follow the existing service while open, with WPF copy/tone/pips; five-second action results take precedence. Closing unsubscribes both trails and feed.
- Added the WPF per-account capped local block store in Core under distinct type names, preserving the frozen WPF implementation. Only successful blocks are remembered; only successful unblocks forget. The server list wins, including an empty list; the landing tick drops local entries once a server list exists, without sending new blocks.
- Added focused Core/headless tests and numbered tester steps. No new window surface; feed motion uses the drawer's existing panic registration.

## WPF gap audit and remaining scope

Read the row's three cited WPF files and the shared drawer partials they invoke. Before this unit, missing page behavior comprised feed presentation/lifecycle, receipt/send trails, local block fallback/migration, open-table polling and hosting rows, Social lobby badge leases, friend-card leash offers, and host-backed Goon/chess invite flows. The first three are implemented here. Existing page entry/exit, signed-out/settings/invites routing, presence ask/toggle, plates/bell, add/request/menu flows, avatars and drawer motion remain in place.

Remaining live dependencies keep `views-tab-friends` **stub**:

- `feat-friends-open-tables`: WPF `FriendsDrawer.Tables.cs` starts a refresh plus a 30-second timer only while open, sorts hosting friends first, paints hosting/Join rows, gates at click time, launches Goon and closes the popup. Core already has `GoonOpenTables`, but this head has no Goon host that can fulfill Join. `MainWindow.SocialTabs.cs:23` calls `HookLobbyBadge`; WPF `MainWindow.Lobby.cs` leases the shared Lobby service while visible and not minimized and publishes Social open counts. Avalonia still lacks that host wiring.
- `feat-pbp-media-friends` / Goon host: WPF picker `InviteCodes` uses `GoonHostService.RoomCode`, `CanHost`, `OpenRoomForInviteAsync` (45-second room-opening/busy behavior), and `PieceByPieceHostService.ChallengeFriendAsync` (45-second challenge). Avalonia's `FriendsInviteCodes` still has unseeded host providers. A chess window exists, but the friend challenge/lobby intent path does not; the Goon host is absent.
- Leash drawer offer workflow: WPF `FriendsDrawer.cs:678` calls `OfferChipFor` even in page mode. It needs offer eligibility/holding/sent/receipt states, capacity/recency gates, the explainer, service mutation and live redraws. The page-mode exclusion applies only to the pinned leash section. A fake clickable chip would not fulfill the workflow.
- These are live WPF callers, so P122 does not apply. The Goon host alone is 2,167 WPF lines before adapting its host dependencies and tests; its drawer tables add 141, leash drawer 285 plus explainer/cards, and chess Friends partial 108. Completing this dependency closure cannot fit the 1,000-added-line unit cap. No placeholder Join/Offer action was added.
- Catalogue watch provider is empty in WPF too: at the pinned base `FriendsDrawer.Pickers.cs:466` defines `CatalogueWatches.List` as an empty delegate, and `git grep -n 'CatalogueWatches.List[[:space:]]*=' 672a29fe169961328aef68affad9646d0de0ebb9 -- ConditioningControlPanel` finds no assignment. This is not a new parity gap.
- Feed unread rail/tray badges belong to `feat-friends-feed` beyond this page's cited surfaces; they are still absent and that separate row remains missing.

## Assumptions

- Prioritize reachable page behavior using existing Core services; keep the ledger stub until host-backed workflows exist.
- The frozen WPF source at the required base takes precedence over newer trunk. Distinct Core local-block type names avoid changing WPF or exporting conflicting names.
- Preserve WPF block-file format and timing. Use the existing frame clock for feed entrance and an unclipped sibling-safe dot without WPF's glow Effect, following the Avalonia rendering constraints.

## Verification limits

Results are recorded below after the final checks. Windows UI cannot run on this Linux worker. The headless suites use their existing sandbox profile; no real desktop/profile was used. The cloud image has the known glyphTypeface/font limitation. Vosk model download was not attempted (documented blocked endpoint); optional speech tests may skip without the model.

- `dotnet --version`: .NET 10.0.402. Channel discovery initially returned 403; both the requested installer and pinned fallback ultimately succeeded.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia, and all eleven Linux test projects named in `.github/workflows/build.yml`: exit 0, `0 Error(s)` for each. Existing warnings remain. Initial easing API mismatch was corrected; native output copies initially exhausted disk, then all builds passed sequentially while discarding generated isolated-suite outputs.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, `512 Warning(s), 0 Error(s)`. Windows app/tests compiled, not executed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, `Failed: 0, Passed: 2820, Skipped: 5, Total: 2825`. Initial concurrent-build run had one existing board-frame timing failure (12.51 ms); the final full run passed after builds finished. Vosk-dependent tests skipped by the existing suite without its model; no tests changed to skip or weaken assertions.
- Focused `FriendsLocalBlocksTests`: exit 0, `Failed: 0, Passed: 3, Skipped: 0`.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~FriendsDrawerTests|FullyQualifiedName~SocialPagesTests|FullyQualifiedName~PanicSurfacesTests'`: exit 1, `Failed: 32, Passed: 13, Skipped: 0, Total: 45`. Every failure is `Could not create glyphTypeface` for default/Fredoka/Segoe UI fonts. The three new UI tests compile but cannot reach their behavior assertions here; GitHub rcheck is needed. No pre-fix behavior-failure claim is made from environment failures.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.`
- `git diff --check`: exit 0. `test-plan-coverage.sh` is absent at this base; numbered manual coverage was added anyway.
- No desktop UI, render-all, Windows runtime tests, or additional isolated-suite executions were run; their required compilation succeeded. Remaining host workflows are detailed above, and the ledger remains stub.
