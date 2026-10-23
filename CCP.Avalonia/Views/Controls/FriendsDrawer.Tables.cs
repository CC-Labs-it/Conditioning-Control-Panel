// PORTED from ConditioningControlPanel/Controls/Friends/FriendsDrawer.Tables.cs: a friend with a listed Goon
// Game table floats to the top with a pink "hosting a table" row and a Join button (signed in) or a lock
// (signed out). The list is Core GoonOpenTables, asked on open and every TablesPoll while the drawer is open.
// ponytail: this head has no Goon Game host (GoonHostService is WPF-only), so a Join press words
// "exclusives_not_on_this_build" in the row instead of launching; set JoinTable when a Goon host lands.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    internal static readonly TimeSpan TablesPoll = TimeSpan.FromSeconds(30);

    private DispatcherTimer? _tablesTimer;
    private bool _tablesHooked;

    /// <summary>Test seam: the open tables the drawer draws from.</summary>
    internal Func<OpenTablesReply> Tables { get; set; } = () => GoonOpenTables.Latest;

    /// <summary>Test seam: "may this account sit down" (WPF GoonHostService.JoiningAllowed: signed in). Fails closed.</summary>
    internal Func<bool> CanJoinTables { get; set; } = () =>
    {
        try { return !string.IsNullOrEmpty(CoreAccount.UnifiedUserId); } catch { return false; }
    };

    /// <summary>What a Join press does once the bar is cleared (WPF GoonHostService.Launch with the code).
    /// Null on this head: there is no Goon Game host to sit down in.</summary>
    internal Action<string>? JoinTable { get; set; }

    private void StartTables()
    {
        if (!_tablesHooked) { GoonOpenTables.Changed += OnTablesChanged; _tablesHooked = true; }
        _ = GoonOpenTables.RefreshAsync();
        if (_tablesTimer == null)
        {
            _tablesTimer = new DispatcherTimer { Interval = TablesPoll };
            _tablesTimer.Tick += (_, _) => _ = GoonOpenTables.RefreshAsync();
        }
        _tablesTimer.Start();
    }

    private void StopTables()
    {
        _tablesTimer?.Stop();
        if (_tablesHooked) { GoonOpenTables.Changed -= OnTablesChanged; _tablesHooked = false; }
    }

    internal bool TablesPolling => _tablesTimer?.IsEnabled == true && _tablesHooked;

    private void OnTablesChanged(OpenTablesReply _) => Dispatcher.UIThread.Post(Render);

    /// <summary>The table this friend is hosting, or null.</summary>
    internal OpenTable? TableFor(Friend f)
    {
        try { return GoonOpenTables.ForFriend(Tables(), f.Id, f.Name); }
        catch { return null; }
    }

    /// <summary>The pink wash a hosting row wears (the mockup's <c>.fr.hosting</c>).</summary>
    private static void DressHostingRow(Border row)
    {
        row.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.3, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.7, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x33, 0xFF, 0x5F, 0xA2), 0),
                new GradientStop(Color.FromArgb(0x0A, 0xFF, 0x5F, 0xA2), 1),
            },
        };
        row.BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0x5F, 0xA2));
        row.BorderThickness = new Thickness(1);
    }

    /// <summary>"hosting a table" in pink, bold: replaces the activity line on a hosting row.</summary>
    private static TextBlock HostingLine() =>
        Tagged(Label(Loc.Get("friends_table_hosting"), 11.5, Pink, Display, FontWeight.Bold), "friends-table-hosting");

    /// <summary>Join (signed in) or a lock (signed out). Both run through the same click: the bar is asked
    /// at press time, so a sign-in that lands mid-session works without a repaint.</summary>
    private Button TableJoinButton(Friend f, OpenTable table)
    {
        bool can = CanJoinTables();
        object content = can
            ? new TextBlock { Text = Loc.Get("friends_table_join"), FontFamily = Display, FontSize = 13 }
            : new IconGlyph { Kind = IconKind.LockClosed, Size = 13 };
        var b = can
            ? Pill(content, Pink, new SolidColorBrush(Color.FromRgb(0x2A, 0x06, 0x18)), "friends-table-join", Pink)
            : Pill(content, Raised, Text, "friends-table-locked", Line2);
        b.Padding = can ? new Thickness(12, 4, 12, 4) : new Thickness(10, 4, 10, 4);
        b.Margin = new Thickness(6, 0, 0, 0);
        var tip = Loc.Get(can ? "friends_table_join_tip" : "friends_table_locked_tip");
        ToolTip.SetTip(b, tip);
        global::Avalonia.Automation.AutomationProperties.SetName(b, can ? Loc.Get("friends_table_join") : tip);
        var code = table.Code;
        b.Click += (_, e) => { e.Handled = true; PressJoin(f, code); };
        return b;
    }

    internal void PressJoin(Friend f, string code)
    {
        try
        {
            if (!CanJoinTables())
            {
                // Joining needs an account and nothing else; the lock's tooltip says so.
                Serilog.Log.Information("[Friends] table join pressed while signed out");
                return;
            }
            if (JoinTable == null)
            {
                ShowTimed(f.Id, Loc.Get("exclusives_not_on_this_build"), good: false);
                return;
            }
            Serilog.Log.Information("[Friends] joining {Name}'s open table", f.Name);
            JoinTable(code);
            CloseRequested?.Invoke();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Friends] table join failed"); }
    }
}
