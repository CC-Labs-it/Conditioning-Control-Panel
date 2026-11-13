using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LegacyCompanionRoomAccessTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public void CompanionSwitchSelectsTheVisiblePage(string? value) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = Environment.GetEnvironmentVariable("CCP_COMPANION_V2");
        try
        {
            Environment.SetEnvironmentVariable("CCP_COMPANION_V2", value);
            var tab = new CompanionTabView();
            var window = new Window { Content = tab, Width = 1240, Height = 900 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var room = tab.FindControl<CompanionRoomView>("Room")!;
#if DEBUG
                bool legacy = value == "0";
#else
                bool legacy = false;
#endif
                Assert.Equal(legacy, room.IsEffectivelyVisible);
                var page = tab.FindControl<ConversationPage>("Conversation")!;
                Assert.Equal(!legacy, page.IsEffectivelyVisible);
                if (legacy)
                {
                    var targetName = TutorialHead.AllSteps().Where(x => x.Tour == TutorialType.Companion)
                        .Select(x => x.Step.TargetElementName).First(x => x == "ChatZone");
                    var find = typeof(TutorialOverlay).GetMethod("FindElementByName", BindingFlags.Static | BindingFlags.NonPublic)!;
                    var chat = Assert.IsType<ChatThresholdView>(find.Invoke(null, new object?[] { window, targetName }));
                    Assert.True(chat.IsEffectivelyVisible);
                    Assert.NotNull(chat.ViewModel);
                }
            }
            finally { window.Close(); }
        }
        finally { Environment.SetEnvironmentVariable("CCP_COMPANION_V2", previous); }
    });
}
