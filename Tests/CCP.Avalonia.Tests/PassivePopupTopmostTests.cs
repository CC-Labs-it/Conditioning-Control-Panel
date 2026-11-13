using System;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PassivePopupTopmostTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LoadedRaisesEachPassivePopupWithoutActivation(int kind) => AvaloniaTestDispatcher.Run(() =>
    {
        Window popup = kind switch
        {
            0 => new PinkRushPopup(),
            1 => new AchievementPopup(),
            _ => new ItemUnlockedPopup()
        };
        var previous = X11Overlay.RaiseRequestedForTests;
        var raises = 0;
        try
        {
            X11Overlay.RaiseRequestedForTests = window => { if (ReferenceEquals(window, popup)) raises++; };
            Assert.False(popup.ShowActivated);
            popup.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, raises);
            Assert.False(popup.ShowActivated);
        }
        finally
        {
            popup.Close();
            X11Overlay.RaiseRequestedForTests = previous;
        }
    });
}
