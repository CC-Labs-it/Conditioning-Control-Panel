# Avalonia test plan (alpha / beta testers)

This is the hands-on checklist for the Linux app (the Avalonia head). Every area of
[`avalonia-parity.md`](avalonia-parity.md) has a section here. You compare the Linux app with the
Windows app ("WPF"). The `WPF file:line` after each step is the source line the expected behaviour
comes from, so a developer can check a report quickly; you only need the Windows app itself.
Paths are under `ConditioningControlPanel/` unless they start with `CCP.Core/` (Windows code that moved to
the shared engine). Line numbers are the ones recorded in the ledger and may drift by a few lines.

## How to report a result

One report per row id (a GitHub issue titled `[test] <row id>: pass` or `[test] <row id>: fail`), with:

- **Row id** (the `code` name in the step or in the row list), and the step number if it has one.
- **Pass or fail.**
- **What differed from Windows**: text, order, timing, sound, a missing button, a crash. "Same as Windows" is a valid pass note.
- **Screenshot or short screen recording** of the Linux app (and of Windows when they differ).
- Your distro, desktop (KDE, GNOME, ...), X11 or Wayland, and the app version (Settings -> Updates).

A tester report is evidence that a developer adds to the ledger row, next to the automated test and the
developers' own check of the running app; together they move a row to `verified`.

## Before you start

- Back up your settings folder first (`~/.local/share/ConditioningControlPanel`), or use a spare Linux user.
- Use a test account for anything that spends points, tokens or lock time. Never a wallet you care about.
- "Panic key" means the key in Settings (default Escape). "Rail" is the left navigation bar. "Tray" is the
  system tray icon menu.
- Some areas are not on Linux yet (the ledger says `missing`). Check that nothing broken shows up instead,
  and report it if it does; there is nothing else to compare.

**[SAFETY] steps** (panic, Lockdown, stop and lock paths) are marked **[SAFETY] developers re-check this
before release**. Please test them, but a tester pass alone does not clear them: the developers repeat them
on the release build first.

**General check (G)**, for every row in a "Rows" list below: open the screen named in the row (the
Avalonia column of the ledger), click every button, toggle and tab on it, change the language once, and
compare text, order, layout, sound and timing with the same screen on Windows. Report differences.

## 1. Shell and platform (`shell-platform`)

### Start, stop and panic
1. Home -> press Start. Expect the engine to run and the button to read Stop; press Stop -> everything ends. WPF `MainWindow/MainWindow.StartStop.cs:35`. Rows: `shell-start-stop`.
2. **[SAFETY] developers re-check this before release.** Lab -> Lockdown on, then press Stop. Expect a refusal message and nothing stops. WPF `MainWindow/MainWindow.StartStop.cs:45`. Rows: `shell-start-stop`, `shell-session-feature-lock`.
3. **[SAFETY] developers re-check this before release.** Under Lockdown press the window close button. Expect the window to stay open (not even hidden). WPF `MainWindow/MainWindow.WindowChrome.cs:126-131`. Rows: `shell-window-chrome`, `shell-main-window`.
4. **[SAFETY] developers re-check this before release.** Under Lockdown pick tray -> Exit. Expect nothing to happen. WPF `MainWindow/MainWindow.Launcher.cs:184`. Rows: `shell-tray-icon`.
5. **[SAFETY] developers re-check this before release.** With the engine running press the panic key once. Expect every effect, sound and overlay to stop; the app stays open. WPF `MainWindow/MainWindow.xaml.cs:1548`, `:1934`. Rows: `shell-start-stop`, `shell-global-hotkeys`.
6. **[SAFETY] developers re-check this before release.** With nothing running press the panic key twice within 2 s. Expect the app to exit at once, with no Chaster bill. WPF `MainWindow/MainWindow.xaml.cs:1801-1905`. Rows: `shell-chaster-bill`, `shell-start-stop`.
7. **[SAFETY] developers re-check this before release.** Tray -> Stop everything (Linux only; Windows has the panic key instead). Expect the same result as one panic press. WPF `MainWindow/MainWindow.xaml.cs:1934`. Rows: `shell-tray-icon`.
8. Tray menu: expect Show the window, Back to launcher (only while the launcher is open; greyed under Lockdown), Wake them up (Wake Bambi up in Bambi mode), then Stop everything (Linux only) and Exit. Windows also shows Cut leash after Wake while you are leashed; Linux does not have it yet (known gap, not a failure). WPF `Services/Notifications/TrayIconService.cs:98-135`. Rows: `shell-tray-icon`.
9. Chaster linked with a bill: tray -> Exit. Expect Circe's bill to hold the exit for a few seconds or one click, then the app closes. WPF `MainWindow/MainWindow.Launcher.cs:182`. Rows: `shell-chaster-bill`.
10. Close the window (not under Lockdown). Expect it to go to the tray with a "still running" notification. WPF `Services/Notifications/TrayIconService.cs:191`. Rows: `shell-tray-icon`, `shell-window-chrome`.

### Navigation, window and startup
11. Click every rail entry in turn. Expect each tab to open and the rail highlight to follow. WPF `MainWindow/MainWindow.TabNavigation.cs:868-895`. Rows: `shell-tab-navigation`, `shell-nav-rail`, `ctrl-section-tab-strip`.
12. Resize, maximise and restore the window on a small screen. Expect it to stay inside the screen. Rows: `shell-work-area-fit`, `shell-window-chrome`.
13. Start the app a second time. Expect the running window to come to the front, no second copy. Rows: `shell-single-instance`.
14. Quit and start again. Expect the splash, then the launcher or the panel as on Windows. WPF `App.xaml.cs:3149`. Rows: `shell-app-startup`, `shell-launcher`.
15. Settings -> set a global hotkey, focus another app, press it. Expect the action to run. WPF `MainWindow/MainWindow.xaml.cs:914`. Rows: `shell-global-hotkeys`.
16. Settings -> Updates -> check for updates. Expect the same version text and dialogs as Windows. WPF `App.xaml.cs:4858`. Rows: `shell-auto-updater`, `shell-ui-updates`.
17. Settings -> Account -> sign in with Patreon, then Discord. Expect the tier card to update; sign out clears it. WPF `App.xaml.cs:2267`. Rows: `shell-login`, `shell-patreon`, `shell-subscribestar`, `shell-secret-store`, `shell-account-shell`.

### Tabs and features hosted by the shell
18. **[SAFETY] developers re-check this before release.** Takeover tab -> enable, press Test. Expect a takeover action to run; panic stops it but keeps the switch on. WPF `MainWindow/MainWindow.StartStop.cs:485`. Rows: `shell-autonomy`, `shell-takeover-ui`.
19. Awareness: turn it on (consent), switch between apps. Expect the companion to react to the window title (X11 apps only). WPF `MainWindow/MainWindow.CompanionRoom.cs:236`. Rows: `shell-awareness`.
20. Programs tab -> enrol in a program. Expect the banner and step list as on Windows. WPF `MainWindow/MainWindow.ProgramsTab.cs:605`, `:2251`. Rows: `shell-programs-tab`, `shell-program-banner`.
21. Presets tab -> apply, save and import a preset. WPF `MainWindow/MainWindow.Presets.cs:2202-2381`, `MainWindow/MainWindow.SessionIO.cs:453`. Rows: `shell-presets`, `shell-preset-io`, `shell-session-io`.
22. Assets tab -> tick and untick folders, preview a file. WPF `MainWindow/MainWindow.TabNavigation.cs:433-438`. Rows: `shell-assets`, `shell-assets-fx`.
23. **[SAFETY] developers re-check this before release.** Blink Trainer tab -> start a session with the webcam. Expect the dot to breathe while running and panic to stop it. WPF `MainWindow/MainWindow.BlinkTrainer.cs:671`. Rows: `shell-blink-trainer`, `shell-webcam`.
24. Companion tab and Her Room: open each page. WPF `MainWindow/MainWindow.CompanionRoom.cs:121-178`, `MainWindow/MainWindow.Patreon.cs:1266`. Rows: `shell-companion`, `shell-companion-room`, `shell-companion-tab`.
25. Spiral room, Lab tab, Quests, Achievements, Leaderboard, Roadmap, Profile: open each and use every control. WPF `MainWindow/MainWindow.SpiralRoom.cs:76`, `MainWindow/MainWindow.LabTab.cs:134-153`, `App.xaml.cs:414`. Rows: `shell-spiral-room`, `shell-lab-tab`, `shell-quests`, `shell-achievements-tab`, `shell-leaderboard`, `shell-roadmap`, `shell-profile-card`.
26. Chaster tab: link a test lock. Expect the rail padlock and hero to update. WPF `App.xaml.cs:3149`. Rows: `shell-chaster`.
27. **[SAFETY] developers re-check this before release.** Remote Control: let a controller connect, press panic. Expect the controller to drop and everything to stop. WPF `MainWindow/MainWindow.RemoteControl.cs:1401`. Rows: `shell-remote-control`.

### Possession
28. Start a session with Possession on; watch shell buttons nudge, drift, breathe or wobble, then ease home. WPF `Services/Possession/Possession.cs`. Rows: `shell-possession`.
29. **[SAFETY] developers re-check this before release.** Expect the panic control, the Lockdown tab and the safe word never to be possessed. WPF `Services/Possession/PossessionOffLimitsTree.cs`. Rows: `shell-possession`.
30. **[SAFETY] developers re-check this before release.** With two or more effects moving, press panic, then tray -> Stop everything. Expect every moved control back in place at once. Rows: `shell-possession`.

### Web pages inside the app
31. Search for "intake" -> start Graded Intake. Expect the page to draw and keep moving (not a frozen frame). WPF `MainWindow/MainWindow.Lab.cs:343`. Rows: `shell-webview2-hosts`, `views-tab-graded-intake`.
32. Browser site buttons and Reload: each navigates and the status line says connected. WPF `MainWindow/MainWindow.Browser.cs:132-158`. Rows: `shell-browser`.

### Tray friends line
33. Have a friend poke you, hover the tray icon. Expect "Conditioning Control Panel" plus a 1-unread friends line; open and close the friends drawer, hover again: the line is gone. WPF `Services/Friends/FriendsFeedTray.cs:18`. Rows: `shell-tray-icon`, `feat-friends-feed`.

