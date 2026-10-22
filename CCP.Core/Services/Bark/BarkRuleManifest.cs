using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>
    /// WPF 7.1.5 <c>Services/Bark/BarkRuleLoader.cs</c> over Core's content ladder: the shipped base manifest,
    /// then the active mod's overlay merged FIELD BY FIELD per rule id (arrays replaced, nulls ignored), so a
    /// skin shipping only { id, variant_pool } inherits the rest. Never throws. Named apart from the WPF loader.
    /// </summary>
    public static class BarkRuleManifest
    {
        private static readonly JsonMergeSettings MergeSettings = new()
        {
            MergeArrayHandling = MergeArrayHandling.Replace,       // a skin owns its variant_pool outright
            MergeNullValueHandling = MergeNullValueHandling.Ignore // an explicit null never blanks a base field
        };

        public static BarkRuleSet Load()
        {
            var basePath = ContentLocator.Resolve(Path.Combine(
                CompanionContentResolver.CompanionAudioRelativeDir, CompanionContentResolver.BarkRulesFileName));
            var mod = ModCompanionContent.ResolveActive(CompanionChannel.BarkRules);
            return Load(basePath, mod.Found ? mod.Path : null, CompanionContentResolver.Describe(mod.Source));
        }

        internal static BarkRuleSet Load(string basePath, string? modPath, string modSource = "test")
        {
            var merged = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            int baseCount = MergeFile(merged, basePath, "embedded");
            int modCount = modPath == null ? 0 : MergeFile(merged, modPath, "mod");

            var rules = new List<BarkRule>();
            foreach (var obj in merged.Values)
            {
                try { if (obj.ToObject<BarkRule>() is { } rule && rule.IsValid()) rules.Add(rule); else Log.Warning("BarkRuleManifest: invalid rule after merge"); }
                catch (Exception ex) { Log.Warning(ex, "BarkRuleManifest: failed to materialize a merged rule"); }
            }
            Log.Information("BarkRuleManifest: loaded {Total} rules ({Base} base, {Mod} mod-overlay from {Source}; field-level merge)",
                rules.Count, baseCount, modCount, modSource);
            return new BarkRuleSet(rules);
        }

        private static int MergeFile(Dictionary<string, JObject> merged, string path, string source)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return 0;
                int n = 0;
                foreach (var token in JArray.Parse(json))
                {
                    if (token is not JObject obj) continue;
                    var id = obj.Value<string>("id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        Log.Warning("BarkRuleManifest: skipped a rule with no id in {Source} manifest", source);
                        continue;
                    }
                    if (merged.TryGetValue(id, out var existing)) existing.Merge(obj, MergeSettings);
                    else merged[id] = obj;
                    n++;
                }
                return n;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BarkRuleManifest: failed to read {Source} manifest at {Path}", source, path);
                return 0;
            }
        }
    }
}
