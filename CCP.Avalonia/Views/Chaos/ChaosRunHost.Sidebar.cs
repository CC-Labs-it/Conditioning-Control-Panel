using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    internal static partial class ChaosRunHost
    {
        private static ChaosHudWindow? _preHud;
        private static ChaosRunState? _preState;
        private static ChaosHubWindow? _preHub;
        internal static ChaosHudWindow? PreHud => _preHud;
        internal static ChaosRunState? PreState => _preState;

        // WPF ChaosModeService.cs:586-720: the Warren owns a separate preview until a run starts.
        internal static void ShowLoadoutSidebar(ChaosHubWindow hub)
        {
            if (_active || _preHud != null) return;
            _preHub = hub;
            _preState = new ChaosRunState(ChaosRunConfig.FromSettings());
            _preHud = new ChaosHudWindow(_preState);
            var hud = _preHud;
            hud.Closed += (_, _) => { if (ReferenceEquals(_preHud, hud)) { _preHud = null; CloseLoadoutSidebar(); } };
            ChaosMeta.LoadoutChanged += RefreshSidebarLoadout;
            RefreshSidebarLoadout();
            _preHud.SetHeroMode(true);
            _preHud.SetPreRunExpanded(true);
            _preHud.Show();
        }

        internal static void CloseLoadoutSidebar()
        {
            ChaosMeta.LoadoutChanged -= RefreshSidebarLoadout;
            var hud = _preHud;
            _preHud = null;
            _preState = null;
            _preHub = null;
            hud?.Close();
        }

        // WPF ChaosModeService.cs:619-659: these commands belong to the Warren preview.
        internal static void SidebarPocketClicked(ChaosHudWindow hud, string? id)
        {
            if (!ReferenceEquals(_preHud, hud)) return;
            if (string.IsNullOrEmpty(id)) { _preHub?.NavigateTo("enhance"); return; }
            if (!ChaosMeta.IsBoonActive(id)) return;
            ChaosMeta.SetBoonActive(id, false);
            _preState?.PushEvent(Loc.GetF("chaos_sidebar_left_behind", ChaosLifetimeBoons.ById(id)?.Name ?? id));
            _preHub?.RefreshAfterExternalLoadoutChange();
        }

        internal static void SidebarFallIn(ChaosHudWindow hud)
        {
            if (!ReferenceEquals(_preHud, hud)) return;
            if (_preHub != null) _preHub.FallIn();
            else StartRun();
        }

        internal static void SidebarClose(ChaosHudWindow hud)
        {
            if (!ReferenceEquals(_preHud, hud)) return;
            if (_preHub != null) _preHub.Close();
            else CloseLoadoutSidebar();
        }

        private static void RefreshSidebarLoadout()
        {
            var st = _preState;
            if (st == null) return;
            st.ActiveSidebarToys.Clear();
            st.ActiveSidebarAccessories.Clear();
            st.RunModifiers.Clear();
            foreach (var cat in new[] { ChaosBoonCategory.Skill, ChaosBoonCategory.Accessory, ChaosBoonCategory.Utility })
            {
                var group = cat == ChaosBoonCategory.Skill ? st.ActiveSidebarToys : cat == ChaosBoonCategory.Accessory ? st.ActiveSidebarAccessories : st.RunModifiers;
                foreach (var b in ChaosLifetimeBoons.InCategory(cat))
                {
                    if (!ChaosMeta.IsBoonActive(b.Id)) continue;
                    int level = ChaosMeta.BoonLevel(b.Id);
                    group.Add(new ChaosSidebarBoon { Id = b.Id, Icon = ChaosBoonArt.Resolve("boons", b.Id), Glyph = b.Glyph,
                        Name = cat == ChaosBoonCategory.Utility ? $"{b.Name} · L{level}" : b.Name, Level = level,
                        Desc = b.Desc, Flavor = b.Flavor, Extra = level >= b.MaxLevel && !string.IsNullOrEmpty(b.CapstoneDesc) ? "max: " + b.CapstoneDesc : "", IsModifier = cat == ChaosBoonCategory.Utility });
                }
                if (cat != ChaosBoonCategory.Utility)
                    for (int i = group.Count; i < ChaosMeta.SlotsFor(cat); i++)
                        group.Add(new ChaosSidebarBoon { IsEmptySlot = true, Glyph = "+", Name = Loc.Get(cat == ChaosBoonCategory.Skill ? "chaos_sidebar_empty_toy" : "chaos_sidebar_empty_accessory"), Desc = Loc.Get("chaos_sidebar_empty_desc") });
            }
            foreach (var id in ChaosMeta.State.PurchasedUpgrades)
            {
                if (!ChaosMeta.IsUpgradeActive(id)) continue;
                var u = ChaosUpgrades.ById(id);
                if (u == null) continue;
                st.RunModifiers.Add(new ChaosSidebarBoon { Icon = u.IconPath != null ? ChaosArt.TryLoad(u.IconPath) : ChaosBoonArt.Resolve("upgrades", u.Id), Glyph = u.Glyph, Name = u.Name, Desc = u.Desc, Flavor = u.Flavor, IsModifier = true });
            }
        }
    }
}
