using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>Social > Leash. The receipts/release portion of the holder card is live here.</summary>
public partial class LeashTabView : UserControl
{
    private ILeashService? _service;

    public LeashTabView()
    {
        InitializeComponent();
        EmptyButton.Click += (_, _) => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("friends");
        AttachedToVisualTree += (_, _) => BindReceipts(LeashHead.Service);
        DetachedFromVisualTree += (_, _) => BindReceipts(null);
    }

    internal void BindReceipts(ILeashService? service)
    {
        if (_service != null)
        {
            _service.SnapshotChanged -= SnapshotChanged;
            _service.ReceiptsChanged -= RefreshReceipts;
        }
        _service = service;
        if (service != null)
        {
            service.SnapshotChanged += SnapshotChanged;
            service.ReceiptsChanged += RefreshReceipts;
        }
        RefreshReceipts();
    }

    private void SnapshotChanged(LeashSnapshot _) => RefreshReceipts();

    private void RefreshReceipts()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshReceipts); return; }
        ReceiptsHost.Children.Clear();
        if (_service is { } service)
            foreach (var held in service.Snapshot.Holding)
                ReceiptsHost.Children.Add(new LeashReceiptCard(held, service));
        EmptyPanel.IsVisible = ReceiptsHost.Children.Count == 0;
    }

    internal bool ShowingEmpty => EmptyPanel.IsVisible;
}
