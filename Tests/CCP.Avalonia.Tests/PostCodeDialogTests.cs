using System;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Startup.Conversion;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PostCodeDialogTests
{
    [Fact]
    public Task EmptyPressKeepsTheBoxEditableAndShowsTheReason() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var box = new PostCodeDialog();
        try
        {
            box.Show();
            var children = box.GetLogicalDescendants().ToArray();
            var field = Assert.Single(children.OfType<TextBox>());
            Assert.Equal(64, field.MaxLength);
            Assert.Equal(WindowDecorations.None, box.WindowDecorations);
            var card = Assert.IsType<Border>(box.Content);
            Assert.Equal(new CornerRadius(14), card.CornerRadius);
            Assert.Equal(new Thickness(1.5), card.BorderThickness);
            Assert.Equal(Color.Parse("#FF69B4"), ((ISolidColorBrush)card.BorderBrush!).Color);
            Assert.Equal(Color.Parse("#252542"), ((ISolidColorBrush)field.Background!).Color);
            Assert.Equal(Brushes.White, field.CaretBrush);
            Assert.Equal(Color.Parse("#FF69B4"), ((ISolidColorBrush)field.BorderBrush!).Color);
            Assert.True(PostCodeDialog.CanDrag(card)); Assert.False(PostCodeDialog.CanDrag(field));
            var redeem = children.OfType<Button>().Single(b => Equals(b.Content, Loc.Get("conv_postcode_redeem")));
            Assert.False(PostCodeDialog.CanDrag(redeem));
            Assert.NotNull(redeem.Theme); Assert.Equal(Color.Parse("#FF69B4"), ((ISolidColorBrush)redeem.Background!).Color);
            redeem.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(field.IsEnabled);
            Assert.True(redeem.IsEnabled);
            Assert.Contains(children.OfType<TextBlock>(), t => t.IsVisible && t.Text == Loc.Get("conv_postcode_empty") && ((ISolidColorBrush)t.Foreground!).Color == Color.Parse("#FFB347"));
        }
        finally { box.Close(); }
        return Task.CompletedTask;
    });
    private static void Setup()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NoticeUsesNormalAndQuietRoutes(bool quiet) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); StartupLadder.ResetForTests();
        var owner = new Window(); owner.Show();
        try
        {
            if (quiet) StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            PostCodeDialog.Offer(owner, "Feature body");
            if (quiet) { Assert.Null(PostCodeDialog.Notice); StartupLadder.Inbox.Open(Assert.Single(StartupLadder.Inbox.Items)); }
            else Assert.Empty(StartupLadder.Inbox.Items);
            var notice = Assert.IsAssignableFrom<PostCodeDialog>(PostCodeDialog.Notice);
            Assert.IsAssignableFrom<ConditioningControlPanel.Services.Startup.IPassiveStartupSurface>(notice);
            var controls = notice.GetLogicalDescendants().ToArray();
            Assert.False(Assert.Single(controls.OfType<TextBox>()).IsVisible);
            Assert.Contains(controls.OfType<TextBlock>(), t => t.Text == "Feature body");
            controls.OfType<Button>().Single(b => Equals(b.Content, Loc.Get("conv_postcode_enter"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(PostCodeDialog.Notice); Assert.NotNull(PostCodeDialog.Open);
        }
        finally { PostCodeDialog.Open?.Close(); PostCodeDialog.Notice?.Close(); owner.Close(); StartupLadder.ResetForTests(); }
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PendingReplyAppliesOnlyWhileOpen(bool close) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup(); var box = new PostCodeDialog(); box.Show();
        var grants = CoreSettings.Current.PostGrants; int changed = 0;
        ConditioningControlPanel.Avalonia.App.PostCodeInputsChanged = () => changed++;
        var reply = new TaskCompletionSource<PostCodeRedeem>(); box.Send = _ => reply.Task;
        try
        {
            box.GetLogicalDescendants().OfType<TextBox>().Single().Text = "CODE";
            var work = box.Redeem(); if (close) box.Close();
            reply.SetResult(new(true, "dtrh", DateTime.UtcNow.AddDays(1), null)); await work;
            Assert.Equal(close ? 0 : 1, changed);
            if (close) Assert.Same(grants, CoreSettings.Current.PostGrants);
            else Assert.Contains(box.GetLogicalDescendants().OfType<TextBlock>(), t => t.IsVisible && ((ISolidColorBrush)t.Foreground!).Color == Color.Parse("#7FD8A6"));
        }
        finally { box.Close(); CoreSettings.Current.PostGrants = grants; }
    });
}
