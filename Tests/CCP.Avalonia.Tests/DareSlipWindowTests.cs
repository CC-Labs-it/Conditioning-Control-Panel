using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Services.Dares;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DareSlipWindowTests
{
    private static void Init()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont().With(AppFonts.Options())
                .ConfigureFonts(AppFonts.Register).UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Control? Find(Control root, string tag) => root.GetLogicalDescendants().OfType<Control>().FirstOrDefault(c => Equals(c.Tag, tag));
    private static void Click(Control root, string tag) => Assert.IsType<Button>(Find(root, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task IntroThenComposeAndSendWritingSlip() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var service = new FakeService();
        var marked = 0;
        var friend = new DareFriend("friend", "Friend", null);
        var window = new DareSlipWindow(service, null, new[] { friend }, null, false, () => marked++);
        try
        {
            Assert.NotNull(Find(window, "dares-intro"));
            Click(window, "dares-intro-go");
            Assert.Equal(1, marked);
            Click(window, "dares-who:friend");
            Click(window, "dares-tile:duel");
            Click(window, "dares-tile:bubbles");
            Click(window, "dares-when:today");
            Click(window, "dares-stake:sp");
            var amount = DareGrammar.AmountsFor(DareStakeKind.Sp)[0];
            Click(window, "dares-amount:" + amount);
            Click(window, "dares-stake:sp");
            Click(window, "dares-amount:" + amount);
            Assert.NotNull(Find(window, "dares-send"));
            var result = await window.SendAsync();
            Assert.Equal(DareCreateStatus.Writing, result!.Status);
            Assert.Equal("friend", service.To);
            Assert.Equal(DareKind.Bubbles, service.Slip!.What.Kind);
            Assert.NotNull(Find(window, "dares-write-line"));
        }
        finally { window.Close(); }
    });

    private sealed class FakeService : IDaresService
    {
        public event Action? Changed { add { } remove { } }
        public DaresSnapshot Snapshot => new(true, new DaresMe(null, true, true, 0), Array.Empty<DareBet>());
        public bool IsPreview => false;
        public string? To { get; private set; }
        public DareSlip? Slip { get; private set; }
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<DareCreateOutcome> CreateAsync(string friendId, DareSlip slip, CancellationToken ct = default)
        { To = friendId; Slip = slip; return Task.FromResult(new DareCreateOutcome(DareCreateStatus.Writing, WriteCode: "ABC")); }
        public Task<DareActResult> ActAsync(string betId, DareOp op, CancellationToken ct = default) => Task.FromResult(DareActResult.Done);
    }
}
