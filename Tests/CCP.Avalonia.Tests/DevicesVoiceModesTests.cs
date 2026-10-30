using System.Threading.Tasks;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>u1 G2 / G12: Settings > Devices voice. The wake word and push-to-talk switches write (after
/// the premium bar, mic consent and the always-on OK, in WPF order; OFF is never gated), Set key captures
/// the next real key, and the microphone, phrases and headphones apply live. No microphone is opened:
/// the shell (which owns the engine) is absent, and the prompts are answered by the test.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class DevicesVoiceModesTests
{
    [Fact]
    public Task HeadphonesPreserveSpokenStopListening() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            var section = new DevicesSettingsSection();
            shell.Content = section;
            shell.Show();
            var s = CoreSettings.Current;
            s.MicConsentGiven = s.SpeechWakeWordEnabled = true;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreSpeech.IsAvailableProvider = () => true;
            section.SyncFromSettings();
            shell.RefreshVoiceInputModes();
            Assert.True(shell.WakeLoopArmed);
            shell.StopVoiceInput(); // The spoken stop_listening action, WPF AutonomyService.Voice.cs.
            Assert.True(s.SpeechWakeWordEnabled);
            var headphones = section.FindControl<CheckBox>("ChkHeadphones")!;
            Assert.Same(shell, TopLevel.GetTopLevel(section));
            var applies = section.VoiceApplies;
            var before = s.SpeechHeadphonesMode;
            headphones.IsChecked = !(headphones.IsChecked == true);
            Assert.NotEqual(before, s.SpeechHeadphonesMode);
            Assert.Equal(applies, section.VoiceApplies);
            Assert.True(CoreSpeech.IsAvailable);
            Assert.True(CoreEntitlement.HasPremium);
            Assert.Equal(headphones.IsChecked == true, s.SpeechHeadphonesMode);
            Assert.False(shell.WakeLoopArmed);
        }
        finally { shell.StopVoiceInput(); shell.RequestExit(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("en", "Always-on microphone")]
    [InlineData("de", "Dauerhaft aktives Mikrofon")]
    [InlineData("es", "Micrófono siempre activo")]
    [InlineData("fr", "Microphone toujours actif")]
    [InlineData("it", "Microfono sempre attivo")]
    [InlineData("ja", "常時オンのマイク")]
    [InlineData("ko", "항상 켜진 마이크")]
    [InlineData("pt-BR", "Microfone sempre ativo")]
    [InlineData("ru", "Постоянно включённый микрофон")]
    [InlineData("zh-CN", "始终开启的麦克风")]
    public Task AlwaysOnDialogUsesSelectedLanguage(string language, string title) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var before = LocalizationManager.Instance.CurrentLanguage;
        var owner = new Window();
        try
        {
            LocalizationManager.Instance.SetLanguage(language);
            CoreSettings.Current.MicConsentGiven = true;
            CoreSettings.Current.SpeechWakeWordEnabled = false;
            CoreEntitlement.HasPremiumProvider = () => true;
            var section = new DevicesSettingsSection();
            owner.Content = section;
            owner.Show();
            section.FindControl<CheckBox>("ChkSpeechWakeWord")!.IsChecked = true;
            await Task.Yield();
            var dialog = Assert.Single(owner.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(title, dialog.Title);
            Assert.Equal(Loc.Get("devices_always_on_body"), dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            Assert.Equal(Loc.Get("btn_yes"), Assert.IsType<TextBlock>(dialog.FindControl<Button>("BtnOk")!.Content).Text);
            Assert.Equal(Loc.Get("btn_no"), Assert.IsType<TextBlock>(dialog.FindControl<Button>("BtnCancel")!.Content).Text);
            dialog.Close(false);
            await Task.Yield();
            Assert.False(CoreSettings.Current.SpeechWakeWordEnabled);
        }
        finally
        {
            foreach (var dialog in owner.OwnedWindows.ToList()) dialog.Close();
            owner.Close();
            LocalizationManager.Instance.SetLanguage(before);
        }
    });

    [Fact]
    public async Task VoiceSwitchesWrite_GatedOnConsent_AndApplyLive()
    {
        var s = CoreSettings.Current;
        var old = (s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled, s.MicConsentGiven, s.SpeechPushToTalkKey, s.SpeechWakeWords, s.SpeechHeadphonesMode);
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        var oldDenied = CoreEntitlement.ShowDeniedHandler;
        try
        {
            bool premium = false;
            int denied = 0, asks = 0;
            CoreEntitlement.HasPremiumProvider = () => premium;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            CoreEntitlement.ShowDeniedHandler = _ => denied++;
            s.SpeechWakeWordEnabled = false; s.SpeechPushToTalkEnabled = false; s.MicConsentGiven = false;
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var section = new DevicesSettingsSection();
                var window = new Window { Content = section };
                window.Show();
                var wake = section.FindControl<CheckBox>("ChkSpeechWakeWord")!;
                var ptt = section.FindControl<CheckBox>("ChkSpeechPushToTalk")!;

                // Consent declined: the switch falls back, nothing is saved, no mic.
                bool consent = false, alwaysOn = false;
                section.AskMicConsent = () => { asks++; if (consent) s.MicConsentGiven = true; return Task.FromResult(consent); };
                section.AskAlwaysOn = () => Task.FromResult(alwaysOn);

                // Premium bar refused (WPF order: before consent): told once, switch falls back, no ask.
                wake.IsChecked = true;
                await Task.Yield();
                Assert.False(wake.IsChecked);
                Assert.False(s.SpeechWakeWordEnabled);
                Assert.Equal(1, denied);
                Assert.Equal(0, asks);
                ptt.IsChecked = true;
                Assert.False(ptt.IsChecked);
                Assert.False(s.SpeechPushToTalkEnabled);
                Assert.Equal(2, denied);
                premium = true;

                wake.IsChecked = true;
                await Task.Yield();
                Assert.False(wake.IsChecked);
                Assert.False(s.SpeechWakeWordEnabled);
                Assert.Equal(0, section.VoiceApplies);
                Assert.Equal(1, asks);

                // Consent given but the always-on question answered No: still off.
                consent = true;
                wake.IsChecked = true;
                await Task.Yield();
                Assert.False(wake.IsChecked);
                Assert.False(s.SpeechWakeWordEnabled);

                // Both yes: saved and applied live.
                alwaysOn = true;
                wake.IsChecked = true;
                await Task.Yield();
                Assert.True(s.SpeechWakeWordEnabled);
                Assert.Equal(1, section.VoiceApplies);

                // Off is never gated, never asks.
                consent = false; alwaysOn = false; premium = false;
                wake.IsChecked = false;
                await Task.Yield();
                Assert.False(s.SpeechWakeWordEnabled);
                Assert.Equal(2, section.VoiceApplies);

                premium = true;
                // Push to talk: consent is current now, one click writes.
                ptt.IsChecked = true;
                await Task.Yield();
                Assert.True(s.SpeechPushToTalkEnabled);
                Assert.Equal(3, section.VoiceApplies);

                // Set key: a lone modifier keeps waiting, the next real key is stored and shown.
                section.BtnSetPttKey_Click(null, new RoutedEventArgs());
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.LeftCtrl });
                Assert.NotEqual("LeftCtrl", s.SpeechPushToTalkKey);
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F9 });
                Assert.Equal("F9", s.SpeechPushToTalkKey);
                Assert.Equal("F9", section.FindControl<TextBlock>("TxtPttKey")!.Text);
                // The label goes back as a TextBlock child (trap 1: bare Content eats "_").
                Assert.IsType<TextBlock>(section.FindControl<Button>("BtnSetPttKey")!.Content);
                Assert.Equal(4, section.VoiceApplies);

                // G12: headphones update presentation without reconciling voice modes.
                var head = section.FindControl<CheckBox>("ChkHeadphones")!;
                head.IsChecked = !(head.IsChecked == true);
                Assert.Equal(head.IsChecked == true, s.SpeechHeadphonesMode);
                Assert.Equal(4, section.VoiceApplies);
                var phrases = section.FindControl<TextBox>("TxtSpeechWakeWords")!;
                phrases.Text = "  ";
                phrases.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));   // TextBox's own handler casts to this
                Assert.Equal("hey bambi", s.SpeechWakeWords);
                Assert.Equal("hey bambi", phrases.Text);
                Assert.Equal(5, section.VoiceApplies);
                var mic = section.FindControl<ComboBox>("CmbMicDevice")!;
                mic.Items.Add(new ComboBoxItem { Content = "Test mic", Tag = 999 });
                mic.SelectedIndex = mic.Items.Count - 1;
                Assert.Equal(999, s.SpeechInputDeviceIndex);
                Assert.Equal("Test mic", s.SpeechInputDeviceName);
                Assert.Equal(6, section.VoiceApplies);
                premium = false;
                ptt.IsChecked = false;
                Assert.False(s.SpeechPushToTalkEnabled);
                Assert.Equal(7, section.VoiceApplies);
                window.Close();
            });
        }
        finally
        {
            CoreEntitlement.HasPremiumProvider = oldPremium;
            CoreEntitlement.IsFreeTodayProvider = oldFree;
            CoreEntitlement.ShowDeniedHandler = oldDenied;
            (s.SpeechWakeWordEnabled, s.SpeechPushToTalkEnabled, s.MicConsentGiven, s.SpeechPushToTalkKey, s.SpeechWakeWords, s.SpeechHeadphonesMode) = old;
            CoreSettings.SaveImmediate();
        }
    }
}
