
1. WPF `ConditioningControlPanel/Views/Tabs/SpiralTabView.xaml.cs:758-858` Rows: `ctrl-spiral-first-light-visual`
   Run `SpiralRoomShellTests.FirstLightHoldsForTheBlockThenTimesOutToTheWaitingRoom` headlessly.
   The reveal holds at hand-off, then times out after 12 seconds while minimised without another visual beat.
   Navigation cancels the reveal. WPF retired its production ceremony entry; use the existing test seam.

- Render-only: `ctrl-spiral-first-light-visual`
  `--render-all` includes `SpiralFirstLightVisual.png`, full motion at 2.5 seconds.