### Rows (general check G)
- [ ] `shell-account-shell` - WPF `MainWindow/MainWindow.AccountShell.cs:261`
- [ ] `shell-achievements-tab` - WPF `MainWindow/MainWindow.AchievementsTab.cs`
- [ ] `shell-ambient-fx` - WPF `MainWindow/MainWindow.AmbientFx.cs`
- [ ] `shell-animations` - WPF `MainWindow/MainWindow.Animations.cs`
- [ ] `shell-assets` - WPF `MainWindow/MainWindow.TabNavigation.cs:433-438`
- [ ] `shell-button-accessible-name` - WPF `ButtonBaseAutomationPeer`
- [ ] `shell-assets-fx` - WPF `MainWindow/MainWindow.AssetsFx.cs`
- [ ] `shell-autonomy` - WPF `MainWindow/MainWindow.StartStop.cs:485`
- [ ] `shell-awareness` - WPF `MainWindow/MainWindow.CompanionRoom.cs:236`
- [ ] `shell-bank-fx` - WPF `MainWindow/MainWindow.BankFx.cs`
- [ ] `shell-blink-trainer` - WPF `MainWindow/MainWindow.BlinkTrainer.cs:671`
- [ ] `shell-browser` - WPF `MainWindow/MainWindow.Browser.cs:132-158`
- [ ] `shell-catalogue-submissions` - WPF `MainWindow/MainWindow.CatalogueSubmissions.cs`
- [ ] `shell-chaster-bill` - WPF `MainWindow/MainWindow.ChasterBill.cs`
- [ ] `shell-chaster` - WPF `App.xaml.cs:3149`
- [ ] `shell-chrome-fx` - WPF `MainWindow/MainWindow.ChromeFx.cs`
- [ ] `shell-cloud-backup` - WPF `MainWindow/MainWindow.CloudBackup.cs`
- [ ] `shell-companion` - WPF `MainWindow/MainWindow.Companion.cs`
- [ ] `shell-companion-fx` - WPF `MainWindow/MainWindow.CompanionFx.cs`
- [ ] `shell-companion-room` - WPF `MainWindow/MainWindow.CompanionRoom.cs:121-178`
- [ ] `shell-companion-tab` - WPF `MainWindow/MainWindow.Patreon.cs:1266`
- [ ] `shell-dashboard-billboard` - WPF `MainWindow/MainWindow.DashboardBillboard.cs`
- [ ] `shell-dashboard-fold` - WPF `MainWindow/MainWindow.DashboardFold.cs`
- [ ] `shell-dashboard-fx` - WPF `Features/FeatureCard.xaml.cs:679-688`
- [ ] `shell-deeper-fx` - WPF `MainWindow/MainWindow.DeeperFx.cs`
- [ ] `shell-deeper-hub` - WPF `MainWindow/MainWindow.DeeperHub.cs`
- [ ] `shell-deeper-submissions` - WPF `MainWindow/MainWindow.DeeperSubmissions.cs`
- [ ] `shell-deeper-tab` - WPF `MainWindow/MainWindow.DeeperTab.cs`
- [ ] `shell-descent-fuse` - WPF `MainWindow/MainWindow.DescentFuse.cs`
- [ ] `shell-enhancements` - WPF `MainWindow/MainWindow.Enhancements.cs`
- [ ] `shell-enhancements-fx` - WPF `MainWindow/MainWindow.EnhancementsFx.cs`
- [ ] `shell-event-fx` - WPF `MainWindow/MainWindow.EventFx.cs`
- [ ] `shell-exclusives` - WPF `MainWindow/MainWindow.Exclusives.cs`
- [ ] `shell-favorites-rail` - WPF `Views/Tabs/SettingsTabView.xaml:349`
- [ ] `shell-haptics` - WPF `MainWindow/MainWindow.Haptics.cs`
- [ ] `shell-heartbeat` - WPF `Services/Settings/ProfileSyncService.cs:712`
- [ ] `server-sync-contract` - WPF `/v2/user/sync`
- [ ] `shell-hero-fx` - WPF `MainWindow/MainWindow.HeroFx.cs`
- [ ] `shell-home-audio` - WPF `MainWindow/MainWindow.HomeAudio.cs`
- [ ] `shell-inbox` - WPF `MainWindow/MainWindow.Inbox.cs`
- [ ] `shell-just-drop` - WPF `MainWindow/MainWindow.JustDrop.cs`
- [ ] `shell-keyword-triggers` - WPF `MainWindow/MainWindow.KeywordTriggers.cs`
- [ ] `shell-lab` - WPF `MainWindow/MainWindow.Lab.cs`
- [ ] `shell-lab-tab` - WPF `MainWindow/MainWindow.LabTab.cs:134-153`
- [ ] `shell-launcher` - WPF `MainWindow/MainWindow.Launcher.cs`
- [ ] `shell-leaderboard` - WPF `MainWindow/MainWindow.Leaderboard.cs`
- [ ] `shell-leaderboard-fx` - WPF `MainWindow/MainWindow.LeaderboardFx.cs`
- [ ] `shell-login` - WPF `App.xaml.cs:2267`
- [ ] `shell-secret-store` - WPF `Services/Auth/SecureAuthTokenStore.cs`
- [ ] `shell-marquee` - WPF `MainWindow/MainWindow.Marquee.cs`
- [ ] `shell-marquee-reads` - WPF `MainWindow/MainWindow.MarqueeReads.cs`
- [ ] `shell-mod-catalogue` - WPF `MainWindow/MainWindow.ModCatalogue.cs`
- [ ] `shell-nav-premium-tags` - WPF `MainWindow/MainWindow.NavPremiumTags.cs` (gone from Windows; nothing to test)
- [ ] `shell-nav-rail` - WPF `MainWindow/MainWindow.NavRail.cs`
- [ ] `ctrl-section-tab-strip` - WPF `Controls/NavRail/SectionTabStrip.xaml`
- [ ] `shell-patreon` - WPF `MainWindow/MainWindow.Patreon.cs`
- [ ] `shell-play-tab` - WPF `MainWindow/MainWindow.PlayTab.cs`
- [ ] `shell-possession` - WPF `MainWindow/MainWindow.Possession.cs`
- [ ] `shell-preset-io` - WPF `MainWindow/MainWindow.PresetIO.cs`
- [ ] `shell-presets` - WPF `MainWindow/MainWindow.Presets.cs:2202-2381`
- [ ] `shell-profile-bubble` - WPF `MainWindow/MainWindow.ProfileBubble.cs`
- [ ] `shell-profile-card` - WPF `MainWindow/MainWindow.Browser.cs:1963-1981`
- [ ] `shell-profile-cosmetics` - WPF `MainWindow/MainWindow.ProfileCosmetics.cs`
- [ ] `shell-profile-faucet` - WPF `MainWindow/MainWindow.ProfileFaucet.cs`
- [ ] `shell-profile-fx` - WPF `MainWindow/MainWindow.ProfileFx.cs`
- [ ] `shell-profile-spiral` - WPF `MainWindow/MainWindow.ProfileSpiral.cs`
- [ ] `shell-profile-vat` - WPF `MainWindow/MainWindow.ProfileVat.cs`
- [ ] `shell-profile-wardrobe` - WPF `MainWindow/MainWindow.ProfileWardrobe.cs`
- [ ] `shell-program-banner` - WPF `MainWindow/MainWindow.ProgramsTab.cs:2251`
- [ ] `shell-programs-fx` - WPF `MainWindow/MainWindow.ProgramsFx.cs`
- [ ] `shell-programs-tab` - WPF `MainWindow/MainWindow.ProgramsTab.cs:605`
- [ ] `shell-quests` - WPF `App.xaml.cs:414`
- [ ] `shell-quests-tab` - WPF `MainWindow/MainWindow.QuestsTab.cs`
- [ ] `shell-quest-stamps` - WPF `MainWindow/MainWindow.QuestStamps.cs`
- [ ] `shell-remember` - WPF `MainWindow/MainWindow.Remember.cs`
- [ ] `shell-remote-control` - WPF `MainWindow/MainWindow.RemoteControl.cs`
- [ ] `shell-roadmap` - WPF `MainWindow/MainWindow.Roadmap.cs`
- [ ] `shell-session-feature-lock` - WPF `MainWindow/MainWindow.SessionFeatureLock.cs`
- [ ] `shell-session-io` - WPF `MainWindow/MainWindow.SessionIO.cs:453`
- [ ] `shell-settings` - WPF `MainWindow/MainWindow.Settings.cs`
- [ ] `shell-she-listening` - WPF `Services/AutonomyService.Voice.cs:146`
- [ ] `shell-sparkle-wallet` - WPF `MainWindow/MainWindow.SparkleWallet.cs`
- [ ] `shell-spiral-room` - WPF `MainWindow/MainWindow.SpiralRoom.cs:76`
- [ ] `shell-start-stop` - WPF `MainWindow/MainWindow.StartStop.cs:58`
- [ ] `shell-subjects-fx` - WPF `MainWindow/MainWindow.SubjectsFx.cs`
- [ ] `shell-subscribestar` - WPF `MainWindow/MainWindow.SubscribeStar.cs`
- [ ] `shell-tab-fx-presets-quests-achievements` - WPF `MainWindow/MainWindow.TabFxPresetsQuestsAchievements.cs`
- [ ] `shell-tab-fx-takeover-lab-status` - WPF `MainWindow/MainWindow.TabFxTakeoverLabStatus.cs`
- [ ] `shell-tab-navigation` - WPF `MainWindow/MainWindow.TabNavigation.cs:868-895`
- [ ] `shell-takeaway` - WPF `MainWindow/MainWindow.Takeaway.cs:107-185`
- [ ] `shell-takeover-ui` - WPF `MainWindow/MainWindow.TakeoverUi.cs`
- [ ] `shell-tooltip-hygiene` - WPF `MainWindow/MainWindow.ToolTipHygiene.cs`
- [ ] `shell-ui-updates` - WPF `MainWindow/MainWindow.xaml.cs:3726`
- [ ] `shell-window-chrome` - WPF `MainWindow/MainWindow.WindowChrome.cs`
- [ ] `shell-work-area-fit` - WPF `MainWindow/MainWindow.WorkAreaFit.cs`
- [ ] `shell-main-window` - WPF `MainWindow/MainWindow.xaml`
- [ ] `shell-app-startup` - WPF `App.xaml`
- [ ] `shell-tray-icon` - WPF `Services/Notifications/TrayIconService.cs:102`
- [ ] `shell-global-hotkeys` - WPF `MainWindow/MainWindow.xaml.cs:914`
- [ ] `shell-clickthrough-overlays` - WPF `Services/Notifications/OverlayService.cs`
- [ ] `shell-multi-monitor` - WPF `Services/Video/DualMonitorVideoService.cs`
- [ ] `shell-webview2-hosts` - WPF `Chaos/ChaosWebViewHost.cs`
- [ ] `shell-audio-playback-devices` - WPF `Services/AudioService.cs`
- [ ] `shell-video-playback` - WPF `Services/Video/VideoService.cs`
- [ ] `shell-webcam` - WPF `Services/Webcam/WebcamTrackingService.cs`
- [ ] `shell-haptics-device` - WPF `CCP.Core/Services/Haptics/HapticService.cs`
- [ ] `shell-notifications-toasts` - WPF `Services/Notifications/NotificationService.cs`
- [ ] `shell-auto-updater` - WPF `App.xaml.cs:4858`
- [ ] `shell-single-instance` - WPF `App.xaml.cs`
- [ ] `shell-file-dialogs` - WPF `MainWindow/MainWindow.Assets.cs`
- [ ] `svc-prize-ownership` - WPF `Services/Prizes/OwnershipService.cs`
- [ ] `svc-discord-rich-presence` - WPF `Services/BubbleService.cs:244`
- [ ] `svc-media-session-watcher` - WPF `Services/Awareness/SmtcMediaWatcher.cs`
- [ ] `svc-content-packs` - WPF `App.xaml.cs:3163`
- [ ] `game-race` - WPF `Services/Chaos/CaucusHostService.cs:132`
- [ ] `shell-installer` - WPF `installer.iss` (repo root)

