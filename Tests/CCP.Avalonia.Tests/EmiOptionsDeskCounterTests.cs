using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiOptionsDeskCounterTests
{
    [Fact]
    public void DeskCounterClosesPanelAndOpensDeskFromWallet() => AvaloniaTestDispatcher.Run(() =>
    {
        var window = new EmiOptionsWindow();
        try
        {
            var button = window.FindControl<Button>("BtnDeskCounter")!;
            var picks = window.FindControl<WrapPanel>("DeskToyPicks")!;
            Assert.Same(picks.Parent, button.Parent);
            Assert.Equal(Loc.Get("emi_desk_opt_counter"), button.Content);
            string? from = null, tab = null;
            window.LaunchPrizeCounter = (f, t) => { Assert.False(window.IsOpen); from = f; tab = t; };
            window.OpenPanel();
            Assert.True(window.IsOpen);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsOpen);
            Assert.Equal("wallet", from);
            Assert.Equal("desk", tab);
        }
        finally { window.Kill(); }
    });
}
