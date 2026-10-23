using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.SessionIO.cs:1264-1443 + BlinkTrainer.cs:310-369: the shell carries the
/// in-window camera shortcut from startup, it toggles the tracker, and the Workshop pill rebinds it,
/// saves it and repaints itself.</summary>
public sealed class CameraShortcutTests
{
    private static void PumpUntil(Func<bool> done)
    {
        for (var sw = Stopwatch.StartNew(); !done() && sw.ElapsedMilliseconds < 8000;)
            Dispatcher.UIThread.RunJobs();
    }

    private static KeyBinding CameraBinding(Window w) =>
        w.KeyBindings.Single(b => ReferenceEquals(b.Command, CameraShortcut.ToggleCommand));

    [Fact]
    public void ShellShortcutTogglesTheTrackerAndTheWorkshopPillRebindsIt() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var factory = WebcamTracker.SourceFactory;
        var tracker = WebcamTracker.Instance;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown, s.AvatarEnabled) = (true, true, 1, false);
        s.MotionLevel = MotionLevel.Off;
        (s.WebcamConsentGiven, s.WebcamConsentVersion) = (true, WebcamConsent.ConsentVersion);
        WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            shell.GetLogicalDescendants().OfType<WorkshopAccordion>().Single().ExpandAndReveal("BEHAVIOR");
            Dispatcher.UIThread.RunJobs();
            var cell = shell.GetLogicalDescendants().OfType<WorkshopBehaviorCell>().Single();
            var label = cell.FindControl<TextBlock>("TxtCameraShortcutLabel")!;

            // Startup (WPF MainWindow.xaml.cs:239): the default combo is bound and labelled.
            Assert.Equal("Ctrl+Alt+K", label.Text);
            Assert.Equal(new KeyGesture(Key.K, KeyModifiers.Control | KeyModifiers.Alt), CameraBinding(shell).Gesture);

            // The key toggles the tracker on, then off (WPF ToggleWebcamTrackingAsync).
            shell.KeyPress(Key.K, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.K, "k");
            PumpUntil(() => tracker.IsRunning);
            Assert.True(tracker.IsRunning, tracker.LastError);
            shell.KeyPress(Key.K, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.K, "k");
            PumpUntil(() => !tracker.IsRunning);
            Assert.False(tracker.IsRunning);

            // The pill opens the capture dialog; Ctrl+Shift+J is saved, labelled and rebound.
            cell.FindControl<Button>("BtnCameraShortcut")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var dlg = shell.OwnedWindows.OfType<ChatShortcutCaptureDialog>().Single();
            dlg.KeyPress(Key.J, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.J, "j");
            PumpUntil(() => label.Text == "Ctrl+Shift+J");
            Assert.Equal("Ctrl+Shift+J", label.Text);
            Assert.Equal(("J", "Control,Shift"), (s.CompanionPrompt.CameraShortcutKey, s.CompanionPrompt.CameraShortcutModifiers));
            Assert.Equal(new KeyGesture(Key.J, KeyModifiers.Control | KeyModifiers.Shift), CameraBinding(shell).Gesture);

            shell.KeyPress(Key.J, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.J, "j");
            PumpUntil(() => tracker.IsRunning);
            Assert.True(tracker.IsRunning, tracker.LastError);
        }
        finally
        {
            tracker.Stop();
            WebcamTracker.SourceFactory = factory;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = old;
        }
    });
}
