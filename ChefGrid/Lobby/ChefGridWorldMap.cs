using HarmonyLib;
using System.Collections.Generic;

namespace OC2DIYChef
{
    // Replace the native Change Chef screen's carousel with the shared grid.
    // Keep its customiser, input mapping, avatar transaction and root-menu stack.
    [HarmonyPatch]
    internal static class ChefGridWorldMap
    {
        static readonly Dictionary<InGameCustomisation, ChefGridLobby> owners =
            new Dictionary<InGameCustomisation, ChefGridLobby>();

        [HarmonyPostfix, HarmonyPatch(typeof(InGameCustomisation), "Show")]
        static void Show(InGameCustomisation __instance, bool __result,
            UIPlayerRootMenu ___m_uiPlayerRoot, FrontendChefCustomisation[] ___m_chefCustomisation)
        {
            if (!__result || !ChefGridMenu.Enabled || ___m_uiPlayerRoot == null) return;
            ChefGridMenu.Show(__instance, ___m_chefCustomisation, __instance.Close);
            var grid = __instance.GetComponent<ChefGridMenu>();
            if (grid == null || !grid.IsOpen) return;
            var owner = ChefGridLobby.Attach(___m_uiPlayerRoot);
            owners[__instance] = owner;
            owner.BeginInput(___m_chefCustomisation);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(InGameCustomisation), "Update")]
        static bool Update(InGameCustomisation __instance)
        {
            if (!owners.ContainsKey(__instance)) return true;
            var grid = __instance.GetComponent<ChefGridMenu>();
            if (grid != null && grid.IsOpen) grid.Tick();
            if (__instance.isActiveAndEnabled && (grid == null || !grid.IsOpen)) __instance.Hide();
            return false;
        }

        internal static void Commit(InGameCustomisation menu)
        {
            ChefGridLobby owner;
            if (!owners.TryGetValue(menu, out owner)) { menu.Hide(); return; }
            // Hide first performs the game's normal commit and input restoration.
            // Release the shared owner, then close the actual native pause flow.
            if (menu.Hide())
            {
                owner.Committed();
                var flow = T17InGameFlow.Instance;
                if (flow != null) flow.ClosePauseMenu();
            }
        }

        static void Release(InGameCustomisation menu, ChefCustomiser customiser)
        {
            var grid = menu.GetComponent<ChefGridMenu>();
            if (grid != null) grid.CloseGrid();
            ChefGridLobby owner;
            if (owners.TryGetValue(menu, out owner))
            {
                owners.Remove(menu);
                if (owner != null) owner.EndInput();
            }
            ChefSelectionMenu.Release(customiser);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(InGameCustomisation), "Hide")]
        static void Hide(InGameCustomisation __instance, bool __result, ChefCustomiser ___m_customiser)
        {
            if (__result) Release(__instance, ___m_customiser);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(InGameCustomisation), "OnDestroy")]
        static void Destroy(InGameCustomisation __instance, ChefCustomiser ___m_customiser)
        {
            Release(__instance, ___m_customiser);
        }
    }
}
