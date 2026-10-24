using System;
using System.ComponentModel;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AssetsTabView
{
    private bool _officialNoteLanded;
    private CancellationTokenSource? _officialLanding;
    private readonly DispatcherTimer _officialClock = new() { Interval = TimeSpan.FromMinutes(1) };

    private void InitializeOnlineOfficialNote()
    {
        BtnOnlineOfficialNice.Click += (_, _) =>
        {
            S.ScrolllerOfficialNoteSeen = true;
            CoreSettings.Save();
            RefreshOnlineOfficialNote();
        };
        _officialClock.Tick += (_, _) => RefreshOnlineOfficialNote();
        Loaded += (_, _) =>
        {
            S.PropertyChanged += OfficialSettingChanged;
            LocalizationManager.Instance.LanguageChanged += OfficialLanguageChanged;
            RefreshOnlineOfficialNote();
            _officialClock.Start();
        };
        Unloaded += (_, _) =>
        {
            S.PropertyChanged -= OfficialSettingChanged;
            LocalizationManager.Instance.LanguageChanged -= OfficialLanguageChanged;
            _officialClock.Stop();
            _officialLanding?.Cancel();
        };
    }

    private void OfficialSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Models.AppSettings.MediaSource) or nameof(Models.AppSettings.RemoteMediaConsented)
            or nameof(Models.AppSettings.FypOnlineConsented) or nameof(Models.AppSettings.ScrolllerOfficialNoteSeen))
            Dispatcher.UIThread.Post(RefreshOnlineOfficialNote);
    }

    private void OfficialLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshOnlineOfficialNote);

    internal void RefreshOnlineOfficialNote()
    {
        var credit = OnlineOfficialNoteRules.ShowCredit(S.MediaSource, S.HasRemoteMediaConsent,
            ScrolllerPolicy.Enabled, ScrolllerPolicy.Official);
        TxtOnlineOfficialCredit.IsVisible = credit;
        TxtOnlineOfficialCredit.Text = Loc.Get("online_official_stamp") + "  " + Loc.Get("online_official_stamped_line");
        TxtOnlineOfficialText.Inlines!.Clear();
        TxtOnlineOfficialText.Inlines.Add(new Run(Loc.Get("online_official_title")) { FontSize = 14, FontWeight = FontWeight.SemiBold });
        TxtOnlineOfficialText.Inlines.Add(new Run("   " + Loc.Get("online_official_body") + " "));
        TxtOnlineOfficialText.Inlines.Add(new Run(Loc.Get(OnlineOfficialNoteRules.PlanKey(ScrolllerAllowance.Plan)))
            { Foreground = Brush.Parse("#E0B0FF") });
        OnlineOfficialNote.IsVisible = credit && !S.ScrolllerOfficialNoteSeen;
        if (!OnlineOfficialNote.IsEffectivelyVisible || !IsLoaded || _officialNoteLanded) return;
        _officialNoteLanded = true;
        LandOfficialNote();
    }

    private async void LandOfficialNote()
    {
        if (S.MotionLevel == MotionLevel.Off) return;
        _officialLanding?.Cancel();
        _officialLanding = new CancellationTokenSource();
        // Animate the Visual, never a Transform. A finite entrance does not keep composition awake.
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(GentleVisuals.RiseMs(420)),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters =
                {
                    new Setter(OpacityProperty, 0.2),
                    new Setter(RenderTransformProperty, TransformOperations.Parse(
                        S.MotionLevel == MotionLevel.Full && !GentleVisuals.On ? "translate(0px, -6px)" : "translate(0px, 0px)")),
                } },
                new KeyFrame { Cue = new Cue(1), Setters =
                {
                    new Setter(OpacityProperty, 1.0),
                    new Setter(RenderTransformProperty, TransformOperations.Parse("translate(0px, 0px)")),
                } },
            },
        };
        try { await animation.RunAsync(OnlineOfficialNote, _officialLanding.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Official note landing failed"); }
    }
}
