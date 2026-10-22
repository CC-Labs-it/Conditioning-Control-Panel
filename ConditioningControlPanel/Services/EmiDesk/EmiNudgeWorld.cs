using System;
using System.Windows;
using Serilog;
namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The live world: <see cref="EmiState"/> for the counters and <c>App.EmiDesk</c> for the
/// situation. Every property is wrapped, because a nudge is the least important thing in the app
/// and must never be the thing that throws.
/// </summary>
public sealed class EmiNudgeWorld : IEmiNudgeWorld
{
    /// <inheritdoc/>
    public int PetsTotal { get { try { return EmiState.Current.PetsTotal; } catch { return 0; } } }

    /// <inheritdoc/>
    public bool PetGistGot { get { try { return EmiState.Current.PetGistGot; } catch { return true; } } }

    /// <inheritdoc/>
    public int RingOpens { get { try { return EmiState.Current.RingOpens; } catch { return 0; } } }

    /// <inheritdoc/>
    public bool RingGistGot { get { try { return EmiState.Current.RingGistGot; } catch { return true; } } }

    /// <inheritdoc/>
    public bool PinGistGot { get { try { return EmiState.Current.PinGistGot; } catch { return true; } } }

    /// <inheritdoc/>
    public int FiresOf(string track)
    {
        try { return EmiState.Current.NudgeFires.TryGetValue(track, out int n) ? n : 0; }
        catch { return int.MaxValue; }   // unreadable state means "stop", never "nag"
    }

    /// <inheritdoc/>
    public void NoteFire(string track) => EmiState.NoteNudgeFired(track);

    /// <inheritdoc/>
    public bool Quiet
    {
        get
        {
            try
            {
                var desk = App.EmiDesk;
                if (desk == null || !desk.IsOut) return false;

                // AskSituationOk is already the app's answer to "is anything owning the screen or
                // the user's attention right now": a live ask, a mandatory video, a running
                // session, a tube bubble, a minimised app. Reusing it means the nudge cannot
                // develop its own idea of quiet and drift away from the offers'.
                if (!desk.AskSituationOk()) return false;

                var win = desk.Window;
                if (win == null || win.Visibility != Visibility.Visible) return false;
                if (win.ChainLive) return false;

                // A hold is the safety silence: panic pressed, attention check up, lockdown
                // counting down. Nothing tutorial-shaped speaks through one.
                if (EmiLineEngine.Instance.HoldActive) return false;

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] nudge quiet probe failed");
                return false;
            }
        }
    }
}
