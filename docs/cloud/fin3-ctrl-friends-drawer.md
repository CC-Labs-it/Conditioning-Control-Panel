- Add the WPF once-only presence question, short signed-out header status, header/friend tier art, lock-day chip, and notices bell wired to the existing landing setting.
- Add request Block/Report menus and the account-scoped local block fallback; successful block/unblock updates it, and a server block list retires it without reblocking anyone.
- Adapted fallback from read-only parity/trunk commit `0a5efa8b7d4487daaaa57a7654a1d241f23f8719`; tier art recipe from `f8a56b299776ddbf32b0e94cab08aeb2b8f2625d`. Existing head files were merged, never replaced.
- Add focused `FriendsDrawerFinishTests`, numbered WPF tester steps, and consolidate the ledger to one honest stub row. No new window/popup surface was introduced.

## Verification limits

- SDK: .NET 10.0.402. All commands used Release; tests used `DiffEngine_Disabled=true` and the repository's `CCP_USERDATA_DIR` test sandbox.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3,953 passed, 5 existing Vosk/environment skips, 0 failed. No tests were weakened or removed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~FriendsDrawerFinishTests`: exit 0; 5 passed, 0 skipped/failed.
- Combined filter `FullyQualifiedName~FriendsDrawerTests|FullyQualifiedName~FriendsDrawerFinishTests`: exit 1; 25 passed, 5 failed. Four existing window tests fail with “Could not create glyphTypeface” for Fredoka/Segoe UI; `AnAcceptAnsweredAfterTheDrawerFoldedIsSaidOverTheWindow` fails its empty floating-word window assertion. These remain unresolved here; the full existing drawer suite needs the desktop/font-capable supervisor lane. The new tests do not open a desktop window.
- Mutation proof: temporarily remove each production behavior, compile and run its focused test, then restore. All eight runs exited 1 with assertion/runtime test failures, not compiler errors: presence (both Yes/No cases failed), tier, lock chip, notices toggle, request menu, remember-block, unblock cleanup, local-list retirement (one failure each). Initial new suite: 5 passed; final restored new suite: 5 passed.
- Initial overlapping builds hit an Avalonia file lock; subsequent builds ran sequentially. The initial test compile also caught a missing namespace import, fixed before test execution.
- `test-plan-coverage.sh` is absent on this base; numbered steps were added to the test plan. `git diff --check`: exit 0.
- No live remote account checks, Windows execution, full unrelated Avalonia suite, or Vosk model download. WPF was cross-compiled only.

Build commands/results (each `dotnet build <project> -c Release`, exit 0, zero errors; existing warnings remain):

| Project | Result |
|---|---|


| `CCP.Core/CCP.Core.csproj` | exit 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | exit 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | exit 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | exit 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | exit 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | exit 0 |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | exit 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | exit 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | exit 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | exit 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | exit 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | exit 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | exit 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | exit 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | exit 0 with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` |

## Remaining WPF gaps

- Leash section above the popup list and the friend-card Offer chip.
- Juice: entrance spring/stagger, card-in, sheen, dot breath, online hop, picker pop/sparks, Add shockwave; FriendsSfx cues.
- Goon/chess hosting invite paths still return the existing “not on this build” explanation.
- Friend links (`FriendsDrawer.Link.cs`): Copy link beside own code, New link in Add, pasted link/token claim, busy guard and timed link notes are absent.
- Remote avatar/platform marks, incoming request More button/responsive age layout, and incremental row preservation differ from WPF. The head also retains its existing simplified palette/font/hover recipes and confirmation-picker reset behavior.
- No live remote friends account or Windows desktop check; Windows code can only be cross-compiled here.

## Assumptions

- Keep the row stub while these paths remain incomplete, as explicitly required by the unit rules; avoid importing the much larger leash/motion dependency tree under this branch's 1,000-added-line cap.
- Retain the head's Fluent icon policy for lock and bell instead of WPF emoji/MDL2.
- A server-provided block list, including an empty list, is authoritative; retire local entries without sending block operations (WPF fallback semantics).
