using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public class FriendsDrawerRequestLayoutTests
{
    [Theory]
    [InlineData("de", 5)]
    [InlineData("en", 9)]
    public Task IncomingAgeKeepsRoomForTranslatedActions(string language, double padding) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.SetLanguage(language);
            var drawer = new FriendsDrawer(null);
            var request = new FriendRequest("request", "Ann", null, null, DateTimeOffset.UtcNow.AddMinutes(-59));
            var row = (Control)typeof(FriendsDrawer).GetMethod("RequestRow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drawer, new object[] { request, true })!;
            var controls = row.GetLogicalDescendants().ToArray();
            var accept = controls.OfType<Button>().Single(b => (string?)b.Tag == "friends-accept");
            var decline = controls.OfType<Button>().Single(b => (string?)b.Tag == "friends-decline");
            var sub = controls.OfType<TextBlock>().Single(t => (string?)t.Tag == "friends-request-sub");
            Assert.StartsWith(Loc.GetF("friends_notice_minutes", 59), sub.Text);
            Assert.Equal(new Thickness(padding, 3, padding, 3), accept.Padding);
            Assert.Equal(accept.Padding, decline.Padding);
        }
        finally { LocalizationManager.Instance.SetLanguage(previous); }
        return Task.CompletedTask;
    });
}
