using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OC2DIYChef
{
    // Diagnostic build: observe IsDown and private state only. Never consume an
    // input edge, release someone else's suppressor, or invoke a cancel action.
    [HarmonyPatch]
    internal static class FrontendCancelTrace
    {
        sealed class Probe
        {
            internal bool Down, BoundDown, Report;
            internal int FollowUntil = -1;
            internal string Signature;
            internal GameInputConfig Config;
            internal readonly List<ILogicalButton> Physical = new List<ILogicalButton>();
        }
        static readonly Dictionary<FrontendRootMenu, Probe> probes = new Dictionary<FrontendRootMenu, Probe>();
        static bool failed;

        static object Read(object value, string field)
        {
            if (value == null) return null;
            var type = value as Type ?? value.GetType();
            var info = AccessTools.Field(type, field);
            return info == null ? null : info.GetValue(value is Type ? null : value);
        }

        static string Bit(object value) { return value == null ? "?" : value.ToString(); }
        static string Name(GameObject value) { return value == null ? "null" : value.name; }
        static FrontendRootMenu Root
        {
            get { return T17FrontendFlow.Instance == null ? null : T17FrontendFlow.Instance.m_Rootmenu; }
        }

        static void Menu(StringBuilder text, BaseMenuBehaviour menu)
        {
            if (menu == null) { text.Append("null"); return; }
            text.Append(menu.gameObject.name).Append('[').Append(menu.GetType().Name)
                .Append(" active=").Append(menu.gameObject.activeInHierarchy)
                .Append(" enabled=").Append(menu.enabled)
                .Append(" allowCancel=").Append(Bit(Read(menu, "m_bAllowCancelHandeling")))
                .Append(" shownThisFrame=").Append(Bit(Read(menu, "m_bShownThisFrame")))
                .Append(" gamer=").Append(menu.CurrentGamepadUser != null)
                .Append(" cancelComponent=").Append(menu.NavigateOnUICancel != null).Append(']');
        }

        static void Snapshot(FrontendRootMenu root, string reason)
        {
            if (root == null || failed) return;
            try
            {
                var button = Read(root, "m_uiCancelButton") as ILogicalButton;
                var text = new StringBuilder("[CancelTrace] frame=").Append(Time.frameCount).Append(' ').Append(reason);
                text.Append(" focus=").Append(Application.isFocused)
                    .Append(" dialogs=").Append(T17DialogBoxManager.HasAnyOpenDialogs())
                    .Append(" cameraTransition=").Append(T17FrontendFlow.Instance != null && T17FrontendFlow.Instance.IsCameraTransitioning())
                    .Append(" grid=").Append(ChefGridMenu.Active != null)
                    .Append(" lobbyBlocking=").Append(ChefGridLobby.Blocking)
                    .Append(" engagementOpen=").Append(Bit(Read(typeof(ChefGridEngagementGuard), "open")))
                    .Append(" engagementPending=").Append(Bit(Read(typeof(ChefGridEngagementGuard), "pending")))
                    .Append(" cancelBound=").Append(button != null)
                    .Append(" cancelDown=").Append(button != null && button.IsDown())
                    .Append(" pressClaimed=").Append(Bit(Read(button, "m_pressClaimed")))
                    .Append(" releaseClaimed=").Append(Bit(Read(button, "m_releaseClaimed")))
                    .Append(" cancelHandlers=").Append(BaseMenuBehaviour.AllowedCancelHandlers)
                    .Append(" inputConfig=").Append(PlayerInputLookup.GetInputConfig() == null ? "null" : PlayerInputLookup.GetInputConfig().GetHashCode().ToString())
                    .Append(" root=");
                Menu(text, root);
                var domain = root.CachedEventSystem;
                text.Append(" domain=").Append(domain != null)
                    .Append(" disabled=").Append(domain != null && domain.IsDisabled());
                if (domain != null)
                    text.Append(" selected=").Append(Name(domain.currentSelectedGameObject))
                        .Append(" pending=").Append(Name(domain.GetPendingSelectedGameObject()));
                text.Append(" stack={");
                var stack = Read(root, "m_FrontendMenuStack") as List<BaseMenuBehaviour>;
                if (stack != null) foreach (var menu in stack) { Menu(text, menu); text.Append(';'); }
                text.Append("} active={");
                if (root.m_FrontEndTabableMenuTypes != null)
                    foreach (var group in root.m_FrontEndTabableMenuTypes.Values)
                        foreach (var menu in group.m_Menus)
                            if (menu != null && menu.gameObject.activeInHierarchy) { Menu(text, menu); text.Append(';'); }
                text.Append('}');
                DIYChefPlugin.Log(text.ToString());
            }
            catch (Exception error)
            {
                failed = true;
                DIYChefPlugin.Warn("[CancelTrace] observation disabled: " + error);
            }
        }

        static bool Physical(Probe probe)
        {
            var users = Team17.Online.ClientUserSystem.m_Users;
            var signature = new StringBuilder();
            for (int i = 0; i < users.Count; i++)
            {
                var user = users._items[i];
                if (user.IsLocal) signature.Append((int)user.Engagement).Append(':').Append(user.PadSide).Append(';');
            }
            var config = PlayerInputLookup.GetInputConfig();
            string identity = signature.ToString();
            if (probe.Signature != identity || probe.Config != config)
            {
                probe.Signature = identity; probe.Config = config; probe.Physical.Clear();
                var manager = GameUtils.RequireManager<PlayerManager>();
                for (int i = 0; i < users.Count; i++)
                {
                    var user = users._items[i];
                    if (!user.IsLocal) continue;
                    var input = new PlayerGameInput((ControlPadInput.PadNum)user.Engagement, user.PadSide,
                        user.PadSide == PadSide.Both ? manager.UnsidedAmbiMapping : manager.SidedAmbiMapping);
                    probe.Physical.Add(PlayerInputLookup.GetFixedButton(PlayerInputLookup.LogicalButtonID.UICancel, input));
                }
            }
            bool down = Input.GetKey(KeyCode.Escape);
            foreach (var button in probe.Physical) down |= button != null && button.IsDown();
            return down;
        }

        internal static void Observe()
        {
            var root = Root;
            if (root == null || !root.gameObject.activeInHierarchy) return;
            BeforeUpdate(root);
            AfterUpdate(root);
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(FrontendRootMenu), "Update")]
        static void BeforeUpdate(FrontendRootMenu __instance)
        {
            if (failed) return;
            try
            {
                Probe probe;
                if (!probes.TryGetValue(__instance, out probe))
                {
                    probes[__instance] = probe = new Probe();
                    Snapshot(__instance, "root-initial");
                }
                var button = Read(__instance, "m_uiCancelButton") as ILogicalButton;
                bool down = Physical(probe), bound = button != null && button.IsDown();
                bool edge = down != probe.Down || bound != probe.BoundDown;
                if (edge) probe.FollowUntil = Time.frameCount + 3;
                probe.Report = edge || Time.frameCount <= probe.FollowUntil;
                if (probe.Report) Snapshot(__instance, "before-root-update physical=" + down + " native=" + bound);
                probe.Down = down; probe.BoundDown = bound;
            }
            catch (Exception error) { failed = true; DIYChefPlugin.Warn("[CancelTrace] probe disabled: " + error); }
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(FrontendRootMenu), "Update")]
        static void AfterUpdate(FrontendRootMenu __instance)
        {
            Probe probe;
            if (probes.TryGetValue(__instance, out probe) && probe.Report) Snapshot(__instance, "after-root-update");
        }

        [HarmonyPrefix, HarmonyPatch(typeof(FrontendRootMenu), "DoNavigateOnUICancel")]
        static void Navigate(FrontendRootMenu __instance) { Snapshot(__instance, "DoNavigateOnUICancel-enter"); }

        [HarmonyPostfix, HarmonyPatch(typeof(BaseMenuBehaviour), "InvokeNavigateOnUICancel")]
        static void Invoked(BaseMenuBehaviour __instance, bool __result)
        {
            Probe probe; var root = Root;
            if (root == null || !probes.TryGetValue(root, out probe) || !probe.Report) return;
            Snapshot(root, "InvokeNavigateOnUICancel menu=" + __instance.gameObject.name + " result=" + __result);
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(FrontendChefMenu), "Show")]
        static void ChefShown(bool __result) { Snapshot(Root, "chef-show result=" + __result); }

        [HarmonyPrefix, HarmonyPatch(typeof(FrontendChefMenu), "Hide")]
        static void ChefHiding() { Snapshot(Root, "chef-hide-enter"); }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(FrontendChefMenu), "Hide")]
        static void ChefHidden(bool __result) { Snapshot(Root, "chef-hide-exit result=" + __result); }

        [HarmonyPostfix, HarmonyPatch(typeof(FrontendRootMenu), "OnDestroy")]
        static void Destroy(FrontendRootMenu __instance) { probes.Remove(__instance); }
    }
}
