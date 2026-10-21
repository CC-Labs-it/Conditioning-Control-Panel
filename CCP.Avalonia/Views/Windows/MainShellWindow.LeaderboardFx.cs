// WPF MainWindow.LeaderboardFx.cs is implemented in Views/Tabs/LeaderboardTabView.Fx.cs.
// The Avalonia tab owns its fetched board and generated containers, so its rebuild is the real
// caller: three-card entrance, gold podium breath, and refresh-only washes for moved roster rows.
// The snapshot engine is shared with WPF in CCP.Core/Services/Progression/LeaderboardRankSnapshotService.cs.
