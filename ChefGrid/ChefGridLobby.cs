using HarmonyLib;
using OC2DIYChef.Extension;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Team17.Online;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    // Input ownership only: entry is in the existing player name menu.
    public sealed class ChefGridLobby : MonoBehaviour
    {
        static readonly FieldInfo state = AccessTools.Field(typeof(ClientLobbyFlowController), "m_state");
        static readonly FieldInfo leave = AccessTools.Field(typeof(ClientLobbyFlowController), "m_leaveButton");
        static readonly FieldInfo localInputs = AccessTools.Field(typeof(ClientLobbyFlowController), "m_localUserInput");
        static readonly FieldInfo changeTeam = AccessTools.Field(
            AccessTools.Inner(typeof(ClientLobbyFlowController), "UserInput"), "m_changeTeamButton");
        static ChefGridLobby current;
        readonly ChefGridLobbySession session = new ChefGridLobbySession();
        readonly List<ILogicalButton> watched = new List<ILogicalButton>();
        readonly List<Suppressor> suppressors = new List<Suppressor>();
        readonly HashSet<T17EventSystem> eventSystems = new HashSet<T17EventSystem>();
        ChefGridLobbyMenu menu;
        internal LobbyFlowController Flow { get; private set; }
        internal static bool Blocking { get { return current != null && current.session.Blocking; } }

        internal static void Attach(LobbyFlowController flow)
        {
            var entry = flow.GetComponent<ChefGridLobby>() ?? flow.gameObject.AddComponent<ChefGridLobby>();
            entry.Flow = flow;
            current = entry;
        }

        internal static bool CanShowFor(User user)
        {
            var flow = LobbyFlowController.Instance;
            var client = ClientLobbyFlowController.Instance;
            if (!ChefGridMenu.Enabled || user == null || user.SelectedChefData == null ||
                ChefGridLobbySelection.FindLocal(user) < 0 || flow == null ||
                flow.m_uiPlayerRoot == null || client == null) return false;
            var value = (LobbyFlowController.LobbyState)state.GetValue(client);
            return value == LobbyFlowController.LobbyState.LocalThemeSelection ||
                value == LobbyFlowController.LobbyState.OnlineThemeSelection || value == LobbyFlowController.LobbyState.Matchmake;
        }

        internal static bool CanOpenFor(User user)
        {
            return CanShowFor(user) && !Blocking && ChefGridMenu.Active == null && Application.isFocused &&
                !T17DialogBoxManager.HasAnyOpenDialogs();
        }

        internal static void OpenFor(UIPlayerMenuBehaviour card)
        {
            if (card == null || !CanOpenFor(card.UserInfo) || card.m_rootMenu != LobbyFlowController.Instance.m_uiPlayerRoot) return;
            Attach(LobbyFlowController.Instance);
            try { current.Open(card); }
            catch (Exception ex)
            {
                if (current.menu != null) current.menu.Hide(false);
                DIYChefPlugin.Warn("Arcade chef menu unavailable: " + ex);
            }
        }

        void Open(UIPlayerMenuBehaviour card)
        {
            var target = ChefGridLobbySelection.Capture(card.UserInfo);
            if (target == null) return;
            if (menu == null)
            {
                var host = new GameObject("DIYChef arcade selection");
                host.SetActive(false);
                host.transform.SetParent(transform, false);
                menu = host.AddComponent<ChefGridLobbyMenu>();
            }
            var gamer = card.UserInfo.GamepadUser;
            if (gamer != null && T17EventSystemsManager.Instance.GetEventSystemForGamepadUser(gamer) == null)
                T17EventSystemsManager.Instance.AssignFreeEventSystemToGamepadUser(gamer);
            Flow.m_uiPlayerRoot.CloseAllPlayerMenus();
            ChefGridPlayerMenu.Hide(card);
            // Return to the name bar instead of the hidden menu item.
            var events = gamer == null ? null : T17EventSystemsManager.Instance.GetEventSystemForGamepadUser(gamer);
            if (events != null && card.MenuButton != null) events.SetSelectedGameObject(card.MenuButton.gameObject);
            if (menu.Open(this, gamer, target)) DIYChefPlugin.Log("Chef grid opened from own player name menu");
        }

        internal void BeginInput(FrontendChefCustomisation[] views)
        {
            session.Begin();
            watched.Clear();
            var manager = GameUtils.RequireManager<PlayerManager>();
            var users = ClientUserSystem.m_Users;
            for (int i = 0; i < users.Count; i++)
            {
                var user = users._items[i];
                if (!user.IsLocal) continue;
                var mapping = user.PadSide == PadSide.Both ? manager.UnsidedAmbiMapping : manager.SidedAmbiMapping;
                var input = new PlayerGameInput((ControlPadInput.PadNum)user.Engagement, user.PadSide, mapping);
                foreach (var id in new[] { PlayerInputLookup.LogicalButtonID.UILeftPlayerSpecific,
                    PlayerInputLookup.LogicalButtonID.UIRightPlayerSpecific, PlayerInputLookup.LogicalButtonID.UIUp,
                    PlayerInputLookup.LogicalButtonID.UIDown, PlayerInputLookup.LogicalButtonID.UISelect,
                    PlayerInputLookup.LogicalButtonID.UICancel, PlayerInputLookup.LogicalButtonID.Curse,
                    PlayerInputLookup.LogicalButtonID.WorkstationInteract, PlayerInputLookup.LogicalButtonID.PlayerSwitch })
                    watched.Add(PlayerInputLookup.GetFixedButton(id, input));
                var gamer = user.GamepadUser;
                var events = gamer == null ? null : T17EventSystemsManager.Instance.GetEventSystemForGamepadUser(gamer);
                if (events != null && eventSystems.Add(events)) suppressors.Add(events.Disable(this));
            }
            ConsumeLobbyButtons(ClientLobbyFlowController.Instance);
        }

        internal void EndInput() { session.End(Time.frameCount); }

        internal static void BeforeLevelLoad()
        {
            if (current != null && current.menu != null) current.menu.Hide(false);
        }

        void LateUpdate()
        {
            if (!session.Blocking) return;
            bool held = Input.GetMouseButton(0);
            foreach (var code in new[] { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Escape, KeyCode.Space,
                KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow }) held |= Input.GetKey(code);
            foreach (var logical in watched) if (logical != null) held |= logical.IsDown();
            session.Advance(Time.frameCount, held);
            if (!session.Blocking) ReleaseInput();
        }

        static void Claim(ILogicalButton logical)
        {
            if (logical == null) return;
            logical.HasUnclaimedPressEvent(); logical.ClaimPressEvent();
            logical.HasUnclaimedReleaseEvent(); logical.ClaimReleaseEvent();
        }

        internal static void ConsumeLobbyButtons(ClientLobbyFlowController client)
        {
            if (!Blocking || client == null) return;
            Claim((ILogicalButton)leave.GetValue(client));
            var inputs = (IEnumerable)localInputs.GetValue(client);
            if (inputs != null) foreach (var input in inputs) Claim((ILogicalButton)changeTeam.GetValue(input));
        }

        void ReleaseInput()
        {
            foreach (var suppression in suppressors) if (suppression != null) suppression.Release();
            suppressors.Clear(); eventSystems.Clear(); watched.Clear();
        }

        internal void Shutdown()
        {
            if (menu != null) { menu.Hide(false); Object.Destroy(menu.gameObject); menu = null; }
            session.Reset(); ReleaseInput();
            if (current == this) current = null;
        }

        void OnDestroy() { Shutdown(); }
    }

    [HarmonyPatch]
    internal static class ChefGridLobbyPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(LobbyFlowController), "Start")]
        static void Start(LobbyFlowController __instance) { ChefGridLobby.Attach(__instance); }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientLobbyFlowController), "Update")]
        static void Update(ClientLobbyFlowController __instance) { ChefGridLobby.ConsumeLobbyButtons(__instance); }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientLobbyFlowController), "ShowLeaveDialog")]
        static bool Leave() { return !ChefGridLobby.Blocking; }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientLobbyFlowController), "OnLoadLevel")]
        static void LevelLoad() { ChefGridLobby.BeforeLevelLoad(); }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientEmoteWheel), "Update")]
        static bool Emotes(ClientEmoteWheel __instance)
        {
            return !ChefGridLobby.Blocking || !__instance.get_m_emoteWheel().ForUI;
        }
    }
}
