# Avalonia navigation tester steps

1. WPF `MainWindow.ChromeFx.cs:204-459`: set Full motion; navigate Quests → Achievements → Quests. Check a 12 px slide follows rail order, cards rise in capped 40 ms slots, and the leaving page fades for 100 ms without intercepting clicks. Navigate again mid-entrance and confirm all previous transforms settle. In pointer automation call `SettleTabEntrance()` before targeting the page. Rows: `shell-tab-navigation`.
2. WPF `MainWindow.ChromeFx.cs:224-253`: Reduced keeps only crossfade; Off swaps instantly; Dashboard/native-airspace pages never slide. Close during an entrance and confirm no animation remains. Rows: `shell-tab-navigation`.