## 2. Windows (`windows`)

### Panic key on pop-up windows
Set the panic key to Enter for steps 1-4 (Settings -> panic key).
1. **[SAFETY] developers re-check this before release.** Mandatory video: press Escape (pause card), Tab to Resume, press Enter. Expect the video to stop and close, not resume. WPF `Services/Video/VideoService.cs:361`, `Windows/GracePauseOverlayWindow.cs:61`. Rows: `win-grace-pause-overlay`.
2. **[SAFETY] developers re-check this before release.** Bubble Count game: press Enter during the clip. Expect the game to close. WPF `Services/BubbleCountService.cs:353`. Rows: `win-bubble-count`.
3. **[SAFETY] developers re-check this before release.** Bubble Count answer box: type the right count, press Enter. Expect the windows to close with no "CORRECT!" and no XP. WPF `Services/BubbleCountService.cs:373`. Rows: `win-bubble-count`, `win-bubble-count-result`.
4. **[SAFETY] developers re-check this before release.** First show (welcome): Tab to a choice, press Enter. Expect the show to close without taking the choice. WPF `MainWindow/MainWindow.xaml.cs:546-553`. Rows: `feat-first-show`.
5. **[SAFETY] developers re-check this before release.** Repeat step 1 with the panic key set to F12: Enter must not stop anything. Rows: `win-grace-pause-overlay`.

### Friend invite card
6. Have a friend send you a Back Room invite while the panel is open. Expect a "dling" sound and a card at the panel's top-right with name, art, Not now and x, a countdown ring; the panel keeps keyboard focus. WPF `Windows/Friends/KnockCard.cs:341`. Rows: `win-knock-card`, `feat-friends-landing`.
7. A second invite stacks under the first. After 45 s both shrink; hovering one opens it again. WPF `Windows/Friends/KnockCard.cs`. Rows: `win-knock-card`.
8. Click x: the card goes to the Inbox; opening the Inbox row brings it back. Click Not now: "sink" sound, card and row gone. WPF `Windows/Friends/KnockCard.cs`. Rows: `win-knock-card`, `shell-inbox`.
9. **[SAFETY] developers re-check this before release.** Press the panic key with cards up. Expect every card to fold into the Inbox; panic otherwise behaves as usual. WPF `Windows/Friends/KnockCard.cs`. Rows: `win-knock-card`.
10. Start a session with a card up. Expect it to fold into the Inbox within 3 s; new invites wait until the session ends. WPF `Windows/Friends/KnockCard.cs`. Rows: `win-knock-card`.
11. A Ramp invite: with the panel hidden behind the launcher, press Join. Expect the panel on the Studio tab. WPF `Windows/Friends/KnockCard.cs`. Rows: `win-knock-card`, `feat-friends-landing`.

### Other windows
12. First start on a fresh profile: the first-run wizard walks every page, Back and Next work. WPF `Windows/FirstRunWizard.xaml.cs:913-969`. Rows: `win-first-run-wizard`, `win-splash-screen`.
13. **[SAFETY] developers re-check this before release.** Lock card: start one, type the phrase wrong, then right. Expect it to stay until the right phrase; panic closes it. WPF `Windows/LockCardWindow.xaml.cs:763`. Rows: `win-lock-card`.
14. Unlock an achievement and finish a quest. Expect the pop-ups with the same text and sound. Rows: `win-achievement-popup`, `win-quest-complete`, `win-item-unlocked`.
15. Lab -> Quiz: play a round, report a question, edit a category. WPF `MainWindow/MainWindow.Lab.cs:116`, `:468`, `Windows/QuizWindow.xaml.cs:356`. Rows: `win-quiz`, `win-quiz-report`, `win-quiz-category-editor`, `win-pop-quiz`.
16. Finish a session. Expect the summary, then the log history lists it. WPF `Windows/SessionLogHistoryWindow.xaml.cs:46`, `MainWindow/MainWindow.Presets.cs:1551`. Rows: `win-session-complete`, `win-session-log-history`, `win-session-editor`.
17. Launcher: open every tile, then Back to the panel. WPF `CCP.Core/Services/BackRoom/BackRoomBridge.cs:208`. Rows: `win-launcher`, `win-launcher-media-dialog`.
18. Help -> Report a bug: fill and send to a test address. WPF `Windows/BugReportWindow.xaml.cs:87-101`. Rows: `win-bug-report`, `win-my-reports`.
19. Webcam: calibrate, quick recalibrate, gaze tracker. WPF `MainWindow/MainWindow.LabTab.cs:1094`, `MainWindow/MainWindow.xaml.cs:3631`. Rows: `win-webcam-calibration`, `win-webcam-quick-recal`, `win-webcam-gaze-tracker`, `win-webcam-loading-splash`.
20. Emi desk: open it, its options, ring and book; mute prompt. WPF `AvatarTube/AvatarTubeWindow.Speech.cs:461`. Rows: `win-emi-desk`, `win-emi-options`, `win-emi-ring`, `win-emi-book`, `win-emi-codex`, `win-emi-mute-prompt`.
21. Settings palette shortcut opens the command palette. WPF `MainWindow/MainWindow.xaml.cs:280-283`. Rows: `win-settings-palette`.

### Down the Rabbit Hole game
22. Launcher -> Down the Rabbit Hole -> Play (signed in). Expect the game window and the Warren hub within 45 s. WPF `Services/Chaos/DtrhHostService.cs:94`. Rows: `win-launcher`.
23. Start a descent. Expect the tube to draw and bubbles to spawn; note any missing pictures or sounds. Rows: `win-launcher`.
24. **[SAFETY] developers re-check this before release.** Panic key = Escape: press it once mid-descent. Expect the game to pause and stay open; click the title bar, wait 2 s, press again: it still only pauses. WPF `MainWindow/MainWindow.xaml.cs:1651`, `Services/Chaos/DtrhHostService.cs:231`. Rows: `win-launcher`.
25. **[SAFETY] developers re-check this before release.** Press Escape twice within 2 s. Expect the game window to close and the app to stay up; tray -> Stop everything also closes it. Compare carefully: on Windows the game owns Escape (pause, hold to leave). Rows: `win-launcher`.
26. Settings -> Motion Reduced, then Off; reopen the game. Expect calmer, then no motion. Rows: `win-launcher`.
27. Without Lab access press Play. Expect no window and a "needs Lab" message. Rows: `win-launcher`.

