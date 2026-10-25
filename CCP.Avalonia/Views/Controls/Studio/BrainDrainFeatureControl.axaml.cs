using System;
using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SettingsModel = ConditioningControlPanel.Models.AppSettings;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio;

public partial class BrainDrainFeatureControl : UserControl
{
    private bool _loading;
    private SettingsModel? _settings;
    public BrainDrainFeatureControl()
    {
        AvaloniaXamlLoader.Load(this);
        ModArt.BindFeaturePlates(this, "features/brain_drain.png", this.FindControl<Border>("HeroArt"), this.FindControl<Border>("SideArt"));
        Toggle("ChkEnable", s => s.BrainDrainEnabled, (s,v) => s.BrainDrainEnabled = v, () =>
        {
            if (!CoreEngine.IsRunning) return;
            if (CoreSettings.Current.BrainDrainEnabled) BrainDrainPlayer.Instance?.Start();
            else BrainDrainPlayer.Instance?.Stop();
        });
        Toggle("ChkHighRefresh", s => s.BrainDrainHighRefresh, (s,v) => s.BrainDrainHighRefresh = v, () =>
        {
            if (CoreEngine.IsRunning && BrainDrainPlayer.Instance?.IsRunning == true)
            { BrainDrainPlayer.Instance.Stop(); BrainDrainPlayer.Instance.Start(); }
        });
        Toggle("ChkMelt", s => s.BrainDrainMeltEnabled, (s,v) => s.BrainDrainMeltEnabled = v);
        Toggle("ChkKeepClear", s => s.BrainDrainKeepPicturesClear, (s,v) => s.BrainDrainKeepPicturesClear = v);
        Toggle("ChkAllowCapture", s => s.AllowOverlayCapture, (s,v) => s.AllowOverlayCapture = v);
        Slider("Intensity", s => s.BrainDrainIntensity, (s,v) => s.BrainDrainIntensity = v);
        Slider("BlurStrength", s => s.BrainDrainBlurStrength, (s,v) => s.BrainDrainBlurStrength = v);
        Slider("Volume", s => s.BrainDrainVolume, (s,v) => s.BrainDrainVolume = v);
        this.FindControl<Button>("BtnRefreshAudio")!.Click += (_,_) => RefreshClips();
        foreach (var name in new[] { "BtnOpenAudioFolder", "BtnOpenAudioFolderEmpty" })
            this.FindControl<Button>(name)!.Click += async (_,_) =>
            {
                try { Directory.CreateDirectory(BrainDrainAudioPolicy.AudioFolder); await ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), BrainDrainAudioPolicy.AudioFolder); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Brain Drain: open clip folder failed"); }
            };
        // ponytail: audio works; the desktop-capture visual host still needs a separate port.
        this.FindControl<Border>("WithheldNotice")!.IsVisible = true;
        Load();
    }
    private event Action? ReadSettings;
    private void Toggle(string name, Func<SettingsModel,bool> read, Action<SettingsModel,bool> write, Action? apply = null)
    {
        var control = this.FindControl<CheckBox>(name)!;
        ReadSettings += () => control.IsChecked = read(CoreSettings.Current);
        control.IsCheckedChanged += (_,_) =>
        {
            if (_loading) return;
            write(CoreSettings.Current, control.IsChecked ?? false); apply?.Invoke(); CoreSettings.Save();
        };
    }
    private void Slider(string name, Func<SettingsModel,int> read, Action<SettingsModel,int> write)
    {
        var slider = this.FindControl<Slider>("Slider" + name)!;
        var label = this.FindControl<TextBlock>("Txt" + name)!;
        ReadSettings += () => { slider.Value = read(CoreSettings.Current); label.Text = $"{read(CoreSettings.Current)}%"; };
        slider.PropertyChanged += (_,e) =>
        {
            if (e.Property != global::Avalonia.Controls.Slider.ValueProperty || _loading) return;
            var value = (int)slider.Value; label.Text = $"{value}%";
            write(CoreSettings.Current, value); CoreSettings.Save();
        };
    }
    private void Load() => Load(rescan: true);
    // A settings tick only repaints; the clip folders are rescanned on open, rebind and Refresh.
    private void Load(bool rescan)
    {
        _loading = true;
        try
        {
            ReadSettings?.Invoke();
            if (CoreSettings.Current.BrainDrainVolume != 100) this.FindControl<MoreFold>("FoldMore")!.IsOpen = true;
            if (rescan) RefreshClips();
        }
        finally { _loading = false; }
    }
    private void Rebind()
    {
        if (_settings != null) _settings.PropertyChanged -= SettingsChanged;
        _settings = CoreSettings.Current; _settings.PropertyChanged += SettingsChanged; Load();
    }
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName?.StartsWith("BrainDrain", StringComparison.Ordinal) == true || e.PropertyName == nameof(SettingsModel.AllowOverlayCapture))
            Dispatcher.UIThread.Post(() => Load(rescan: false));
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (CoreSettings.Service is { } svc) svc.CurrentReplaced += Rebind;
        Rebind();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= Rebind;
        if (_settings != null) _settings.PropertyChanged -= SettingsChanged;
        base.OnDetachedFromVisualTree(e);
    }
    private void RefreshClips()
    {
        try { Directory.CreateDirectory(BrainDrainAudioPolicy.AudioFolder); } catch { }
        var count = BrainDrainAudioPolicy.Discover(BrainDrainAudioPolicy.AudioFolder, BrainDrainAudioPolicy.LegacyFolder).Length;
        this.FindControl<TextBlock>("TxtClipCount")!.Text = Loc.GetF("st4_braindrain_clips_loaded_0", count);
        this.FindControl<Border>("NoAudioHint")!.IsVisible = count == 0;
        this.FindControl<TextBlock>("TxtAudioFolderPath")!.Text = BrainDrainAudioPolicy.AudioFolder;
        var legacy = BrainDrainAudioPolicy.Enumerate(BrainDrainAudioPolicy.LegacyFolder).Length > 0;
        this.FindControl<TextBlock>("TxtLegacyFolderNote")!.IsVisible = legacy;
        this.FindControl<TextBlock>("TxtLegacyFolderPath")!.IsVisible = legacy;
        this.FindControl<TextBlock>("TxtLegacyFolderPath")!.Text = legacy ? BrainDrainAudioPolicy.LegacyFolder : "";
    }
}
