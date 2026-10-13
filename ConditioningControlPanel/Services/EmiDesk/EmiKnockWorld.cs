using System;
using System.Windows;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The live world: <see cref="EmiState"/> for the ledger, <c>AppSettings</c> for the switch, and
/// the app's own flags for the gates. Every property is wrapped, because a knock is the least
/// important thing in the app and must never be the thing that throws.
///
/// <para><see cref="LastSeenVersion"/> is a SNAPSHOT taken by the caller, not a live read.
/// <c>ShowWhatsNewIfNeeded</c> stamps the setting to the current version on the synchronous side
/// of the first-run branch, minutes before the knock's dispatcher item runs; a live read would see
/// the stamp and classify every upgrader as somebody who is owed nothing.</para>
/// </summary>
public sealed class EmiKnockWorld : IEmiKnockWorld
{
    private readonly string _seenVersion;
    private readonly DateTime _launchUtc;

    /// <summary>
    /// Builds the live world. <paramref name="seenVersionSnapshot"/> must be read BEFORE anything
    /// on this launch stamps <c>LastSeenVersion</c> - see the class remarks.
    /// </summary>
    public EmiKnockWorld(string? seenVersionSnapshot, DateTime? launchStartedUtc = null)
    {
        _seenVersion = seenVersionSnapshot ?? string.Empty;
        _launchUtc = launchStartedUtc ?? ProcessStartUtc();
    }

    private static DateTime ProcessStartUtc()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return DateTime.UtcNow.AddMinutes(-5); }
    }

    /// <inheritdoc/>
    public int KnockState { get { try { return EmiState.Current.KnockState; } catch { return EmiKnockMachine.Spent; } } }

    /// <inheritdoc/>
    public long KnockAtUtc { get { try { return EmiState.Current.KnockAtUtc; } catch { return 0; } } }

    /// <inheritdoc/>
    public int KnockOffers { get { try { return EmiState.Current.KnockOffers; } catch { return EmiKnockMachine.OfferCap; } } }

    /// <inheritdoc/>
    public bool TourDone(string tour) => EmiState.HasTourDone(tour);

    /// <inheritdoc/>
    public string? LastSeenVersion => _seenVersion;

    /// <inheritdoc/>
    public string? CurrentVersion
    {
        get { try { return ConditioningControlPanel.Services.UpdateService.AppVersion; } catch { return null; } }
    }

    /// <inheritdoc/>
    public DateTime LaunchStartedUtc => _launchUtc;

    /// <inheritdoc/>
    public bool DeskEnabled
    {
        get { try { return App.Settings?.Current?.EmiDeskEnabled == true; } catch { return false; } }
    }

    /// <inheritdoc/>
    public bool AlreadyOut
    {
        get { try { return App.EmiDesk?.IsOut == true; } catch { return true; } }
    }

    /// <inheritdoc/>
    public bool WizardUp
    {
        get { try { return ConditioningControlPanel.MainWindow.IsStartupDialogShowing; } catch { return true; } }
    }

    /// <inheritdoc/>
    public bool UpdateDialogUp
    {
        get { try { return App.IsUpdateDialogActive; } catch { return true; } }
    }

    /// <inheritdoc/>
    public bool SessionRunning
    {
        get { try { return SessionEngine.Active?.IsRunning == true; } catch { return true; } }
    }

    /// <inheritdoc/>
    public bool TutorialOpen
    {
        get { try { return App.Tutorial?.IsActive == true; } catch { return true; } }
    }

    /// <inheritdoc/>
    public bool WindowUsable
    {
        get
        {
            try
            {
                var main = Application.Current?.MainWindow;
                if (main == null) return false;
                if (!main.IsLoaded) return false;
                if (main.Visibility != Visibility.Visible) return false;
                if (main.WindowState == WindowState.Minimized) return false;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] knock window probe failed");
                return false;
            }
        }
    }
}
