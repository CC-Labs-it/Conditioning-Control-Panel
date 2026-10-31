using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Remote tab "List me in the directory" (WPF MainWindow.RemoteControl.cs:515-738), the title-bar
/// listing pill, the share-media-sources row, the hero art and the tube's emote bubble (WPF
/// AvatarTubeWindow.ChatInput.cs:957), against a fake relay. Swaps the process-wide relay, so it runs alone.
/// Adapted from parity/trunk 61daf0358 + d7a46b6e4.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class RemoteDirectoryTabTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly List<(string Path, JObject Body)> Seen = new();
        public string Poll = "{}";
        public HttpStatusCode OptIn = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            var body = JObject.Parse(await r.Content!.ReadAsStringAsync(ct));
            lock (Seen) Seen.Add((path, body));
            var (status, json) = path switch
            {
                "/v2/remote/start" => (HttpStatusCode.OK, "{\"code\":\"ABC123\"}"),
                "/v2/remote/poll" => (HttpStatusCode.OK, Poll),
                "/v2/directory/opt-in" => (OptIn, "{}"),
                _ => (HttpStatusCode.OK, "{}"),
            };
            return new HttpResponseMessage(status) { Content = new StringContent(json) };
        }
        public List<JObject> At(string path) { lock (Seen) return Seen.Where(s => s.Path == path).Select(s => s.Body).ToList(); }
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    private static void Click(CheckBox box)
    {
        box.IsChecked = !(box.IsChecked ?? false);
        box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [Fact]
    public Task Opt_in_form_caps_tags_counts_the_status_lists_the_session_and_remembers_only_when_asked() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureAvalonia();
        var (oldRelay, oldProvider) = (RemoteControlTabView.Relay, CoreSettings.ServiceProvider);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var f = new FakeRelay();
        var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => { }, f) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        Window? host = null;
        try
        {
            var s = CoreSettings.Current;
            (s.RememberDirectoryDetails, s.SavedDirectoryTags, s.SavedDirectoryStatusText) = (true, new List<string> { "trance" }, "saved line");
            var view = new RemoteControlTabView();
            host = new Window { Width = 1200, Height = 900, Content = view };
            host.Show();
            Dispatcher.UIThread.RunJobs();

            // UI jobs do not wait for the worker decode. Readiness includes assigning the brush.
            await view.FeatureArtReady.WaitAsync(TimeSpan.FromSeconds(30));

            // Both blocks are on the page, as on WPF; the hero plate carries remote_control.png.
            Assert.True(view.FindControl<Border>("EmotePickerPanel")!.IsVisible);
            Assert.IsType<global::Avalonia.Media.ImageBrush>(view.FindControl<Border>("HeroArtPlate")!.Background);

            // Share media sources: seeded from settings, saved on flip (WPF RemoteControlTabView.xaml.cs:122).
            var media = view.FindControl<CheckBox>("ChkRemoteShareMediaSources")!;
            Assert.Equal(s.RemoteShareMediaSources, media.IsChecked);
            media.IsChecked = !s.RemoteShareMediaSources;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(media.IsChecked, s.RemoteShareMediaSources);
            var section = view.FindControl<Border>("OptInSectionPanel")!;
            Assert.True(section.IsVisible);
            var form = view.FindControl<StackPanel>("OptInFormPanel")!;
            Assert.False(form.IsVisible);

            // Ticking opens the form with the remembered details.
            var optIn = view.FindControl<CheckBox>("ChkOptIntoDirectory")!;
            optIn.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(form.IsVisible);
            Assert.True(view.FindControl<CheckBox>("ChkTagTrance")!.IsChecked);
            Assert.False(view.FindControl<CheckBox>("ChkTagDrone")!.IsChecked);
            var status = view.FindControl<TextBox>("TxtOptInStatus")!;
            Assert.Equal("saved line", status.Text);
            Assert.Equal("10/80", view.FindControl<TextBlock>("TxtOptInStatusCount")!.Text);
            Assert.True(view.FindControl<CheckBox>("ChkRememberOptInDetails")!.IsChecked);
            status.Text = "hello";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("5/80", view.FindControl<TextBlock>("TxtOptInStatusCount")!.Text);

            // Five tags at most: the sixth tick is undone and says why.
            foreach (var name in new[] { "ChkTagBimbo", "ChkTagDrone", "ChkTagFeminization", "ChkTagSubmission" })
                Click(view.FindControl<CheckBox>(name)!);
            var feedback = view.FindControl<TextBlock>("TxtOptInFeedback")!;
            Assert.False(feedback.IsVisible);
            var sixth = view.FindControl<CheckBox>("ChkTagChastity")!;
            Click(sixth);
            Assert.False(sixth.IsChecked);
            Assert.True(feedback.IsVisible);
            Assert.Equal(Loc.Get("msg_optin_directory_max_tags"), feedback.Text);

            // No session: nothing is published, nobody is told they are listed.
            var listed = view.FindControl<Border>("ListedConfirmationPanel")!;
            await view.RunOptInChainAsync();
            Assert.Empty(f.At("/v2/directory/opt-in"));
            Assert.False(listed.IsVisible);
            Assert.Equal(Loc.Get("msg_optin_directory_failed"), feedback.Text);

            // A refusal (429 here) is the same one inline line; the session is untouched.
            Assert.Equal("ABC123", await relay.StartAsync("light"));
            f.OptIn = HttpStatusCode.TooManyRequests;
            await view.RunOptInChainAsync();
            Assert.False(listed.IsVisible);
            Assert.True(relay.IsActive);
            Assert.Equal(Loc.Get("msg_optin_directory_failed"), feedback.Text);

            // Listed: the banner shows, and with Remember off the saved details go.
            f.OptIn = HttpStatusCode.OK;
            view.FindControl<CheckBox>("ChkRememberOptInDetails")!.IsChecked = false;
            await view.RunOptInChainAsync();
            Assert.True(listed.IsVisible);
            var body = f.At("/v2/directory/opt-in").Last();
            Assert.Equal(new[] { "bimbo", "drone", "trance", "feminization", "submission" }, body["tags"]!.Select(t => (string?)t).ToArray());
            Assert.Equal("hello", (string?)body["status_text"]);
            Assert.False(s.RememberDirectoryDetails);
            Assert.Empty(s.SavedDirectoryTags);
            Assert.Equal("", s.SavedDirectoryStatusText);

            // Remember on: the tags and the line are kept for next time.
            view.FindControl<CheckBox>("ChkRememberOptInDetails")!.IsChecked = true;
            await view.RunOptInChainAsync();
            Assert.True(s.RememberDirectoryDetails);
            Assert.Equal(5, s.SavedDirectoryTags.Count);
            Assert.Equal("hello", s.SavedDirectoryStatusText);

            // The session ends: the banner goes, the tick is cleared (re-opt every session).
            await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(listed.IsVisible);
            Assert.False(optIn.IsChecked);
            Assert.True(section.IsEnabled);

            // A session started from the tab runs the chain itself: ticked beforehand, it is listed.
            optIn.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            var before = f.At("/v2/directory/opt-in").Count;
            await view.StartAsync(host, "light");
            Assert.NotNull(view.OptInChain);
            await view.OptInChain!;
            Assert.Equal(before + 1, f.At("/v2/directory/opt-in").Count);
            Assert.True(listed.IsVisible);
            await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            host?.Close();
            relay.Dispose();
            (RemoteControlTabView.Relay, CoreSettings.ServiceProvider) = (oldRelay, oldProvider);
        }
    });

    [Fact]
    public Task Title_bar_pill_is_private_then_listed_then_claimed_and_hides_when_the_session_ends() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureAvalonia();
        var (oldRelay, oldProvider) = (RemoteControlTabView.Relay, CoreSettings.ServiceProvider);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var f = new FakeRelay();
        var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => { }, f) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var pill = shell.Named<Border>("DirectoryStatusPill")!;
            var text = shell.Named<TextBlock>("TxtDirectoryStatus")!;
            var tab = shell.Named<RemoteControlTabView>("RemoteControlTab")!;
            Assert.False(pill.IsVisible);

            async Task Poll(bool connected)
            {
                f.Poll = connected ? "{\"controller_connected\":true}" : "{\"controller_connected\":false}";
                await relay.PollOnceAsync();
                Dispatcher.UIThread.RunJobs();
            }

            // A session that is not listed: "Private only" even with a controller on.
            await relay.StartAsync("light");
            await Poll(true);
            Assert.True(pill.IsVisible);
            Assert.Equal("Private only", text.Text);
            await Poll(false);

            // Listed, then claimed by a controller, then listed again when it leaves.
            tab.FindControl<CheckBox>("ChkOptIntoDirectory")!.IsChecked = true;
            await tab.RunOptInChainAsync();
            Assert.Equal("Listed", text.Text);
            Assert.True(tab.FindControl<Border>("ListedConfirmationPanel")!.IsVisible);
            await Poll(true);
            Assert.Equal("Claimed", text.Text);
            await Poll(false);
            Assert.Equal("Listed", text.Text);

            // The session ends: the pill goes.
            await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(pill.IsVisible);
        }
        finally
        {
            shell?.Close();
            relay.Dispose();
            (RemoteControlTabView.Relay, CoreSettings.ServiceProvider) = (oldRelay, oldProvider);
        }
    });

    [Fact]
    public void Pill_states_match_wpf()
    {
        Assert.False(MainShellWindow.DirectoryPillFor(active: false, optedIn: true, claimed: true).Visible);
        Assert.Equal(("Private only", "#8A8AA0"), Pick(MainShellWindow.DirectoryPillFor(true, false, true)));
        Assert.Equal(("Listed", "#B47BFF"), Pick(MainShellWindow.DirectoryPillFor(true, true, false)));
        Assert.Equal(("Claimed", "#00FF88"), Pick(MainShellWindow.DirectoryPillFor(true, true, true)));
        static (string, string) Pick(MainShellWindow.DirectoryPill p) => (p.Text, p.DotHex);
    }

    [Fact]
    public Task Tube_bubble_says_sending_then_sent_and_never_over_an_ai_bubble() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        AvatarTubeWindow? tube = null;
        try
        {
            tube = new AvatarTubeWindow(null);
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            var speech = tube.FindControl<TextBlock>("TxtSpeech")!;

            tube.ShowEmoteFeedback("good girl", isPending: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Sending...", speech.Text);
            Assert.True(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
            // Up one second longer than a preset line of the same text (WPF ShowEmoteFeedback).
            var timer = (DispatcherTimer)typeof(AvatarTubeWindow).GetField("_speechTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(tube)!;
            Assert.Equal(TimeSpan.FromSeconds(Math.Clamp(CoreSettings.Current.BubbleDurationSeconds, 1.0, 10.0) + 1), timer.Interval);

            var longText = new string('x', 50);
            tube.ShowEmoteFeedback(longText, isPending: false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal($"Sent: \"{new string('x', 40)}...\"", speech.Text);

            // An AI bubble owns the surface: the feedback stays out of its way.
            typeof(AvatarTubeWindow).GetField("_isShowingAiBubble", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(tube, true);
            tube.ShowEmoteFeedback("again", isPending: true);
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("Sent:", speech.Text);
        }
        finally { tube?.Close(); }
        return Task.CompletedTask;
    });
}