### Rows (general check G)
- [ ] `win-achievement-popup` - WPF `Windows/AchievementPopup.xaml`
- [ ] `win-announcement-popup` - WPF `Windows/AnnouncementPopup.xaml`
- [ ] `win-bubble-count-result` - WPF `Windows/BubbleCountResultWindow.xaml`
- [ ] `win-bubble-count` - WPF `Services/BubbleCountService.cs:373`
- [ ] `win-bug-report` - WPF `Windows/BugReportWindow.xaml.cs:87-101`
- [ ] `win-corner-gif` - WPF `App.xaml.cs:480`
- [ ] `win-descent-fuse` - WPF `Services/Descent/DescentShowDirector.cs:130-170`
- [ ] `win-easter-egg` - WPF `Windows/EasterEggWindow.xaml`
- [ ] `win-emi-codex` - WPF `Windows/EmiCodexWindow.xaml`
- [ ] `win-emi-book` - WPF `Windows/EmiDesk/EmiBookWindow.xaml`
- [ ] `win-emi-desk` - WPF `AvatarTube/AvatarTubeWindow.Speech.cs:461`
- [ ] `win-emi-mute-prompt` - WPF `Windows/EmiDesk/EmiMutePromptWindow.xaml`
- [ ] `win-emi-options` - WPF `Windows/EmiDesk/EmiOptionsWindow.xaml`
- [ ] `win-emi-ring` - WPF `Windows/EmiDesk/EmiRingWindow.xaml`
- [ ] `win-feature-intro` - WPF `Windows/FeatureIntroPopup.xaml`
- [ ] `win-feature-settings` - WPF `Windows/FeatureSettingsPopup.xaml`
- [ ] `win-first-run-wizard` - WPF `Windows/FirstRunWizard.xaml.cs:913-969`
- [ ] `win-floating-word` - WPF `Windows/Friends/FloatingWord.cs`
- [ ] `win-friend-toast` - WPF `Windows/Friends/FriendToast.cs` (gone from Windows; nothing to test)
- [ ] `win-grace-pause-overlay` - WPF `Windows/GracePauseOverlayWindow.cs`
- [ ] `win-haptics-setup` - WPF `MainWindow/MainWindow.Patreon.cs:263-269`
- [ ] `win-help-video` - WPF `Windows/HelpVideoWindow.xaml`
- [ ] `win-item-unlocked` - WPF `Windows/ItemUnlockedPopup.xaml`
- [ ] `win-knock-card` - WPF `Windows/Friends/KnockCard.cs`
- [ ] `win-launcher-media-dialog` - WPF `Windows/Launcher/LauncherMediaDialog.xaml`
- [ ] `win-launcher` - WPF `CCP.Core/Services/BackRoom/BackRoomBridge.cs:208`
- [ ] `win-layered-audio` - WPF `Windows/LayeredAudioWindow.xaml`
- [ ] `win-lock-card` - WPF `Windows/LockCardWindow.xaml.cs:763`
- [ ] `linux-package-vosk-model` - WPF `installer/publish`
- [ ] `linux-package` - WPF `installer/publish`
- [ ] `linux-package-flatpak` - WPF `installer/publish`
- [ ] `win-mantra` - WPF `Windows/MantraWindow.xaml`
- [ ] `win-media-history` - WPF `Windows/MediaHistoryWindow.xaml`
- [ ] `win-mini-player` - WPF `MainWindow/MainWindow.Assets.cs:1233`
- [ ] `win-mod-creator` - WPF `Windows/ModCreatorWindow.xaml`
- [ ] `win-my-reports` - WPF `Windows/MyReportsWindow.xaml`
- [ ] `win-phrase-input` - WPF `Windows/FeatureSettingsPopup.xaml.cs`
- [ ] `win-pink-rush` - WPF `Windows/PinkRushPopup.xaml`
- [ ] `win-pop-quiz` - WPF `MainWindow/MainWindow.StartStop.cs:393`
- [ ] `win-programs-intro` - WPF `MainWindow/MainWindow.TabNavigation.cs:333`
- [ ] `win-quest-complete` - WPF `Windows/QuestCompletePopup.xaml`
- [ ] `win-quiz-category-editor` - WPF `Windows/QuizWindow.xaml.cs:356`
- [ ] `win-quiz-report` - WPF `MainWindow/MainWindow.Lab.cs:468`
- [ ] `win-quiz` - WPF `MainWindow/MainWindow.Lab.cs:116`
- [ ] `win-session-complete` - WPF `Windows/SessionLogHistoryWindow.xaml.cs:46`
- [ ] `win-session-editor` - WPF `MainWindow/MainWindow.SessionIO.cs:1739`
- [ ] `win-session-log-history` - WPF `MainWindow/MainWindow.Presets.cs:1551`
- [ ] `win-settings-palette` - WPF `MainWindow/MainWindow.xaml.cs:280-283`
- [ ] `win-whatmoved-card` - WPF `Windows/WhatMovedCard.xaml`
- [ ] `win-splash-screen` - WPF `Windows/SplashScreen.xaml`
- [ ] `win-tease-reveal` - WPF `Windows/TeaseRevealPopup.xaml`
- [ ] `win-tutorial-overlay` - WPF `Windows/TutorialOverlay.xaml`
- [ ] `win-webcam-calibration` - WPF `Windows/WebcamCalibrationWindow.xaml`
- [ ] `win-webcam-gaze-tracker` - WPF `MainWindow/MainWindow.LabTab.cs:1094`
- [ ] `win-webcam-loading-splash` - WPF `MainWindow/MainWindow.xaml.cs:3631`
- [ ] `win-webcam-quick-recal` - WPF `Windows/WebcamQuickRecalWindow.xaml`

## 3. Views (`views`)

1. Settings: open every section (General, Audio, Data, Devices, Notifications, Performance, Updates, Account, Emi desk). Expect the same options in the same order. WPF `MainWindow/MainWindow.Presets.cs:54`, `MainWindow/MainWindow.UiUpdates.cs:2701`. Rows: `views-settings-*`, `views-tab-settings`, `views-tab-app-settings`.
2. **[SAFETY] developers re-check this before release.** Lockdown tab: arm Lockdown, then try Stop, close, Exit and leaving the tab. Expect each to refuse with the Windows message; the wrong unlock phrase is refused. WPF `MainWindow/MainWindow.Lab.cs:672`, `:862`. Rows: `views-tab-lockdown`.
3. **[SAFETY] developers re-check this before release.** Lockdown on: press Pause in a session. Expect it refused. WPF `MainWindow/MainWindow.Presets.cs:2033`. Rows: `views-tab-lockdown`, `views-tab-presets`.
4. **[SAFETY] developers re-check this before release.** Her Room -> chat History link opens "Chat history"; press panic. Expect it to close; a second press does nothing odd. WPF `Views/Controls/Companion/Runtime/CompanionTranscriptWindow.cs:121`. Rows: `views-companion-transcript-window`.
5. Companion pages: Engine room, Memory diary, Constellation, Workshop cells, Make her yours, keyword triggers, chat threshold. WPF `MainWindow/MainWindow.CompanionTab.cs:85`, `MainWindow/MainWindow.xaml.cs:3164-3383`. Rows: `views-companion-*`.
6. Studio: Ramp and Scheduler racks, Brain Drain card. WPF `Views/Tabs/StudioTabView.xaml.cs:1231`. Rows: `views-tab-studio`, `views-studio-*`.
7. Achievements: filters, reward band, tile effects. WPF `MainWindow/MainWindow.AchievementsTab.cs:886`. Rows: `views-tab-achievements`.
8. Quests: daily cards, reroll, stamps. WPF `MainWindow/MainWindow.Animations.cs:35`. Rows: `views-tab-quests`, `views-daily-quest-card`.
9. Presets: load, save, session import/export. WPF `MainWindow/MainWindow.SessionIO.cs:911-972`. Rows: `views-tab-presets`.
10. Awareness tab: consent, privacy rules, app picker. WPF `App.xaml.cs:1574`. Rows: `views-tab-awareness`, `views-companion-awareness-privacy`.
11. **[SAFETY] developers re-check this before release.** Remote Control tab: share a code, let a controller in, end it from both sides. WPF `MainWindow/MainWindow.RemoteControl.cs:325-480`. Rows: `views-tab-remote-control`.
12. Blink Trainer, Haptics, Discord, Leaderboard, Deeper, Enhancements, Exclusives, Friends, Leash, Play, Programs, She's Listening, Spiral, Chaster, Subjects, Takeover tabs: open each, use every control. WPF `MainWindow/MainWindow.Presets.cs:118` (Blink Trainer). Rows: `views-tab-*`.
13. Deeper editor: new enhancement, URL prompt, gaze picker, play one. Rows: `views-deeper-*`.

