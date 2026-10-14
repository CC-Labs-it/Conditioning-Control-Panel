using System;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
public sealed class ProofApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
public sealed class PackFxProof
{
    [Fact]
    public void FixtureUsesTheProductionOuterCardAndItsPointerBindings()
    {
        var production = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ProductionPackCards.axaml"));
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AssetsTabView.axaml"));
        var card = Regex.Match(production, "<Border Classes=\"packCard\".*?BorderThickness=\"1\">", RegexOptions.Singleline).Value;
        Assert.NotEmpty(card);
        Assert.Contains(card.Replace("<Border Classes=", "<Border x:Name=\"Card1\" Classes="), fixture);
        Assert.Contains(card.Replace("<Border Classes=", "<Border x:Name=\"Card2\" Classes="), fixture);
    }

    [Fact]
    public Task PointerMovesOneSheenAndLateLeaveDoesNotStopTheNewCard() => Run((w, v, layer) =>
    {
        Hover(w,v.Card(1),true);
        var first = Assert.Single(layer.Children.OfType<CardSheenAdorner>());
        Assert.True(first.IsTicking);
        Hover(w,v.Card(1),true);
        Assert.Same(first,Assert.Single(layer.Children.OfType<CardSheenAdorner>()));
        Hover(w,v.Card(2),true);
        var second=Assert.Single(layer.Children.OfType<CardSheenAdorner>());
        Assert.NotSame(first,second); Assert.False(first.IsTicking);
        Hover(w,v.Card(1),false);
        Assert.Same(second,Assert.Single(layer.Children.OfType<CardSheenAdorner>()));
        Hover(w,v.Card(2),false);
        Assert.Empty(layer.Children.OfType<CardSheenAdorner>()); Assert.False(second.IsTicking);
    });
    [Fact]
    public Task HiddenAncestorAndMotionOffRefuseSheenAndHidingStopsItsClock() => Run((w,v,layer) =>
    {
        AmbientFxCanvas.Env.AllowTransitions=false;
        Hover(w,v.Card(1),true); Assert.Empty(layer.Children.OfType<CardSheenAdorner>());
        AmbientFxCanvas.Env.AllowTransitions=true;
        Hover(w,v.Card(1),true);
        var sheen=Assert.Single(layer.Children.OfType<CardSheenAdorner>());
        v.IsVisible=false;
        Assert.Empty(layer.Children.OfType<CardSheenAdorner>()); Assert.False(sheen.IsTicking);
        Hover(w,v.Card(1),true); Assert.Empty(layer.Children.OfType<CardSheenAdorner>());
    });
    [Fact]
    public Task RemovingTheCardStopsTheProductionFrameClockAndRemovesTheAdorner() => Run((w,v,layer) =>
    {
        Hover(w,v.Card(1),true);
        var sheen=Assert.Single(layer.Children.OfType<CardSheenAdorner>());
        ((Panel)v.Card(1).Parent!).Children.Remove(v.Card(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(layer.Children.OfType<CardSheenAdorner>()); Assert.False(sheen.IsTicking);
    });
    [Fact]
    public Task UnloadingTheTabStopsTheClock() => Run((w,v,layer) =>
    {
        Hover(w,v.Card(1),true);
        var sheen=Assert.Single(layer.Children.OfType<CardSheenAdorner>());
        w.Content=null; Dispatcher.UIThread.RunJobs();
        Assert.Empty(layer.Children.OfType<CardSheenAdorner>()); Assert.False(sheen.IsTicking);
    });
    private static void Hover(Window w,Control c,bool on) => c.RaiseEvent(new PointerEventArgs(
        on ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent,c,
        new global::Avalonia.Input.Pointer(1,PointerType.Mouse,true),w,default,0,PointerPointProperties.None,KeyModifiers.None));
    private static Task Run(Action<Window,AssetsTabView,AdornerLayer> action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if(Application.Current==null)AppBuilder.Configure<ProofApp>().WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=false }).SetupWithoutStarting();
        AmbientFxCanvas.Env.AllowTransitions=true;
        var v=new AssetsTabView();var w=new Window { Width=440,Height=300,Content=v };
        w.Show();Dispatcher.UIThread.RunJobs();w.UpdateLayout();
        try { action(w,v,AdornerLayer.GetAdornerLayer(v.Card(1))!); }
        finally { w.Close();Dispatcher.UIThread.RunJobs();AmbientFxCanvas.Env.AllowTransitions=true; }
        return Task.CompletedTask;
    });
}
