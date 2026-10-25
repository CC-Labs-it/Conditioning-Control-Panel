using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Companion pills open WPF page hosts containing the original live room controls.</summary>
public sealed class CompanionZonePillsTests
{
    [Fact]
    public async Task CompanionPillsLandOnTheirZone()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            MainShellWindow? w = null;
            try
            {
                w = new MainShellWindow { Width = 1400, Height = 800 };
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;
                var room = w.Named<CompanionTabView>("CompanionTab")!.FindControl<CompanionRoomView>("Room")!;
                var scroll = room.FindControl<ScrollViewer>("PageScroll")!;

                w.ShowTab("companion");
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("personality", strip.PillKeys);
                Assert.Contains("permissions", strip.PillKeys);
                Assert.Contains("companionlinks", strip.PillKeys);
                Assert.Equal(0, scroll.Offset.Y);

                var permissions = room.FindControl<AiPermissionsGrid>("PermissionsZone")!;
                var presets = room.FindControl<MakeHerYoursView>("PersonalityZone")!;
                var library = room.WorkshopParts!.Library;
                var community = room.WorkshopParts.Community;
                strip.PillFor("permissions")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("permissions", w.CurrentTab);
                Assert.Equal("permissions", strip.ActivePillKey);
                Assert.False(w.Named<CompanionTabView>("CompanionTab")!.IsVisible);
                var permissionsPage = w.Named<PermissionsPage>("CompanionPermissionsTab")!;
                Assert.True(permissionsPage.IsVisible);
                Assert.Same(permissions, permissionsPage.PrimaryHost.Content);

                strip.PillFor("companionlinks")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Same(permissions, room.FindControl<AiPermissionsGrid>("PermissionsZone"));
                Assert.Equal("companionlinks", w.CurrentTab);
                Assert.Same(library, w.Named<LinksPage>("CompanionLinksTab")!.PrimaryHost.Content);

                strip.PillFor("personality")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("personality", w.CurrentTab);
                var personality = w.Named<PersonalityPage>("CompanionPersonalityTab")!;
                Assert.True(personality.IsVisible);
                Assert.Same(presets, personality.PrimaryHost.Content);
                Assert.Contains(community, personality.GetVisualDescendants());
                Assert.NotNull(personality.FindControl<Button>("BtnOpenPromptEditor"));
                Assert.Single(personality.GetVisualDescendants().OfType<CompanionPickerCard>());
                w.ShowTab("companion");
                Dispatcher.UIThread.RunJobs();
                Assert.Same(presets, room.FindControl<MakeHerYoursView>("PersonalityZone"));
                Assert.Contains(community, room.GetVisualDescendants());
                Assert.Contains(library, room.GetVisualDescendants());

            }
            finally
            {
                w?.Close();
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });
    }
}
