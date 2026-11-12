using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class TutorialMainWindowDimTests
{
    private sealed class Store : TutorialService.ITourCompletionStore
    {
        public bool Has(string name) => false;
        public void Latch(string name) { }
    }

    private static Task WithWindows(Action<Window, Window, Button, TutorialOverlay> check, string? targetName = null) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previousStore = TutorialService.CompletionStore;
        TutorialService.CompletionStore = new Store();
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        var button = new Button { Content = "Main", Width = 100, Height = 40 };
        var main = new Window { Width = 800, Height = 600, Content = button };
        var target = new Button { Name = targetName, Content = "Dialog", Width = targetName == null ? 100 : 160, Height = 40 };
        var dialog = new Window { Width = 400, Height = 300 };
        if (targetName != null)
        {
            dialog.Width = 900;
            dialog.Height = 650;
            Canvas.SetLeft(target, 40);
            Canvas.SetTop(target, 60);
            dialog.Content = new Canvas { Children = { target } };
        }
        else dialog.Content = target;
        TutorialOverlay? overlay = null;
        TutorialStep? step = null;
        string? previousWindow = null;
        try
        {
            TutorialHead.Service.Start(TutorialType.DeeperEditorInteractiveHTPart2);
            while (TutorialHead.Service.CurrentStep!.Id != "iht_play") TutorialHead.Service.Next();
            step = TutorialHead.Service.CurrentStep;
            previousWindow = step.TargetWindowTypeName;
            step.TargetWindowTypeName = null;
            main.Show();
            dialog.Show();
            target.Focus();
            overlay = new TutorialOverlay(dialog, main);
            overlay.Show();
            Dispatcher.UIThread.RunJobs();
            check(main, dialog, button, overlay);
        }
        finally
        {
            overlay?.Close();
            TutorialHead.ResetForTests();
            dialog.Close();
            main.Close();
            if (step != null) step.TargetWindowTypeName = previousWindow;
            TutorialService.CompletionStore = previousStore;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static void Click(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task DialogTourBlocksMainWindowWithoutTakingDialogFocus() => WithWindows((main, dialog, button, overlay) =>
    {
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        Assert.Same(dialog.Content, dialog.FocusManager?.GetFocusedElement());
        Click(main, button);
        Assert.Equal(0, clicks);
        overlay.Hide();
        Click(main, button);
        Assert.Equal(1, clicks);
        overlay.Show();
        Click(main, button);
        Assert.Equal(1, clicks);
        overlay.Close();
        Click(main, button);
        Assert.Equal(2, clicks);
    });

    [Fact]
    public Task ResizingMainKeepsItsInputBlocked() => WithWindows((main, dialog, button, overlay) =>
    {
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Bottom;
        main.Width = 1200;
        main.Height = 850;
        main.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        Click(main, button);
        Assert.Equal(0, clicks);
    });

    [Fact]
    public Task NonBlockingStepRestoresMainInputUntilBlockingStepReturns() => WithWindows((main, dialog, button, overlay) =>
    {
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        CoreTutorial.RaiseStepChanged(null, new CoreTutorial.Step { BlockBackgroundClicks = false });
        Click(main, button);
        Assert.Equal(1, clicks);
        CoreTutorial.RaiseStepChanged(null, new CoreTutorial.Step { BlockBackgroundClicks = true });
        Click(main, button);
        Assert.Equal(1, clicks);
    });

    [Theory]
    [InlineData(TutorialType.DeeperEditorInteractiveLocalAudio, "iaud_browse", "BtnBrowse")]
    [InlineData(TutorialType.DeeperEditorInteractiveLocalAudio, "iaud_create", "BtnCreate")]
    [InlineData(TutorialType.DeeperEditorInteractiveLocalVideo, "ivid_browse", "BtnBrowse")]
    [InlineData(TutorialType.DeeperEditorInteractiveLocalVideo, "ivid_create", "BtnCreate")]
    public Task NonBlockingSpotlightStillBlocksMainWindow(TutorialType type, string id, string targetName) => WithWindows((main, dialog, button, overlay) =>
    {
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        var step = TutorialHead.Service.StepsFor(type).Single(s => s.Id == id);
        Assert.False(step.BlockBackgroundClicks);

        // A missing target draws the nonblocking full overlay.
        CoreTutorial.RaiseStepChanged(null, new CoreTutorial.Step
        {
            TargetElementName = "MissingTarget", BlockBackgroundClicks = false,
        });
        Click(main, button);
        Assert.Equal(1, clicks);

        // Once a spotlight is actually drawn, WPF dims and blocks outside it.
        CoreTutorial.RaiseStepChanged(null, TutorialHead.Project(step));
        Click(main, button);
        Assert.Equal(1, clicks);
        var target = ((Canvas)dialog.Content!).Children.OfType<Button>().Single();
        var targetClicks = 0;
        target.Click += (_, _) => targetClicks++;
        Click(dialog, target);
        Assert.Equal(1, targetClicks);
    }, targetName);

    [Theory]
    [InlineData("skip")]
    [InlineData("panic")]
    [InlineData("host-close")]
    [InlineData("finish")]
    public Task EndingTourRestoresMainInput(string ending) => WithWindows((main, dialog, button, overlay) =>
    {
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        Click(main, button);
        Assert.Equal(0, clicks);
        switch (ending)
        {
            case "skip": TutorialHead.Service.Skip(); break;
            case "panic": TutorialHead.OnPanic(); break;
            case "host-close": dialog.Close(); break;
            case "finish": while (TutorialHead.Service.IsActive) TutorialHead.Service.Next(); break;
        }
        Dispatcher.UIThread.RunJobs();
        Click(main, button);
        Assert.Equal(1, clicks);
    });
}
