using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

/// <summary>WPF LeashHolderCard SentSection and confirmed release, hosted on Social > Leash.</summary>
internal sealed class LeashReceiptCard : Border
{
    internal Button ReleaseButton { get; }

    internal LeashReceiptCard(HeldLeash held, ILeashService service)
    {
        // ponytail: only the receipt/release portion; holder figures and send sheets remain feat-leash.
        Tag = "leash-receipt-card:" + held.Who.Id;
        Background = F.Raised;
        BorderBrush = F.Gold;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12);
        Margin = new Thickness(0, 4, 0, 6);
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(F.Label(held.Who.Name, 16, F.Gold, F.Display));
        if (LeashSentList.For(service, held.Who.Id) is { } sent) body.Children.Add(sent);
        ReleaseButton = F.Pill(Loc.GetF("leash_menu_release", held.Who.Name), F.Raised, F.Text, "leash-menu-release", F.Line2);
        ReleaseButton.Click += (_, _) => LeashCutConfirmWindow.Confirm(
            Loc.GetF("leash_release_confirm_title", held.Who.Name),
            Loc.GetF("leash_release_confirm_body", held.Who.Name),
            Loc.Get("leash_release_confirm_yes"), Loc.Get("leash_cut_confirm_no"), "leash-release-confirm",
            () => _ = ReleaseAsync(service, held));
        body.Children.Add(ReleaseButton);
        Child = body;
    }

    private static async Task ReleaseAsync(ILeashService service, HeldLeash held)
    {
        bool ok = false;
        try { ok = await service.ReleaseAsync(held.Who.Id); }
        catch (Exception ex) { Serilog.Log.Debug("Leash release failed: {E}", ex.Message); }
        if (ok) LeashSound.Cut(); else LeashSound.Denied();
        App.Notifications.Show(Loc.GetF(ok ? "leash_release_done" : "leash_release_failed", held.Who.Name),
            ok ? Helpers.NotificationType.Info : Helpers.NotificationType.Warning);
    }
}
