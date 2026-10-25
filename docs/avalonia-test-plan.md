# Avalonia manual test plan

1. WPF `MainWindow/MainWindow.HeroFx.cs` START: with Full motion and an active window, observe the idle tinted charge and exhale. Press Start: dip, white flash and two staggered rings, then red STOP and a subtle heartbeat. Stop returns to charge. Minimize/deactivate parks ambient movement; motion Off suppresses ignition and loops; a remote controller retains its green disabled button. Rows: `shell-start-stop`
2. WPF `MainWindow/MainWindow.StartStop.cs` RampTimer_Tick/StopRampTimer: enable a master-audio ramp, start, and keep Settings Audio open. The master slider and percentage follow the ramp; the dashboard copy agrees. Stop restores the starting volume on both surfaces. Rows: `shell-start-stop`
