# wire-views-tab-quests: Quests tab art, season title, Fix day

Ledger row `views-tab-quests`: stub -> wired. Base `avalonia-port/quiz-core-a`.

- **Quest art** on the three daily cards and the weekly card (`CCP.Avalonia/Views/Tabs/QuestsTabView.Art.cs`),
  ported from WPF `GetQuestArt` / `GetModeAwareQuestImagePath` / `LoadQuestImage`: a cached remote image first, a mod
  override of the bundled picture next, then the Sissy / CCP Default wordmark swaps; decoded once per path and
  cleared on a mod switch. Imported from trunk **348cdf487** (Art.cs + `QuestArtTests`), adapted: the mod override is
  checked before the swaps, as WPF does. The commit's "Jump right in" half is not part of this unit and was left out.
- **Season title**: `App.QuestDefinitions` (the `QuestDefinitionService` StartQuests already built) is exposed, and
  `RefreshQuestUI` writes its `SeasonTitle` (server title, else the local month name) into `TxtSeasonTitle`, WPF
  MainWindow.QuestsTab.cs:205.
- **Fix day** (`CCP.Avalonia/Views/Tabs/QuestsTabView.StreakFix.cs`), ported from WPF RefreshStreakCalendar's
  button tail, `BtnFixStreak_Click`, `ExitStreakFixMode`, `StreakFixDay_Click` and `OnSettingsPropertyChangedForQuests`:
  shown for everyone, armed only signed in with a charge and a missed day; fix mode puts a ring on every missed day
  that pulses 1.0 to 0.3 over 600 ms on the tab's existing ambient beat (clamped; at rest with ambient loops off, no
  infinite Animation); a ring asks Yes/No, spends on the server, and only a server yes takes the charge and stamps the
  day; a balance change from anywhere repaints the parked tab. Button caption and status line are `IconLabel`s, so
  the loc strings' emoji draw as Fluent icons. Imported from trunk **87824d840**, adapted.
- **Core `StreakFix`** (`CCP.Core/Services/Progression/StreakFix.cs`): `HasMissedDays`, `WireDate` (invariant
  calendar), `ApplySpent` (server balance, else one off; day stamped once; streak recounted; XP untouched) and the
  Avalonia head's `UseAsync` (POST `/v2/user/use-oopsie`). WPF MainWindow.QuestsTab.cs now calls the Core rules
  instead of its inline copies (one copy); WPF keeps its own send in ProfileSyncService because it carries the 401
  recovery this head does not have (ponytail note in the class).
- **Punch card**: unchanged and collapsed, matching WPF, which hides it while `IntakePunchCardService.UiEnabled` is false.
- Not in this unit (over the line cap): the WPF depth pass in QuestsTabView.xaml.cs (planks, float bands, done-sink,
  section-hue shadows, tube bead; trunk e9ac15a25 + d33b90fcc), named in the row as lane `wire-views-tab-quests-b`.
- Panic: nothing added that WPF's panic closes (the confirm dialog is modal and transient), so PanicSurfaces is unchanged.

## Verification limits

- `dotnet build` CCP.Core, CCP.Avalonia, Tests/CCP.Avalonia.Tests and every Linux test project in build.yml: 0 errors.
  `Tests/ConditioningControlPanel.Tests` (with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`)
  also builds, so the WPF edits compile; nothing WPF was run.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 2781 passed, 6 skipped, 2 failed. The two failures
  (`LoopbackOAuthTests.GoodState_OnEitherLoopback...(host: "[::1]")`, no IPv6 loopback in this container;
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`, the container runs as root) fail identically on the
  untouched base. New `StreakFixTests` (4) pass.
- `dotnet test Tests/CCP.Avalonia.Tests --filter QuestsStreakFixTests|QuestArtTests|QuestsTab|AmbientBeatLoopsTests`: 14 passed.
- Fail-proven: `QuestsStreakFixTests.FixDayArmsEntersFixModeAndSpendsOnlyOnAServerYes` goes red with the season-title
  write removed (line 69), with `Art = null` (line 71), and with the charge-balance watch removed (line 149). On the base
  `FixStreak()` was an empty method and the button never showed, so the fix-mode assertions cannot pass there.
- Not run: the real use-oopsie round trip (fake handler only), the Yes/No dialog itself (test seam), a live Keincheck
  session. `--render-all` renders the tab without a quest service, so the art, title and Fix day button do not appear
  in its PNG (`--render-all`: 221/221 views rendered; `QuestsTabView.png` shows the placeholder state only).
