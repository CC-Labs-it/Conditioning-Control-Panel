# Avalonia tester steps

1. WPF `MainWindow.ChromeFx.cs:AnimateTabIn`: in Full motion navigate forward and backward between Quests, Achievements and Library. The page slides 12 px in rail order and eligible cards rise with a short stagger. Navigate again during entrance; the new page remains clickable. Reduced fades without slide/stagger; Off swaps instantly. Rows: `shell-tab-navigation`
2. WPF `MainWindow.ChromeFx.cs:FadeOutgoingTab`: leave Achievements for Quests, then immediately return. The leaving page fades for 100 ms without accepting input; the returned page accepts input. Repeat rapidly and close during a fade; no snapshot remains. Browser-airspace pages cut away. Rows: `shell-tab-navigation`
3. WPF `MainWindow.TabNavigation.cs:ApplyNavRailDoorState`: on a real desktop click a door with the rail shut, then hover the rail; only the chosen door expands. This release check remains outstanding in the cloud environment. Rows: `shell-tab-navigation`