### Rows (general check G)
- [ ] `views-adorned-avatar` - WPF `Views/Controls/AdornedAvatar.xaml`
- [ ] `views-settings-account` - WPF `Views/Controls/AppSettings/AccountSettingsSection.xaml`
- [ ] `views-settings-audio` - WPF `MainWindow/MainWindow.Presets.cs:54`
- [ ] `views-settings-data` - WPF `MainWindow/MainWindow.UiUpdates.cs:2701`
- [ ] `views-settings-devices` - WPF `MainWindow/MainWindow.LabTab.cs:1094`
- [ ] `views-settings-emidesk` - WPF `CCP.Core/Services/EmiDesk/EmiLineEngine.cs:946`
- [ ] `views-settings-general` - WPF `Views/Controls/AppSettings/GeneralSettingsSection.xaml`
- [ ] `views-settings-notifications` - WPF `Views/Controls/AppSettings/NotificationsSettingsSection.xaml`
- [ ] `views-settings-performance` - WPF `Views/Controls/AppSettings/PerformanceSettingsSection.xaml`
- [ ] `views-settings-updates` - WPF `Views/Controls/AppSettings/UpdatesSettingsSection.xaml`
- [ ] `views-companion-ai-permissions-grid` - WPF `MainWindow/MainWindow.Patreon.cs:1619`
- [ ] `views-companion-attention-gauge` - WPF `MainWindow/MainWindow.TabNavigation.cs:126`
- [ ] `views-companion-awareness-privacy` - WPF `Views/Controls/Companion/AwarenessPrivacyView.xaml`
- [ ] `views-companion-chat-threshold` - WPF `Views/Controls/Companion/ChatThresholdView.xaml`
- [ ] `views-companion-hero-card` - WPF `App.xaml.cs:380`
- [ ] `views-companion-room-preview` - WPF `Views/Controls/Companion/CompanionRoomPreviewWindow.xaml`
- [ ] `views-companion-room` - WPF `Views/Controls/Companion/CompanionRoomView.xaml`
- [ ] `views-companion-engine-room-drawer` - WPF `Views/Controls/Companion/EngineRoomDrawer.xaml`
- [ ] `views-companion-keyword-triggers` - WPF `Views/Controls/Companion/KeywordTriggersPanel.xaml`
- [ ] `views-companion-make-her-yours` - WPF `Views/Controls/Companion/MakeHerYoursView.xaml`
- [ ] `views-companion-memory-diary` - WPF `Views/Controls/Companion/MemoryDiaryView.xaml`
- [ ] `views-companion-relationship-constellation` - WPF `Views/Controls/Companion/RelationshipConstellation.xaml`
- [ ] `views-companion-workshop-awareness-cell` - WPF `Views/Controls/Companion/Runtime/WorkshopAwarenessCell.xaml`
- [ ] `views-companion-workshop-behavior-cell` - WPF `Views/Controls/Companion/Runtime/WorkshopBehaviorCell.xaml`
- [ ] `views-companion-workshop-community-cell` - WPF `Views/Controls/Companion/Runtime/WorkshopCommunityCell.xaml`
- [ ] `views-companion-workshop-library-cell` - WPF `MainWindow/MainWindow.xaml.cs:3164-3383`
- [ ] `views-companion-workshop-roster-cell` - WPF `MainWindow/MainWindow.CompanionTab.cs:85`
- [ ] `views-companion-workshop-triggers-cell` - WPF `Views/Controls/Companion/Runtime/WorkshopTriggersCell.xaml`
- [ ] `views-companion-workshop-accordion` - WPF `Views/Controls/Companion/WorkshopAccordion.xaml`
- [ ] `views-companion-transcript-window` - WPF `Views/Controls/Companion/Runtime/CompanionTranscriptWindow.cs`
- [ ] `views-daily-quest-card` - WPF `Views/Controls/DailyQuestCard.xaml`
- [ ] `views-emi-ring-picker` - WPF `Views/Controls/EmiRingPicker.xaml`
- [ ] `views-profile-privacy-panel` - WPF `Views/Controls/ProfilePrivacyPanel.xaml`
- [ ] `views-studio-brain-drain-feature` - WPF `Views/Controls/Studio/BrainDrainFeatureControl.xaml`
- [ ] `views-studio-ramp-rack` - WPF `Views/Controls/Studio/RampRackPanel.xaml`
- [ ] `views-studio-scheduler-rack` - WPF `Views/Controls/Studio/SchedulerRackPanel.xaml`
- [ ] `views-deeper-editor-window` - WPF `Views/Deeper/DeeperEditorWindow.xaml`
- [ ] `views-deeper-enhancement-player` - WPF `Views/Deeper/EnhancementPlayerWindow.xaml`
- [ ] `views-deeper-gaze-picker` - WPF `Views/Deeper/GazePickerWindow.xaml`
- [ ] `views-deeper-new-enhancement-dialog` - WPF `Views/Deeper/NewEnhancementDialog.xaml`
- [ ] `views-deeper-url-prompt-dialog` - WPF `Views/Deeper/UrlPromptDialog.xaml`
- [ ] `views-tab-achievements` - WPF `MainWindow/MainWindow.AchievementsTab.cs:886`
- [ ] `views-tab-app-settings` - WPF `Views/Tabs/AppSettingsTabView.xaml`
- [ ] `views-tab-assets` - WPF `Views/Tabs/AssetsTabView.xaml`
- [ ] `views-tab-available-subjects` - WPF `Views/Tabs/AvailableSubjectsTabView.xaml`
- [ ] `views-tab-awareness` - WPF `App.xaml.cs:1574`
- [ ] `views-tab-bambi-takeover` - WPF `Views/Tabs/BambiTakeoverTabView.xaml`
- [ ] `views-tab-blink-trainer` - WPF `MainWindow/MainWindow.Presets.cs:118`
- [ ] `views-tab-chaster` - WPF `Views/Tabs/ChasterTabView.xaml`
- [ ] `views-tab-companion` - WPF `Views/Tabs/CompanionTabView.xaml`
- [ ] `views-tab-deeper` - WPF `Views/Tabs/DeeperTabView.xaml`
- [ ] `views-tab-discord` - WPF `Views/Tabs/DiscordTabView.xaml`
- [ ] `views-tab-enhancements` - WPF `Views/Tabs/EnhancementsTabView.xaml`
- [ ] `views-tab-exclusives` - WPF `Views/Tabs/ExclusivesTabView.xaml`
- [ ] `views-tab-haptics` - WPF `Views/Tabs/HapticsTabView.xaml`
- [ ] `views-tab-graded-intake` - WPF `MainWindow/MainWindow.Lab.cs:343`
- [ ] `views-tab-leaderboard` - WPF `Views/Tabs/LeaderboardTabView.xaml`
- [ ] `views-tab-friends` - WPF `Views/Tabs/FriendsTabView.xaml`
- [ ] `views-tab-leash` - WPF `Views/Tabs/LeashTabView.xaml`
- [ ] `views-tab-lockdown` - WPF `MainWindow/MainWindow.Lab.cs:672`
- [ ] `views-tab-play` - WPF `Views/Tabs/PlayTabView.xaml`
- [ ] `views-tab-presets` - WPF `MainWindow/MainWindow.SessionIO.cs:911-972`
- [ ] `views-tab-programs` - WPF `Views/Tabs/ProgramsTabView.xaml`
- [ ] `views-tab-quests` - WPF `MainWindow/MainWindow.Animations.cs:35`
- [ ] `views-tab-remote-control` - WPF `MainWindow/MainWindow.RemoteControl.cs:325-480`
- [ ] `views-tab-settings` - WPF `Views/Tabs/SettingsTabView.xaml`
- [ ] `views-tab-she-listening` - WPF `Views/Tabs/SheListeningTabView.xaml`
- [ ] `views-tab-spiral` - WPF `Views/Tabs/SpiralTabView.xaml`
- [ ] `views-tab-studio` - WPF `Views/Tabs/StudioTabView.xaml.cs:1231`
- [ ] `views-companion-workshop-cell-theme` - WPF `Views/Controls/Companion/Runtime/WorkshopCellTheme.xaml`
- [ ] `views-companion-theme` - WPF `Views/Controls/Companion/Themes/CompanionTheme.xaml`

## 4. Dialogs (`dialogs`)

1. Sign in (Settings -> Account). Expect the login dialog, username picker on first sign-in, display name editor. WPF `App.xaml.cs:2267`, `Services/Settings/ProfileSyncService.cs:4161`. Rows: `dialogs-login`, `dialogs-username-picker`, `dialogs-display-name`, `dialogs-offline-username`.
2. Mod manager: open it, download a content pack, switch the active mod. WPF `MainWindow/MainWindow.xaml.cs:2910`, `:587-588`. Rows: `dialogs-mod-manager`, `dialogs-mod-picker`.
3. Consent dialogs: webcam, microphone, awareness, explicit content. Expect the same text; Decline leaves the feature off. WPF `AvatarTube/AvatarTubeWindow.ChatInput.cs:1191`. Rows: `dialogs-webcam-consent`, `dialogs-mic-consent`, `dialogs-awareness-consent`, `dialogs-explicit-content-acknowledgement`, `dialogs-content-policy-warning`.
4. Updates: trigger an update check with a newer version available; cancel; a failed download. Rows: `dialogs-update-notification`, `dialogs-update-progress`, `dialogs-update-failed`, `dialogs-whats-new`.
5. Companion: phrase editor, prompt editor, local AI setup wizard, OpenAI sampler, knowledge link, chat shortcut capture. WPF `App.xaml.cs:342`. Rows: `dialogs-companion-*`, `dialogs-local-ai-setup-wizard`, `dialogs-openai-sampler-settings`, `dialogs-knowledge-link-editor`, `dialogs-chat-shortcut-capture`.
6. Roadmap: start, step pop-up, diary, confirm. WPF `MainWindow/MainWindow.Roadmap.cs:457`. Rows: `dialogs-roadmap-*`.
7. Programs: enrol dialog. WPF `MainWindow/MainWindow.ProgramsTab.cs:1999`. Rows: `dialogs-program-enroll`.
8. Catalogue: pick from and submit to the catalogue (test entries only). WPF `MainWindow/MainWindow.DeeperTab.cs:1017-1204`, `MainWindow/MainWindow.DeeperHub.cs:225-266`, `MainWindow/MainWindow.Presets.cs:503`. Rows: `dialogs-catalogue-picker`, `dialogs-catalogue-submit`, `dialogs-asset-submit`.
9. Profile: customise, privacy, wardrobe, colour editor, lock-card colour. Rows: `dialogs-profile-*`, `dialogs-wardrobe-editor`, `dialogs-color-editor`, `dialogs-lock-card-color`.
10. Settings -> link phone: the QR/code dialog. WPF `Views/Tabs/SettingsTabView.xaml.cs:242`. Rows: `dialogs-link-phone`.
11. Awareness: preset detail, app picker, attention target editor. WPF `MainWindow/MainWindow.Awareness.cs:832`. Rows: `dialogs-awareness-*`, `dialogs-attention-target-editor`.

### Rows (general check G)
- [ ] `dialogs-asset-submit` - WPF `MainWindow/MainWindow.Presets.cs:503`
- [ ] `dialogs-attention-target-editor` - WPF `Dialogs/AttentionTargetEditorDialog.xaml`
- [ ] `dialogs-awareness-app-picker` - WPF `Dialogs/AwarenessAppPickerDialog.xaml`
- [ ] `dialogs-awareness-consent` - WPF `Dialogs/AwarenessConsentDialog.xaml`
- [ ] `dialogs-awareness-preset-detail` - WPF `MainWindow/MainWindow.Awareness.cs:832`
- [ ] `dialogs-catalogue-picker` - WPF `MainWindow/MainWindow.DeeperTab.cs:1017-1204`
- [ ] `dialogs-catalogue-submit` - WPF `MainWindow/MainWindow.DeeperHub.cs:225-266`
- [ ] `dialogs-chaster-import-confirm` - WPF `Dialogs/ChasterImportConfirmDialog.xaml`
- [ ] `dialogs-chat-shortcut-capture` - WPF `Dialogs/ChatShortcutCaptureDialog.xaml`
- [ ] `dialogs-color-editor` - WPF `Dialogs/ColorEditorDialog.xaml`
- [ ] `dialogs-companion-phrase-editor` - WPF `App.xaml.cs:342`
- [ ] `dialogs-companion-prompt-editor` - WPF `Dialogs/CompanionPromptEditorDialog.xaml`
- [ ] `dialogs-content-policy-warning` - WPF `AvatarTube/AvatarTubeWindow.xaml.cs:396`
- [ ] `dialogs-display-name` - WPF `Services/Settings/ProfileSyncService.cs:4161`
- [ ] `dialogs-explicit-content-acknowledgement` - WPF `AvatarTube/AvatarTubeWindow.ChatInput.cs:1191`
- [ ] `dialogs-input` - WPF `Dialogs/InputDialog.xaml`
- [ ] `dialogs-knowledge-link-editor` - WPF `Dialogs/KnowledgeLinkEditorDialog.xaml`
- [ ] `dialogs-link-phone` - WPF `Views/Tabs/SettingsTabView.xaml.cs:242`
- [ ] `dialogs-local-ai-setup-wizard` - WPF `Dialogs/LocalAiSetupWizard.xaml`
- [ ] `dialogs-lock-card-color` - WPF `Dialogs/LockCardColorDialog.xaml`
- [ ] `dialogs-login` - WPF `Dialogs/LoginDialog.xaml`
- [ ] `dialogs-mic-consent` - WPF `Dialogs/MicConsentDialog.xaml`
- [ ] `dialogs-mod-manager` - WPF `MainWindow/MainWindow.xaml.cs:2910`
- [ ] `dialogs-mod-picker` - WPF `MainWindow/MainWindow.xaml.cs:587-588`
- [ ] `dialogs-offline-username` - WPF `Dialogs/OfflineUsernameDialog.xaml`
- [ ] `dialogs-openai-sampler-settings` - WPF `Dialogs/OpenAiCompatibleSamplerSettingsDialog.xaml`
- [ ] `dialogs-profile-customize` - WPF `Dialogs/ProfileCustomizeDialog.xaml`
- [ ] `dialogs-profile-privacy` - WPF `Dialogs/ProfilePrivacyDialog.xaml`
- [ ] `dialogs-program-enroll` - WPF `MainWindow/MainWindow.ProgramsTab.cs:1999`
- [ ] `dialogs-roadmap-confirm` - WPF `Dialogs/RoadmapConfirmDialog.xaml`
- [ ] `dialogs-roadmap-diary` - WPF `Dialogs/RoadmapDiaryDialog.xaml`
- [ ] `dialogs-roadmap-start` - WPF `Dialogs/RoadmapStartDialog.xaml`
- [ ] `dialogs-roadmap-step-popup` - WPF `MainWindow/MainWindow.Roadmap.cs:457`
- [ ] `dialogs-text-editor` - WPF `Dialogs/TextEditorDialog.xaml`
- [ ] `dialogs-tube-fit` - WPF `Dialogs/TubeFitDialog.xaml`
- [ ] `dialogs-update-failed` - WPF `Dialogs/UpdateFailedDialog.xaml`
- [ ] `dialogs-update-notification` - WPF `Dialogs/UpdateNotificationDialog.xaml`
- [ ] `dialogs-update-progress` - WPF `Dialogs/UpdateProgressDialog.xaml`
- [ ] `dialogs-username-picker` - WPF `Dialogs/UsernamePickerDialog.xaml`
- [ ] `dialogs-wardrobe-editor` - WPF `Dialogs/WardrobeEditorDialog.xaml`
- [ ] `dialogs-vault-gate` - WPF `Dialogs/VaultGateDialog.cs`
- [ ] `dialogs-warning` - WPF `Dialogs/WarningDialog.xaml`
- [ ] `dialogs-webcam-consent` - WPF `Dialogs/WebcamConsentDialog.xaml`
- [ ] `dialogs-welcome-back-sheet` - WPF `Dialogs/WelcomeBackSheet.xaml`
- [ ] `dialogs-welcome` - WPF `Dialogs/WelcomeDialog.xaml`
- [ ] `dialogs-whats-new` - WPF `Dialogs/WhatsNewDialog.xaml`

