using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    internal static partial class ChaosRunHost
    {
        private static ChaosHudWindow? _preHud;
        private static ChaosRunState? _preState;
        internal static ChaosHudWindow? PreHud => _preHud;
        internal static ChaosRunState? PreState => _preState;

        // WPF ChaosModeService.cs:586-720: the Warren owns a separate preview until a run starts.
        internal static void ShowLoadoutSidebar()
        {
            if (_active || _preHud != null) return;
            _preState = new ChaosRunState(ChaosRunConfig.FromSettings());
            _preHud = new ChaosHudWindow(_preState);
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
            hud?.Close();
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
                        group.Add(new ChaosSidebarBoon { IsEmptySlot = true, Glyph = "+", Name = cat == ChaosBoonCategory.Skill ? "empty toy pocket" : "empty accessory pocket", Desc = "click to go shopping in the dollhouse." });
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
