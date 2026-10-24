# fin2-views-tab-app-settings

- Finish `views-tab-app-settings`: invites now appear only in Settings > Account & Plans, with a separate placement host so signed-out/offline visibility remains owned by InvitePanel.
- Point shell refresh and startup ticket read subscription at the Plans card. Plans repaint refreshes it using the existing 30-second throttle.
- Ticket and friends-drawer invite navigation use Settings > Account, then retain WPF's 300 ms settle, forced refresh and BringIntoView. Preserve Lockdown refusal.
- Extend AccountPlansVaultTests and InvitePanelTests for placement, destination, card-to-ticket updates, throttling and forced refresh. Existing AppSettingsRefreshTests cover per-open refresh isolation.
- Mark the single ledger row wired, not verified. No new window, notification or panic surface; reuse the existing Settings shell and Plans view.
- Reference: WPF at base `f52156c2628ee8924f1b26a363548d15cd365d5c`, AppSettingsTabView XAML/host and eight compatibility partials; ExclusivesTabView.PlansMode, MainWindow.Exclusives.EnsureExclusivesBuilt/RefreshVaultCore, MainWindow.InviteTicket.OpenInvitesCard.
- Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Existing head files retained and adapted in place.

## Gap inventory and assumptions

The host already implements WPF section order, focus/scroll spy, isolated per-open refresh and Plans header/link/pill motion. The compatibility partials expose section-owned controls rather than adding host behavior; those sections retain their existing implementations and parity rows. Remaining host gaps were invites placement, shell refresh/read subscription routing and ticket/friends-link destination. All are handled here. Keep the existing Avalonia Lockdown guard and established Fluent icon divergence. A permanently instantiated hidden Premium InvitePanel remains unused; this keeps the shared view/XAML diff small.

## Verification limits

- Required base verified after fetching: exact expected commit; clean checkout before branch creation.
- .NET 10.0.100 installed (channel discovery returned 403; pinned SDK download succeeded).
- Pre-fix focused run: 3 failed, 6 passed, 0 skipped. Placement failed at its visibility assertion; ticket destination was Premium instead of Settings; Plans card hookup was absent.
- Post-fix focused run (`AccountPlansVaultTests|InvitePanelTests|AppSettingsRefreshTests`, Release, DiffEngine_Disabled=true): 12 passed, 0 failed, 0 skipped (exit 0). Tests use the existing isolated profile and headless dispatcher; no real desktop or user profile.
- An overlapping initial build/test attempt hit an Avalonia PDB lock; rerunning against the completed build passed.
- Full Avalonia suite and real server integration not run. Windows UI execution unavailable on Linux.
- Release builds: CCP.Core, CCP.Avalonia and all 11 Linux test projects listed in build.yml succeeded with zero errors. The WPF test project also built with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false (zero errors); compiled Windows code was not executed.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed. A separate read-only-sandbox attempt could not create temporary files; the authorized writable run passed.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 2811 passed, 0 failed, 5 skipped (Vosk model unavailable). No tests weakened or disabled.
- Plans repaint negative proof: temporarily removing its RefreshAsync call made PlansRepaintRefreshesItsInviteCard fail (exit 1, missing mine read); restored source and final focused run passed.
