using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class QuestCardNavigationTests
{
    [Fact]
    public void DailyCardRequiresPressOnCardAndOpensItsRack() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldQuests = AvApp.Quests;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        (settings.Current.Welcomed, settings.Current.HasAcceptedAgeVerification, settings.Current.WhatMovedCardShown, settings.Current.AvatarEnabled) = (true, true, 1, false);
        var dir = Directory.CreateTempSubdirectory("ccp-quest-door-").FullName;
        using var quests = new QuestService(null, dir);
        quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d") };
        AvApp.Quests = quests;
        var shell = new MainShellWindow { Width = 1600, Height = 1400 };
        try
        {
            shell.Show(); shell.Activate(); shell.ShowTab("quests");
            Dispatcher.UIThread.RunJobs(); shell.CompleteTabTransition(); shell.UpdateLayout();
            var view = shell.Named<QuestsTabView>("QuestsTab")!;
            view.RefreshQuestUI();
            view.FindControl<Border>("QuestsLoginOverlay")!.IsVisible = false;
            shell.UpdateLayout(); HeadlessRendering.RenderLiveTree(shell);
            var card = view.FindControl<Control>("DailyCard0")!;
            var at = card.TranslatePoint(new Point(card.Bounds.Width / 2, 30), shell)!.Value;
            Assert.True(card.IsVisible && card.Bounds.Width > 0, $"card bounds {card.Bounds}");
            Assert.True(shell.GetVisualsAt(at).Any(v => v == card || card.IsVisualAncestorOf(v)), $"card at {at}, hits {string.Join(", ", shell.GetVisualsAt(at).Select(v => v.GetType().Name))}");
            Assert.NotNull(card.Cursor);
            var outside = new Point(5, 5);
            shell.MouseMove(outside); shell.MouseDown(outside, MouseButton.Left);
            shell.MouseMove(at); shell.MouseUp(at, MouseButton.Left);
            Assert.Equal("quests", shell.CurrentTab);
            shell.MouseMove(outside); shell.MouseMove(at); shell.MouseDown(at, MouseButton.Left); shell.MouseUp(at, MouseButton.Left);
            Assert.Equal("studio", shell.CurrentTab);
        }
        finally
        {
            shell.Close(); Dispatcher.UIThread.RunJobs();
            AvApp.Quests = oldQuests; CoreSettings.ServiceProvider = oldSettings;
            try { Directory.Delete(dir, true); } catch { }
        }
    });
}
