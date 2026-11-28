using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Glassroad;

// ============================ the player's own pictures ("My files", 2026-11-24) ============================
//
// The race's "My files" used to be a browser file picker only, picked again for every course and launch, so a
// player on local media saw blank world screens. The init frame now says caps.ownMedia.library, the page asks
// own-media-list once, and this answers own-media with the ticked library (GlassroadOwnMediaRules). Local files
// need no remote consent and never go online; the Scrolller gate (Pictures.cs) is untouched.
internal static partial class GlassroadHostService
{
    private static bool _ownListInFlight;

    /// <summary>page -&gt; host <c>own-media-list</c>: the active library, walked off the UI thread and posted to the
    /// window that asked. One walk at a time.</summary>
    private static void OnOwnMediaList()
    {
        if (_host == null || _ownListInFlight) return;
        _ownListInFlight = true;
        var asked = _host;
        _ = Task.Run(ReadOwnLibrary).ContinueWith(t => RunOnUi(() =>
        {
            _ownListInFlight = false;
            if (t.Status != TaskStatus.RanToCompletion || !ReferenceEquals(asked, _host)) return;
            App.Logger?.Information("Glassroad: own library answered with {N} files", t.Result.Count);
            try { _core?.PostWebMessageAsJson(GlassroadOwnMediaRules.Frame(t.Result).ToString(Newtonsoft.Json.Formatting.None)); }
            catch (Exception ex) { App.Logger?.Debug("Glassroad: own library post failed: {E}", ex.Message); }
        }), TaskScheduler.Default);
    }

    /// <summary>The same active walk DtRH, chess and Goon read. Counts only are ever logged.</summary>
    private static JArray ReadOwnLibrary()
    {
        var sources = new List<GlassroadOwnMediaRules.Source>();
        string root = "";
        try
        {
            root = App.EffectiveAssetsPath;
            foreach (var (full, _, _, isImage) in DtrhAssetManifest.EnumerateActive())
                sources.Add(new GlassroadOwnMediaRules.Source(full, isImage));
        }
        catch (Exception ex) { App.Logger?.Debug("Glassroad: own library listing failed ({Type})", ex.GetType().Name); }
        return GlassroadOwnMediaRules.Rows(sources, root, new Random());
    }
}
