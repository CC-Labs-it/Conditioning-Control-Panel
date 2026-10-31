# A5 lobby-race handoff

Base verified: CC-Labs-it/Conditioning-Control-Panel, work,
74ac6ce3d5e0ae082c2871c00e864943e15aa2ad, clean after the requested reset.
One commit, target avalonia-port/cloud-r72-a5-lobby-race.

References read from origin/release/7.2.0:
- ConditioningControlPanel/MainWindow/MainWindow.Lobby.cs:273, race dispatch; :283, room validation.
- ConditioningControlPanel/Services/Lobby/LobbyService.cs:128, race listing.
- ConditioningControlPanel/Services/Glassroad/GlassroadHostService.cs:56, Prime; :80, first join and live join-room; :238, friends list; :371, invite send.
- ConditioningControlPanel/Services/Friends/FriendsApi.cs:483, known course only with a valid room.
- ConditioningControlPanel/Services/Friends/FriendsLanding.cs:212, course on invite; :532, Prime refusal; :570, join.
- ConditioningControlPanel/Services/Friends/FriendsLandingRules.cs:228, course wording.
- Course-related WPF commits: 0f68f3437 and 9a5fb53a5. Hosting course selection stays in the existing web tree.

Decisions read from origin/docs/72-answers-1031:
- handoff-1029/B1-SPARKLES-DECISIONS.md, B2-RT-LINUX-DECISIONS.md, B3-SHOP-DECISIONS.md.
- handoff-1031/MICH-72-ANSWERS-1031.md:56 limits Sparkles UI to slip and wallet and excludes the Linux shop.
- MICH-72-ANSWERS-1031.md:95 excludes generated/verbatim copies from the 600 hand-written line cap.
This slice adds no shop, engine, web changes, payout logic, or localization keys.

Changed files:
- CCP.Core/Services/Friends/{FriendsModel,FriendsApi,FriendsLandingRules}.cs: course adoption and wording.
- CCP.Avalonia/Platform/FriendsLanding.cs: course-aware knock and gated race join.
- CCP.Avalonia/Views/Games/{GameWindow,GameWindow.Glassroad,GameWindow.LobbyRace,GlassroadHostRules}.cs: room intent, invite bridge and capability.
- CCP.Avalonia/Views/Tabs/AvailableSubjectsTabView.{axaml,axaml.cs,Race.cs}: visible-page poll, Core rows and joins.
- Tests/CCP.Core.Tests/LobbyRaceInviteTests.cs; Tests/CCP.Avalonia.Tests/{LobbyPageTests,GameWindowTests.LobbyRace}.cs.
- docs/avalonia-parity.html; docs/avalonia-main-sync-20261030.html; this handoff.
Generated/copied files: none. All changed lines count as hand-written.

Validation commands (SDK /tmp/dotnet/dotnet, Release, DOTNET_ROLL_FORWARD=Major):
- dotnet-install --channel 10.0: installed 10.0.403 with the stable builds.dotnet.microsoft.com feed after ci.dot.net returned 403.
- dotnet build CCP.Core; dotnet build CCP.Avalonia; dotnet build Tests/CCP.Avalonia.Tests: 0 errors. Single-worker rebuild used after concurrent MSBuild worker failure.
- dotnet test Tests/CCP.Core.Tests: Failed 0, Passed 4643, Skipped 5, Total 4648.
- Final Core course tests: Failed 0, Passed 8, Total 8.
- Course fail-proof: disable course adoption, Failed 5, Passed 2; restored and green.
- Page join fail-proof: remove join dispatch, Failed 1; restored.
- Focused page test: Passed 1. Covers listing, course, join, malformed id, sign-out, polling stop and late-answer suppression.
- Final focused Avalonia run after restore: Failed 0, Passed 2, Total 2, Duration 8 s.
- Broad affected Avalonia run: 29 passed, 10 failed, including glyphTypeface creation for Segoe UI Black and existing floating-word assertions. Window behavior is compiled, not verified by that broad run; the two focused regressions subsequently passed together.
- node --test Assets/web/glassroad/tests/friend-invite.test.mjs: pass 6, fail 0.
- bash .github/scripts/core-guards.sh: All Core guards passed.
- python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.
- git diff --check: clean.

Part B: remaining validation and scope

No live multiplayer seat or desktop panic desk run was performed. The new host regression exercises Prime refusal,
room validation, first-load query, live join-room, friend bridge replies, course wording and the existing games panic registry.
The broader font-dependent coverage must be run on the tester machine. No claim of live verification is made.
The launcher Lobby dropdown and other lobby game host/join lanes remain open; main-sync row 499 is explicitly partial.
Rows 586 and 588 close this page/invite wiring slice. The Linux Electron engine and its measured performance gate remain separate work.