## 5. Features and controls (`features-controls`)

### Effects and monitors (2 monitors needed for 1-4)
1. Settings -> Monitors -> "Show content on". Expect All monitors, Primary only, then `Monitor N (Primary, WxH)` per screen. WPF `Views/Controls/AppSettings/MonitorsSettingsSection.xaml:35`. Rows: `feat-monitor-target-picker`, `shell-multi-monitor`.
2. Pick Monitor 2; start pink filter, spiral, bubbles, a flash, a subliminal, bouncing text and a mandatory video. Expect each only on monitor 2. WPF `Views/Controls/AppSettings/MonitorsSettingsSection.xaml:35`. Rows: `feat-monitor-target-picker`, `feat-pink-filter`, `feat-spiral`, `feat-bubble-pop`, `feat-flash`, `feat-subliminal`, `feat-bouncing-text`, `feat-video`.
3. With them running switch to All monitors. Expect them on every screen without a restart. WPF `Views/Controls/AppSettings/MonitorsSettingsSection.xaml:35`. Rows: `feat-monitor-target-picker`.
4. Pick Monitor 2, quit, unplug it, start. Expect content on the primary; plug it back: Monitor 2 is chosen again. WPF `Views/Controls/AppSettings/MonitorsSettingsSection.xaml:35`. Rows: `feat-monitor-target-picker`.
5. **[SAFETY] developers re-check this before release.** Each effect card on Home/Studio: enable, change sliders, Test. Expect the same look and timing; panic clears all. WPF `MainWindow/MainWindow.Presets.cs:1360`, `:1380-1433`, `Services/BubbleService.cs:1089`, `Services/Subliminal/BouncingTextService.cs:372`, `Services/Video/VideoService.cs:3372`. Rows: `feat-*` effect rows, `feat-feature-card`.
6. Corner hit with Bouncing Text: one "did you see that" line per session. Rows: `feat-bouncing-text`.

### Takeover and Deeper cues
7. Takeover with announcements on: wait for an action. Expect "TAKEOVER" over the effect word near the top, with a glow and a short shake, gone after ~2 s; clicks pass through. WPF `Overlays/TakeoverAnnouncerOverlay.cs:69`. Rows: `overlay-takeover-announcer`.
8. Two actions back to back: the second cue waits for the first. Rows: `overlay-takeover-announcer`.
9. **[SAFETY] developers re-check this before release.** Press panic while a cue shows. Expect it gone at once and the queued one never shown. Rows: `overlay-takeover-announcer`.
10. Deeper enhancement with a "speak" prompt: "SAY IT" over the line near the top; clicks pass through; it fades when answered. WPF `Overlays/SpeakCueOverlay.cs:39`. Rows: `overlay-speak-cue`.
11. **[SAFETY] developers re-check this before release.** With the speak cue up press panic, then tray -> Stop everything, then close the app. Expect the cue gone every time. Rows: `overlay-speak-cue`.

### Chaster pop
12. Chaster linked: pop a priced bubble. Expect a big "+N:NN" at the bubble that lifts and fades in ~1.5 s, clicks pass through; the rail padlock ring pulses. WPF `Controls/ChasterBookedPop.cs:117`. Rows: `ctrl-chaster-booked-pop`, `ctrl-chaster-rail-chip`.
13. Two prices within half a second: one bigger number. Daily cap full: one lilac "day is full" tag. Motion Reduced or Off: no lift. Rows: `ctrl-chaster-booked-pop`.

### Chaos mode
14. Rabbit Hole -> FALL IN. Expect the hub to close, the HUD and countdown, then bubbles rising. WPF `Chaos/ChaosHubWindow.xaml.cs:1730`, `Services/Chaos/ChaosModeService.cs:297`. Rows: `chaos-hub-window`, `chaos-hud-window`, `chaos-overlay-window`, `chaos-bubble-host`.
15. **[SAFETY] developers re-check this before release.** Pop a few bubbles, press panic. Expect HUD, overlay and every bubble gone at once, no recap, and drops/gold unchanged in the hub. WPF `Services/Chaos/ChaosModeService.cs:288`, `:3161`. Rows: `chaos-hud-window`, `chaos-overlay-window`.
16. **[SAFETY] developers re-check this before release.** FALL IN again, press panic twice within 2 s. Expect the run to end and the app to stay open. Rows: `chaos-hud-window`.
17. **[SAFETY] developers re-check this before release.** Tray -> Stop everything mid-run: same as step 15. Rows: `chaos-hud-window`.
18. Let a run finish. Expect the recap; the hub then shows the recap's drops, +1 descent, more gold, and the XP bar moved. Rows: `chaos-hub-window`.
19. First visit to the hub: the intro card appears; "i understand. take me down" closes it; `?` shows it again; it does not come back on reopen. WPF `Chaos/ChaosHubWindow.Reveals.cs:145-153`. Rows: `chaos-intro-window`.
20. **[SAFETY] developers re-check this before release.** Intro card up: press panic with its button focused. Expect the card to close and panic to run. Rows: `chaos-intro-window`.
21. Toy button (with a toy pocket): round button bottom-right with a key badge; click or press its key: "20s" countdown, then "ready". It never steals focus. WPF `Chaos/ChaosToyButtonWindow.cs:42`. Rows: `chaos-toy-button`.
22. **[SAFETY] developers re-check this before release.** Set the toy key to the panic key: pressing it panics and does not fire the toy. After panic the toy key does nothing. Rows: `chaos-toy-button`.
23. Pocket Watch charm: a "WAVE 1/N" pill top-right; clock turns red under 10 s; "LAST WAVE" blinks. WPF `Chaos/ChaosWaveTimerOverlay.cs:23`. Rows: `chaos-wave-timer`.
24. Announcer on: "THE HOLE IS CLOSING..." pops near the top, lines never overlap. WPF `Chaos/ChaosAnnouncerOverlay.cs:65`. Rows: `chaos-announcer`.
25. Draft a mantra and a sin: tiles appear in a pill top-right, hover shows a card. WPF `Chaos/ChaosBoonBarOverlay.cs:34`. Rows: `chaos-boon-bar`.
26. Heat bar full: warm edge tint fades in; off when "colour flashes" is off. WPF `Chaos/ChaosFxWindow.cs:26`. Rows: `chaos-fx-window`.
27. Pop words ("SNAP"), effect banners, cursor halo, vibe trail, sparks, ripples, DVD logo, GIF rain, brain-drain wash, e-stim bolt: when each appears, clicks pass through and nothing takes focus. WPF `Chaos/ChaosPopText.cs:50`, `Chaos/ChaosEffectBannerOverlay.cs:57`, `Chaos/ChaosCursorGlowOverlay.cs:46`, `Chaos/ChaosVibeTrailOverlay.cs:58`, `Chaos/ChaosSkiaFxOverlay.cs:72`, `Chaos/ChaosFieldFxOverlay.cs:59`, `Chaos/ChaosDvdOverlay.cs:92`, `Chaos/ChaosGifCascadeOverlay.cs:51`, `Chaos/ChaosFlashOverlay.cs:44`, `Chaos/ChaosEStimOverlay.cs:40`. Rows: `chaos-pop-text`, `chaos-effect-banner`, `chaos-cursor-glow`, `chaos-vibe-trail`, `chaos-skia-fx`, `chaos-field-fx`, `chaos-dvd-overlay`, `chaos-dvd-host`, `chaos-gif-cascade`, `chaos-flash-overlay`, `chaos-estim-overlay`, `chaos-estim-glow`.
28. **[SAFETY] developers re-check this before release.** While any of step 27's effects or the wave pill, boon pill or announcer shows: panic, tray -> Stop everything, and the safe word each close it at once with no window left behind. Rows: as step 27, plus `chaos-wave-timer`, `chaos-boon-bar`, `chaos-announcer`, `chaos-fx-window`, `chaos-toy-button`.
29. Without a compositor (or on plain Wayland): those effects show nothing rather than a black or click-blocking sheet. Rows: as step 27.
30. Click a bubble: it pops and the click does not reach the window below; empty space between bubbles stays clickable. WPF `Chaos/ChaosBubbleHostOverlay.cs:137`. Rows: `chaos-bubble-host`.

