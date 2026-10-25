# Avalonia tester steps

1. Own Perfect Bimbo Week; complete a daily quest taking the streak to 7, 14,
   21 or 30. Check the level-scaled bonus and notification. Complete another daily
   seat and relaunch: no second bonus. Rebuild a broken streak to earn it again.
   WPF `ConditioningControlPanel/Services/Progression/SkillTreeService.cs` Rows: `shell-quests-tab`
2. With a session quest active, stop a session early: no progress. Finish a
   session naturally: exactly one credit; the board updates without leaving the tab.
   WPF `ConditioningControlPanel/Services/Progression/AchievementService.cs` Rows: `shell-quests-tab`
3. Reroll a daily seat and the weekly quest. Confirm the companion receives
   `reroll_daily` and `reroll_weekly` feedback even for a refused attempt.
   WPF `ConditioningControlPanel/MainWindow/MainWindow.QuestsTab.cs` Rows: `shell-quests-tab`
4. Earn the perfect-week notification, then panic. The notification closes.
   WPF `ConditioningControlPanel/Services/Progression/SkillTreeService.cs` Rows: `shell-quests-tab`
