using System;
using System.Linq;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
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
            var link = AssertLayout(box, false); box.UpdateLayout();
            var at = link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), box)!.Value;
            box.MouseDown(at, MouseButton.Left); box.MouseUp(at, MouseButton.Left); Assert.False(box.IsVisible);
        }
        finally { box.Close(); }
        return Task.CompletedTask;
    });
    private static TextBlock AssertLayout(PostCodeDialog box, bool notice)
    {
        var panel = Assert.IsType<StackPanel>(Assert.IsType<Border>(box.Content).Child); Assert.Equal(0, panel.Spacing);
        var texts = panel.Children.OfType<TextBlock>().ToArray(); Assert.Equal(13, texts[1].FontSize);
        Assert.Equal(new Thickness(0, notice ? 8 : 6, 0, 0), texts[1].Margin);
        Assert.Equal(new Thickness(0, 14, 0, 0), panel.Children.OfType<TextBox>().Single().Margin);
        Assert.Equal(new Thickness(0, notice ? 16 : 12, 0, 0), panel.Children.OfType<Button>().Single().Margin);
        Assert.Equal(new Thickness(0, 10, 0, 0), texts[2].Margin);
        var close = texts[^1]; Assert.Equal(Loc.Get("vaultgate_close"), close.Text); Assert.Equal(TextDecorations.Underline, close.TextDecorations);
        Assert.Equal(new Thickness(0, 10, 0, 0), close.Margin); Assert.False(PostCodeDialog.CanDrag(close)); return close;
    }
    private static void Setup()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task NoticeUsesNormalAndQuietRoutes(bool quiet, bool hidden) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); StartupLadder.ResetForTests();
        var owner = new Window(); owner.Show(); if (hidden) owner.Hide();
        try
        {
            if (quiet) StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            PostCodeDialog.Offer(owner, "Feature body");
            if (quiet) { Assert.Null(PostCodeDialog.Notice); StartupLadder.Inbox.Open(Assert.Single(StartupLadder.Inbox.Items)); }
            else Assert.Empty(StartupLadder.Inbox.Items);
            var notice = Assert.IsAssignableFrom<PostCodeDialog>(PostCodeDialog.Notice);
            Assert.IsAssignableFrom<ConditioningControlPanel.Services.Startup.IPassiveStartupSurface>(notice);
            Assert.True(notice.IsVisible); Assert.Same(hidden ? null : owner, notice.Owner); AssertLayout(notice, true);
            var controls = notice.GetLogicalDescendants().ToArray();
            Assert.False(Assert.Single(controls.OfType<TextBox>()).IsVisible);
            Assert.Contains(controls.OfType<TextBlock>(), t => t.Text == "Feature body");
            controls.OfType<Button>().Single(b => Equals(b.Content, Loc.Get("conv_postcode_enter"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(PostCodeDialog.Notice); Assert.NotNull(PostCodeDialog.Open); Assert.Same(hidden ? null : owner, PostCodeDialog.Open.Owner);
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