### Other controls
31. Help "?" on a setting with a clip: a looping silent video with a caption; closing stops it; switching language re-translates the caption. WPF `Controls/InlineLoopVideo.cs:57`, `MainWindow/MainWindow.Presets.cs:130`. Rows: `ctrl-inline-loop-video`, `ctrl-help-popover`.
32. Back Room counter (test account): open the Flashes card. Expect "Checking...", a price and "Get it"; Cancel buys nothing; Yes buys, shows a toast and the row disappears; not enough points shows the Windows error text. WPF `Controls/V2GetItRow.xaml.cs:35`. Rows: `ctrl-v2-get-it-row`, `svc-prize-ownership`.
33. Companion tube: shows beside the panel; minimise hides it; maximise detaches it; restore re-attaches. WPF `MainWindow/MainWindow.xaml.cs:3695`. Rows: `avatar-tube-window`, `shell-companion`.
34. Season recap: Leaderboard -> recap button opens the card window. Rows: `ctrl-season-recap-card`, `ctrl-season-recap-window`.
35. Attention check during a session: the prompt appears and must be answered. WPF `App.xaml.cs:3229-3246`. Rows: `ctrl-attention-check`.
36. Launcher tiles: hover sheen and the comet around the edge. WPF `Windows/Launcher/LauncherWindow.Fx.cs:141`, `:381`. Rows: `ctrl-card-sheen-adorner`, `ctrl-perimeter-comet-adorner`.

### Rows (general check G)
- [ ] `feat-adaptive-side-art` - WPF `Features/AdaptiveSideArt.cs`
- [ ] `feat-app-info` - WPF `Features/AppInfoFeatureControl.xaml`
- [ ] `feat-bouncing-text` - WPF `Services/Subliminal/BouncingTextService.cs:372`
- [ ] `feat-bubble-count` - WPF `Services/BubbleCountService.cs:353`
- [ ] `feat-bubble-pop` - WPF `Services/BubbleService.cs:1089`
- [ ] `feat-feature-card` - WPF `MainWindow/MainWindow.Presets.cs:1380-1433`
- [ ] `feat-feature-popup` - WPF `Features/FeaturePopupWindow.xaml`
- [ ] `feat-flash` - WPF `Features/FlashFeatureControl.xaml`
- [ ] `feat-intensity-ramp` - WPF `Features/IntensityRampFeatureControl.xaml`
- [ ] `feat-lock-card` - WPF `Features/LockCardFeatureControl.xaml`
- [ ] `feat-mind-wipe` - WPF `Features/MindWipeFeatureControl.xaml`
- [ ] `feat-monitor-target-picker` - WPF `Views/Controls/AppSettings/MonitorsSettingsSection.xaml:35`
- [ ] `feat-pink-filter` - WPF `MainWindow/MainWindow.Presets.cs:1360`
- [ ] `feat-scheduler` - WPF `Features/SchedulerFeatureControl.xaml`
- [ ] `feat-spiral` - WPF `Features/SpiralFeatureControl.xaml`
- [ ] `feat-split-card` - WPF `Features/SplitFeatureCard.xaml`
- [ ] `feat-subliminal` - WPF `Features/SubliminalFeatureControl.xaml`
- [ ] `feat-system` - WPF `Features/SystemFeatureControl.xaml(.cs)` (gone from Windows; compare with the Windows app)
- [ ] `feat-video` - WPF `Services/Video/VideoService.cs:3372`
- [ ] `feat-visuals` - WPF `Features/VisualsFeatureControl.xaml`
- [ ] `ctrl-attention-check` - WPF `App.xaml.cs:3229-3246`
- [ ] `ctrl-emi-dock` - WPF `MainWindow/MainWindow.xaml.cs:4202`
- [ ] `ctrl-inbox-flyout` - WPF `Controls/InboxFlyout.xaml`
- [ ] `ctrl-season-recap-card` - WPF `Controls/SeasonRecapCard.xaml`
- [ ] `ctrl-season-recap-window` - WPF `Controls/SeasonRecapWindow.xaml`
- [ ] `ctrl-sparkle-wallet` - WPF `Controls/SparkleWallet.xaml`
- [ ] `ctrl-v2-get-it-row` - WPF `Controls/V2GetItRow.xaml`
- [ ] `ctrl-ambient-fx-canvas` - WPF `Controls/AmbientFxCanvas.cs`
- [ ] `ctrl-card-sheen-adorner` - WPF `Windows/Launcher/LauncherWindow.Fx.cs:381`
- [ ] `ctrl-chaster-account-badge` - WPF `Controls/ChasterAccountBadge.cs`
- [ ] `ctrl-chaster-booked-flash` - WPF `Controls/ChasterBookedFlash.cs`
- [ ] `ctrl-chaster-booked-pop` - WPF `Controls/ChasterBookedPop.cs`
- [ ] `ctrl-chaster-rail-chip` - WPF `Controls/ChasterRailChip.cs:645-665`
- [ ] `ctrl-chaster-receipt-view` - WPF `Controls/ChasterReceiptView.cs`
- [ ] `ctrl-chaster-trailer-view` - WPF `Controls/ChasterTrailerView.cs`
- [ ] `ctrl-descent-fuse-rail-chip` - WPF `Controls/DescentFuseRailChip.cs`
- [ ] `ctrl-descent-fuse-stage-visual` - WPF `Controls/DescentFuseStageVisual.cs`
- [ ] `ctrl-friends-drawer` - WPF `Controls/Friends/FriendsDrawer.cs`
- [ ] `ctrl-friends-rail-chip` - WPF `Controls/Friends/FriendsRailChip.cs`
- [ ] `ctrl-help-popover` - WPF `MainWindow/MainWindow.Presets.cs:130`
- [ ] `ctrl-inline-loop-video` - WPF `Controls/InlineLoopVideo.cs`
- [ ] `ctrl-lock-title` - WPF `Controls/LockTitle.cs`
- [ ] `ctrl-perimeter-comet-adorner` - WPF `Windows/Launcher/LauncherWindow.Fx.cs:141`
- [ ] `ctrl-premium-gate-fx` - WPF `Controls/PremiumGateFx.cs`
- [ ] `ctrl-row-sweep-adorner` - WPF `Controls/RowSweepAdorner.cs`
- [ ] `ctrl-spiral-embed-view` - WPF `Controls/SpiralEmbedView.cs`
- [ ] `ctrl-spiral-first-light-visual` - WPF `Controls/SpiralFirstLightVisual.cs`
- [ ] `ctrl-spiral-glyph` - WPF `Controls/SpiralGlyph.cs`
- [ ] `ctrl-spiral-rail-host` - WPF `Controls/SpiralRailHost.cs`
- [ ] `ctrl-takeover-orb` - WPF `Controls/TakeoverOrb.cs`
- [ ] `ctrl-tier-badge` - WPF `Controls/TierBadge.cs`
- [ ] `ctrl-tier-fx-border` - WPF `Controls/TierFxBorder.cs`
- [ ] `ctrl-vat-glass-canvas` - WPF `Controls/VatGlassCanvas.cs`
- [ ] `chaos-hub-window` - WPF `Chaos/ChaosHubWindow.xaml`
- [ ] `chaos-hud-window` - WPF `Chaos/ChaosHudWindow.xaml`
- [ ] `chaos-overlay-window` - WPF `Chaos/ChaosOverlayWindow.xaml`
- [ ] `chaos-slot-picker` - WPF `Chaos/ChaosSlotPickerWindow.xaml`
- [ ] `chaos-intro-window` - WPF `Chaos/ChaosHubWindow.Reveals.cs:145-153`
- [ ] `chaos-toy-button` - WPF `Chaos/ChaosToyButtonWindow.cs`
- [ ] `chaos-skia-fx` - WPF `Chaos/ChaosSkiaFxOverlay.cs`
- [ ] `chaos-pop-text` - WPF `Chaos/ChaosPopText.cs`
- [ ] `chaos-gif-cascade` - WPF `Chaos/ChaosGifCascadeOverlay.cs`
- [ ] `chaos-fx-window` - WPF `Chaos/ChaosFxWindow.cs`
- [ ] `chaos-flash-overlay` - WPF `Chaos/ChaosFlashOverlay.cs`
- [ ] `chaos-field-fx` - WPF `Chaos/ChaosFieldFxOverlay.cs`
- [ ] `chaos-effect-banner` - WPF `Chaos/ChaosEffectBannerOverlay.cs`
- [ ] `chaos-estim-overlay` - WPF `Chaos/ChaosEStimOverlay.cs`
- [ ] `chaos-estim-glow` - WPF `Chaos/ChaosEStimOverlay.cs`
- [ ] `chaos-dvd-overlay` - WPF `Chaos/ChaosDvdOverlay.cs`
- [ ] `chaos-dvd-host` - WPF `Chaos/ChaosDvdHostOverlay.cs`
- [ ] `chaos-cursor-glow` - WPF `Chaos/ChaosCursorGlowOverlay.cs`
- [ ] `chaos-boon-bar` - WPF `Chaos/ChaosBoonBarOverlay.cs`
- [ ] `chaos-bubble-host` - WPF `Chaos/ChaosBubbleHostOverlay.cs`
- [ ] `chaos-announcer` - WPF `Chaos/ChaosAnnouncerOverlay.cs`
- [ ] `chaos-wave-timer` - WPF `Chaos/ChaosWaveTimerOverlay.cs`
- [ ] `chaos-vibe-trail` - WPF `Chaos/ChaosVibeTrailOverlay.cs`
- [ ] `chaos-unlock-card` - WPF `Chaos/ChaosUnlockCardOverlay.cs`
- [ ] `chaos-webview-host` - WPF `Chaos/ChaosWebViewHost.cs`
- [ ] `avatar-tube-window` - WPF `MainWindow/MainWindow.xaml.cs:3695`
- [ ] `lab-gaze-minigame` - WPF `Lab/GazeMinigame/GazeMinigameWindow.xaml`
- [ ] `overlay-speak-cue` - WPF `Overlays/SpeakCueOverlay.cs`
- [ ] `overlay-takeover-announcer` - WPF `Overlays/TakeoverAnnouncerOverlay.cs`
- [ ] `behavior-hover-pop` - WPF `Behaviors/HoverPopBehavior.cs`
- [ ] `theme-brushes` - WPF `Resources/Theme/Brushes.xaml`
- [ ] `theme-colors` - WPF `Resources/Theme/Colors.xaml`
- [ ] `theme-controls` - WPF `Resources/Theme/Controls.xaml`
- [ ] `theme-converters` - WPF `Resources/Theme/Converters.xaml`
- [ ] `theme-fonts` - WPF `Helpers/EmojiTextBlock.cs:11-14`
- [ ] `theme-inputs` - WPF `Resources/Theme/Inputs.xaml`
- [ ] `theme-main-window` - WPF `Resources/Theme/MainWindow.xaml`
- [ ] `theme-season-recap-card` - WPF `Resources/Theme/SeasonRecapCard.xaml`
- [ ] `feat-companion-ai-cloud-chat` - WPF `CCP.Core/Services/AiService.cs`
- [ ] `feat-companion-ai-local-providers` - WPF `Services/AIService/{AiServiceStrategy,LocalAiService,OpenAiCompatibleService,TransportModeration,AiResponseParser,BrainAdapter}.cs`
- [ ] `feat-companion-brain` - WPF `AvatarTube/AvatarTubeWindow.ChatInput.cs:772`
- [ ] `feat-fyp-host` - WPF `Services/Video/VideoService.cs:1988`

