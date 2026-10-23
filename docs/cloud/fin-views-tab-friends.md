# fin-views-tab-friends

- Port WPF drawer motion into the existing Friends page and popup: bottom-left entrance, 40 ms row stagger capped at slot six, card/Add reveal, hover sheen, Full-only online-dot breath, online hop, picker/copy sparks, and successful Add/Accept shockwaves. Reduced keeps WPF amplitudes/timings; Off stays still. Reuse the shared FrameClock; no Transform animation, infinite Animation, or animated Effect. Close, detach, motion changes and panic restore resting values; register `friends-motion`.
- Restore incoming-request More/right-click menus (Block first, then Report reasons), inline error/report words, and compact translated request buttons so the age remains readable.
- Restore coloured initials, cached HTTPS avatar photos (proxy-relative URLs included, failure retains initials), the two-second copy-label reset, and a fresh five-second hold for repeated identical results.
- Add focused headless tests for these paths in `FriendsDrawerJuiceTests.cs`, reusing the existing fake Core wire and profile sandbox. Keep the ledger row `stub` with its remaining dependencies.
- Reference: WPF sources at `4f75aed68cb06c56af5dc60472ca1ce0d2463000`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Existing head files were merged, never replaced.

## WPF gap audit

Read the ledger row and its three cited WPF page/social-registration files, then the hosted drawer, Juice, Pickers, Honesty, Blocked, and FriendsLook implementations. The page wrapper already had its WPF lifecycle/navigation; all self-contained gaps found were in the shared drawer.

At the base, missing behaviour was: entrance/stagger/card/Add motion; row/request sheen; dot breathing; online hop; all picker/copy Pop bursts; Add/Accept shockwaves; avatar photos and name-derived fallback colours; incoming-request Block/Report menus and translated-label compaction; copied-label reset; restarting a repeated result's read time. These are implemented here. Existing text, section order, code normalization, presence ask, tier/lock plates, bell and sound routes are reused.

Still missing, with live WPF callers:

- Feed, receipt/honesty lines and per-friend sent/arrived/seen trails: `AddFeed`, `TrailLine`, service receipt/feed events; dependency `feat-friends-feed`.
- Table polling, hosting-first ordering/status, row Join and the Social open-table badge: `StartTables`, `HostingFirst`, `TableFor`, `TableJoinButton`, `HookLobbyBadge`; dependency `feat-friends-open-tables` and its host.
- Per-PC blocks when an older server omits `blocked`: `BlockedRows` / `OnBlocked`; dependency `feat-friends-blocked-presence`. Server-backed Block/Unblock and the presence switch remain available.
- Leash offer in an expanded friend card: WPF `BuildCard` calls `LeashDrawerSection.OfferChipFor` even in page mode. This needs the unported leash drawer/offer UI. The pinned leash section itself is intentionally omitted by WPF page mode.
- Goon room / chess challenge invite providers: `FriendsInviteCodes` remains unseeded; needs the Goon host and `feat-pbp-media-friends`. Their tiles retain the existing unavailable explanation.

Catalogue watches are not an additional live WPF capability at this SHA: `git grep -n 'CatalogueWatches' 4f75aed68cb06c56af5dc60472ca1ce0d2463000 -- ConditioningControlPanel` finds two `.List()` consumers and the declaration, with no assignment to the default empty provider. Both heads retain the empty-state text. No P122 promotion is claimed; other live gaps remain.

## Verification limits

- .NET SDK 10.0.402 installed using dotnet-install. Required base matched exactly after the prescribed fetch/checkout; initial checkout was clean but at a different commit.
- Release builds: CCP.Core, CCP.Avalonia and all existing Linux test csproj files referenced by `.github/workflows/build.yml`: passed, zero errors. WPF test project with `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false`: passed, zero errors. Existing warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0; 2,817 passed, 0 failed, 5 existing Vosk tests skipped (model unavailable; download is blocked). No tests were weakened or disabled.
- Avalonia tests compiled only, per the supplied cloud font-creation limit; the font lane is absent at the required base. Pending supervisor command: `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~FriendsDrawerTests|FullyQualifiedName~SocialPagesTests|FullyQualifiedName~PanicSurfacesTests"`. Runtime results, red-before-fix proofs and render-all are not claimed.
- Windows code was compiled, not run. No desktop, account, production friends server or external photo endpoint was exercised; photo tests use an in-process fake.

## Assumptions

- Keep the row `stub` while the named dependencies remain; page-mode omission of the leash section does not excuse its live friend-card offer.
- Use the existing shared 30 Hz frame source for ambient and short drawer motion; sibling BoxShadow decoration replaces WPF particle Effects. Use the existing head avatar gradient and art cache.
- Use a 16 px scrollbar allowance for the compact request-row measurement. The WPF catalogue provider is unseeded, so do not invent catalogue content.
