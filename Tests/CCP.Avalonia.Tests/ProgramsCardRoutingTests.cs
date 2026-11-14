using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ProgramsCardRoutingTests
{
    // No window or text rendering: this also runs on the cloud runtime without system fonts.
    [Fact]
    public void LockedCardsOfferAccountWhileUnfinishableProgramsKeepTheirReason() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => false;
            var rows = MainShellWindow.BuildProgramBrowseItems(BuiltInPrograms.All());
            var locked = rows.Where(r => r.IsLocked).ToArray();
            Assert.NotEmpty(locked);
            Assert.All(locked, r =>
            {
                Assert.True(r.IsActionEnabled, r.ProgramId);
                Assert.Equal(1.0, r.ActionOpacity);
                Assert.Equal(Loc.Get("btn_program_locked"), r.ActionText);
                Assert.True(r.ReasonVisible);
                Assert.Equal(ProgramService.UnavailableReason(r.Definition, ProgramCapabilities.IsAvailable)
                    ?? Loc.Get("programs_locked_hint"), r.ReasonText);
            });
            Assert.All(rows.Where(r => !r.IsLocked), r => Assert.False(r.IsActionEnabled));

            // Owning premium never bypasses the service/capability checks to enroll.
            CoreEntitlement.HasPremiumProvider = () => true;
            rows = MainShellWindow.BuildProgramBrowseItems(BuiltInPrograms.All());
            Assert.All(rows, r => { Assert.False(r.IsLocked); Assert.False(r.IsActionEnabled); });
        }
        finally { CoreEntitlement.HasPremiumProvider = oldPremium; }
    });
}