## 6. Main sync 2 additions (`main-sync-2 additions`)

1. Settings -> "?" help loops: each plays its little animation and stops when closed. WPF `Views/Tabs/SettingsTabView.xaml:452`. Rows: `feat-help-loops`.
2. Leash: accept a friend's leash; the gate card appears on the tabs it locks. WPF `App.xaml.cs:2991-3022`. Rows: `feat-leash`.
3. Finish a session: the summary opens without stealing focus, only one at a time. Rows: `feat-session-summary-passive`.
4. Chaster ladder and scrap: open the Chaster tab ladder. Rows: `views-chaster-ladder-scrap`.
5. Mod manager: the Download button on a content pack shows progress and finishes. Rows: `release-content-mod-manager-rows`, `release-content-avalonia-service`, `release-content-pack-script`.

### Rows (general check G)
- [ ] `feat-help-loops` - WPF `Views/Tabs/SettingsTabView.xaml:452`
- [ ] `feat-leash` - WPF `App.xaml.cs:2991-3022`
- [ ] `views-companion-picker-card` - WPF `Views/Controls/Companion/CompanionPickerCard.xaml`
- [ ] `views-companion-v2-conversation` - WPF `Views/Controls/Companion/V2/ConversationPage.xaml`
- [ ] `views-companion-ai-page` - WPF `Views/Controls/Companion/Pages/AiPage.xaml`
- [ ] `views-awareness-companion` - WPF `Views/Tabs/AwarenessTabView.Companion.cs`
- [ ] `views-chaster-ladder-scrap` - WPF `Views/Tabs/ChasterTabView.Ladder.cs`
- [ ] `feat-first-show` - WPF `MainWindow/MainWindow.xaml.cs:546-553`
- [ ] `feat-dashboard-logo` - WPF `Controls/AnimatedLogoImage.cs`
- [ ] `feat-friends-open-tables` - WPF `Controls/Friends/FriendsDrawer.Tables.cs`
- [ ] `win-emi-desk-extras` - WPF `Windows/EmiDesk/EmiDeskWindow.Presentation.cs`
- [ ] `feat-session-summary-passive` - WPF `CCP.Core/Services/Session/SessionSummaryPresentation.cs`
- [ ] `companion-ccp-default-voice` - WPF `builtin-ccp-default barks`
- [ ] `release-content-pack-script` - WPF `Scripts/build-content-packs.ps1`
- [ ] `release-content-avalonia-service` - WPF `App.xaml.cs:346-356`
- [ ] `release-content-mod-manager-rows` - WPF `Dialogs/ModManagerDialog.xaml.cs:56-316`
- [ ] `blocked-migration-input` - WPF `Tests (migration)`

## 7. Main sync 3 additions (`main-sync-3 additions`)

### Leash hold-to-cut
1. **[SAFETY] developers re-check this before release.** Leashed, tap the panic key once. Expect a normal panic and no ring. WPF `MainWindow/MainWindow.Leash.cs:307`. Rows: `feat-leash-receipts-hold`.
2. **[SAFETY] developers re-check this before release.** Hold it ~3 s and let go. Expect a small countdown ring (5, 4, 3) bottom-centre that vanishes on release; still leashed. WPF `MainWindow/MainWindow.Leash.cs:323-350`, `Controls/Leash/LeashHoldRing.cs:20`. Rows: `feat-leash-receipts-hold`.
3. **[SAFETY] developers re-check this before release.** Hold 5 s. Expect "Cut the leash?" centred; held keys do not answer it. "No" keeps the leash; "Yes, cut it" shows "Cut. You are off the leash." WPF `MainWindow/MainWindow.Leash.cs:358`. Rows: `feat-leash-receipts-hold`, `feat-leash`.
4. **[SAFETY] developers re-check this before release.** Panic key turned off in Settings, leashed: holding it 5 s still asks. Not leashed: holding it is one panic only, never an exit. WPF `MainWindow/MainWindow.Leash.cs:316`, `:347`. Rows: `feat-leash-receipts-hold`.
5. The ring never takes clicks or focus. Rows: `feat-leash-receipts-hold`.

### Companion voice lines
6. Default mod, companion on: open a tab with a voice line (e.g. Leaderboard). Expect a speech bubble and the voice, at most one per minute. WPF `MainWindow/MainWindow.xaml.cs:1755`. Rows: `feat-companion-barks`, `companion-ccp-default-voice`.
7. Master volume 0: text only. Companion muted: text, no voice. Rows: `feat-companion-barks`.
8. **[SAFETY] developers re-check this before release.** Press panic while she speaks. Expect the voice cut at once. Rows: `feat-companion-barks`.

### Other
9. Friends drawer: feed, trail, blocked list, presence setting. Rows: `feat-friends-feed`, `feat-friends-blocked-presence`, `feat-friends-landing`.
10. Flash card: exit and stay options behave as on Windows. WPF `Features/FlashFeatureControl.xaml:166-193`. Rows: `feat-flash-exit-stay`.
11. Haptics -> Intiface address: type a host without `ws://`; it connects the same as Windows. Rows: `feat-intiface-url`.
12. Piece by Piece: launch from the launcher, play, close. Rows: `feat-pbp-host`.
13. Chaster: Circe's mood meter and lines after a booking. Rows: `views-chaster-circe-mood`.

### Rows (general check G)
- [ ] `feat-stakes` - WPF `Services/Stakes/**`
- [ ] `views-chaster-circe-mood` - WPF `Controls/CirceSays.cs`
- [ ] `views-chaster-help-price-edit` - WPF `Views/Tabs/ChasterTabView.Help.cs`
- [ ] `feat-friends-feed` - WPF `Controls/Friends/FriendsDrawer.Feed.cs`
- [ ] `feat-friends-blocked-presence` - WPF `Controls/Friends/FriendsBlockList.cs`
- [ ] `feat-friends-landing` - WPF `Services/Friends/FriendsLanding.cs`
- [ ] `feat-companion-barks` - WPF `MainWindow/MainWindow.xaml.cs:1755`
- [ ] `feat-leash-receipts-hold` - WPF `CCP.Core/Services/Leash/LeashReceipts.cs`
- [ ] `feat-flash-exit-stay` - WPF `Features/FlashFeatureControl.xaml:166-193`
- [ ] `feat-brain-drain-keep-clear` - WPF `CCP.Core/Services/Notifications/BrainDrainKeepClear.cs`
- [ ] `win-launcher-edge` - WPF `Windows/Launcher/LauncherWindow.Edge.cs`
- [ ] `feat-intiface-url` - WPF `CCP.Core/Services/Haptics/ButtplugUrl.cs`
- [ ] `feat-pbp-host` - WPF `Services/PieceByPiece/PieceByPieceHostService.cs`
- [ ] `feat-pbp-media-friends` - WPF `Services/PieceByPiece/PieceByPieceHostService.Media.cs`
- [ ] `feat-mod-personality-picks` - WPF `CCP.Core/Services/Companion/ModPersonalityPicks.cs`
- [ ] `core-sync-server-clock` - WPF `Services/Settings/ProfileSyncService.cs`
- [ ] `core-sync-failure-backoff` - WPF `Services/Settings/ProfileSyncService.cs`
- [ ] `core-dead-refresh-tokens` - WPF `Services/Account/DiscordService.cs`

## 8. Main syncs 4, 5 and 6

These sections of the ledger point at per-commit lists ([`avalonia-main-sync-20261005.md`](avalonia-main-sync-20261005.md),
[`avalonia-main-sync-20261008.md`](avalonia-main-sync-20261008.md), [`avalonia-main-sync-20261009.md`](avalonia-main-sync-20261009.md)),
one row per Windows commit, not per screen. Test them through the screen each commit touched, using the
area sections above; report against the commit row only when no screen row fits.

## Still to write

- Screen-specific steps for rows that only have the general check (G) above; add them as testers find gaps.
- Per-commit rows of main syncs 4-6 that touch no screen in sections 1-7.

## Companion BYO key transport

1. Open Companion -> Engine Room -> Custom. Enter an endpoint, model and API key, then Test Connection and chat through the companion tube. Confirm both use the configured key and reopening the drawer never reveals it. Clear the key and confirm neither action sends a request. Settings must contain no typed key. WPF `MainWindow/MainWindow.CompanionRoom.cs:312` + `CCP.Core/Services/AIService/OpenAiCompatibleService.cs:198`. Rows: `feat-companion-ai-cloud-chat`.
2. On Windows, open a copied WPF profile with a legacy DPAPI-protected BYO key: Test Connection and chat still work. On Linux that Windows-encrypted key must fail closed; re-enter it to use the OS store (memory only when unavailable). WPF `Services/Auth/SecureStringHelper.cs:37`. Rows: `feat-companion-ai-cloud-chat`.
