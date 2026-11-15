using ConditioningControlPanel.Services.Bark;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Chaos
{
    /// <summary>Page bark events and their WPF BarkService contexts (DtrhHostService.cs:729-786).</summary>
    public static class DtrhBarkRoute
    {
        public static BarkContext? Route(JObject o)
        {
            string S(string key) => (string?)o[key] ?? "";
            double N(string key) => (double?)o[key] ?? 0;
            double I(string key) => (int?)o[key] ?? 0;
            var name = S("event");
            var trigger = name switch
            {
                "ending-soon" => "ChaosEndingSoon", "wave-cleared" => "ChaosWaveCleared",
                "wave-escalated" => "ChaosWaveEscalated", "act-changed" => "ChaosActChanged",
                "benign-popped" => "ChaosBenignPopped", "defused" => "ChaosBubbleDefused",
                "detonated" => "ChaosBubbleDetonated", "detonated-absorbed" => "ChaosBubbleDetonatedAbsorbed",
                "darter-caught" or "rabbit-caught" => "ChaosDarterCaught", "freeze-caught" => "ChaosFreezeCaught",
                "combo-milestone" => "ChaosComboMilestone", "combo-big" => "ChaosComboBig",
                "boon-picked" => "ChaosBoonPicked", "curse-picked" => "ChaosCursePicked",
                "boon-skipped" => "ChaosBoonSkipped", "draft-autopick" => "ChaosDraftAutopick",
                "focus-low" => "ChaosFocusLow", "defuse-first" => "ChaosDefuseFirst",
                "defuse-nofocus" => "ChaosDefuseNoFocus", "defuse-release" => "ChaosDefuseRelease",
                "click-detonate" => "ChaosClickDetonate", "tease-debut" => "ChaosTeaseDebut",
                "tease-clicked" => "ChaosTeaseClicked", "tease-denied" => "ChaosTeaseDenied",
                "tease-denied-streak" => "ChaosTeaseDeniedStreak", "gold-first" => "ChaosGoldFirst",
                "dollhouse-first-open" => "ChaosDollhouseFirstOpen", "reveal-flash" => "ChaosRevealFlash",
                "lesson-complete" => "ChaosLessonComplete", "duo-demo" => "ChaosDuoDemo",
                "crafted" => "ChaosFirstTime", _ => null
            };
            if (trigger == null) return null;
            var c = new BarkContext(trigger);
            switch (name)
            {
                case "wave-cleared": case "wave-escalated": c.Set("wave", I("wave")); break;
                case "act-changed": c.Set("act", I("act")).Set("wave", I("wave")); break;
                case "benign-popped": c.Set("variant_id", S("variant")).Set("payload", S("payload")).Set("combo", I("combo")); break;
                case "defused": c.Set("combo", I("combo")).Set("payload", S("variant")).Set("difficulty", S("difficulty")); break;
                case "detonated": case "detonated-absorbed":
                    c.Set("payload", S("variant")).Set("strength", N("strength"))
                        .Set("shield_absorbed", name == "detonated-absorbed").Set("run_detonations", N("runDetonations"))
                        .Set("combo", I("combo")).Set("difficulty", S("difficulty"));
                    if (name == "detonated-absorbed") c.Set("shields_left", I("shields"));
                    break;
                case "darter-caught": c.Set("points", N("points")).Set("combo", I("combo")).Set("quick", (bool?)o["quick"] ?? false); break;
                case "rabbit-caught": c.Set("points", I("gold")).Set("combo", 0d).Set("quick", true); break;
                case "freeze-caught": c.Set("points", N("points")).Set("combo", I("combo")); break;
                case "combo-milestone": c.Set("combo", I("combo")).Set("difficulty", S("difficulty")); break;
                case "combo-big": c.Set("combo", I("combo")).Set("threshold", N("threshold")); break;
                case "boon-picked": c.Set("boon", S("name")); break;
                case "curse-picked": c.Set("boon", S("name")).Set("rarity", S("rarity")).Set("run_mult_bonus", N("mult")); break;
                case "boon-skipped": c.Set("shields_now", I("shields")); break;
                case "tease-denied": case "tease-denied-streak": c.Set("denied_count", I("count")); break;
                case "reveal-flash": c.Set("element", S("id")); break;
                case "lesson-complete": c.Set("lesson_id", S("id")); break;
                case "crafted": c.Set("bonus_id", S("id")); break;
            }
            return c;
        }
    }
}
