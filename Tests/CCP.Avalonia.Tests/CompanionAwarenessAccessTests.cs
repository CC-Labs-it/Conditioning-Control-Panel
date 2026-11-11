using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionAwarenessAccessTests
{
    [Theory]
    [InlineData("0")]
    [InlineData(null)]
    public void AwarenessRoundTripKeepsTheRightOwner(string? value) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = Environment.GetEnvironmentVariable("CCP_COMPANION_V2");
        var oldProvider = CoreSettings.ServiceProvider;
        Environment.SetEnvironmentVariable("CCP_COMPANION_V2", value);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.AwarenessModeEnabled = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            var room = shell.GetLogicalDescendants().OfType<CompanionRoomView>().Single();
            var accordion = room.FindControl<WorkshopAccordion>("WorkshopZone")!;
            var workshop = (WorkshopRuntimeVm)accordion.DataContext!;
            var privacy = room.FindControl<AwarenessPrivacyView>("AwarenessZone")!;
            var awareness = shell.GetLogicalDescendants().OfType<AwarenessTabView>().Single();
            var privacyHost = awareness.FindControl<ContentControl>("CompanionPrivacyHost")!;
            var tuningHost = awareness.FindControl<ContentControl>("CompanionTuningHost")!;
#if DEBUG
            bool legacy = value == "0";
#else
            bool legacy = false;
#endif
            shell.ShowTab("awareness");
            Dispatcher.UIThread.RunJobs();
            if (legacy)
            {
                Assert.Null(privacyHost.Content);
                Assert.Null(tuningHost.Content);
                Assert.True(room.IsVisualAncestorOf(privacy));
            }
            else
            {
                Assert.Same(privacy, privacyHost.Content);
                Assert.Same(workshop.Parts.Awareness, tuningHost.Content);
            }
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            room.RevealWorkshop(CompanionRoomAnchors.WorkshopAwarenessCell);
            Dispatcher.UIThread.RunJobs();
            if (legacy)
            {
                Assert.Equal("companion", shell.CurrentTab);
                Assert.True(workshop.IsExpanded);
                Assert.True(room.IsVisualAncestorOf(privacy));
                Assert.True(room.IsVisualAncestorOf(workshop.Parts.Awareness));
                Assert.Null(privacyHost.Content);
                Assert.Null(tuningHost.Content);
            }
            else
            {
                Assert.Equal("awareness", shell.CurrentTab);
                Assert.True(awareness.FindControl<Expander>("CompanionTuningFold")!.IsExpanded);
                Assert.Same(privacy, privacyHost.Content);
                Assert.Same(workshop.Parts.Awareness, tuningHost.Content);
            }
            shell.ShowTab("companion");
            room.FocusAwareness();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(legacy ? "companion" : "awareness", shell.CurrentTab);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = oldProvider;
            Environment.SetEnvironmentVariable("CCP_COMPANION_V2", previous);
        }
    });
}
