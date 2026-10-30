using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Avalonia.Tests;
[Collection(RunsAloneCollection.Name)]
public sealed class EnhancementsPurchaseTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public string Answer = "{\"success\":false,\"error\":\"refused\"}";
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Answer) });
        }
    }
    [Fact]
    public Task ClickConfirmsRefusesThenAdoptsReceiptAndPanicClosesQuestion() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.SkillPoints = 1000; s.NightTimeUsageCount = 10;
        s.UnifiedId = "purchase-test";
        s.UnlockedSkills = new() { "sparkle_boost_1" };
        var http = new Fake();
        var tab = new EnhancementsTabView { PurchaseService = new SkillPurchase(() => s, http) { Save = () => { } } };
        var question = new TaskCompletionSource<bool>();
        int questions = 0, failures = 0; Control? buying = null;
        tab.AskPurchase = (_, _, message) => { questions++; Assert.Contains("sparkle", message.ToLowerInvariant()); return question.Task; };
        tab.TellFailure = (_, _, error) => { Assert.Equal("refused", error); Assert.False(buying!.IsEnabled); failures++; return Task.CompletedTask; };
        var w = new Window { Width = 1400, Height = 700, Content = tab };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Control Node(string id) => tab.FindControl<Canvas>("SkillTreeCanvas")!.Children.OfType<Control>().Single(c => c.Tag as string == id);
            var node = buying = Node("sparkle_boost_2"); Assert.Null(Node("lucky_bimbo").Cursor); Assert.NotNull(tab.SecretSkillCard(SkillDefinition.All.Single(x => x.Id == "night_shift")).Cursor);
            var point = node.TranslatePoint(new Point(40, 40), w)!.Value;
            w.MouseDown(point, MouseButton.Left);
            w.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, questions); await tab.PurchaseAsync(node, "sparkle_boost_2").WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, questions); Assert.Equal(0, http.Calls);
            question.SetResult(false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1000, s.SkillPoints);
            tab.AskPurchase = (_, _, _) => Task.FromResult(true);
            await tab.PurchaseAsync(node, "sparkle_boost_2");
            Assert.Equal(1, failures); Assert.Equal(1000, s.SkillPoints); Assert.True(node.IsEnabled); Assert.NotSame(node, Node("sparkle_boost_2"));
            http.Answer = "{\"success\":true,\"skill_points\":7,\"unlocked_skills\":[\"sparkle_boost_2\"]}";
            buying = Node("sparkle_boost_2"); await tab.PurchaseAsync(buying, "sparkle_boost_2");
            Assert.Equal(7, s.SkillPoints); Assert.Contains("sparkle_boost_1", s.UnlockedSkills); Assert.Contains("sparkle_boost_2", s.UnlockedSkills);
            Assert.Null(Node("sparkle_boost_2").Cursor);
            var dialogTab = new EnhancementsTabView();
            var prompt = dialogTab.AskPurchase(w, "Purchase", "Confirm purchase");
            Dispatcher.UIThread.RunJobs();
            Assert.False(prompt.IsCompleted);
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(x => x.Id == "enhancement-purchase").Stop(null);
            Assert.False(await prompt.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { w.Close(); }
    });
}
