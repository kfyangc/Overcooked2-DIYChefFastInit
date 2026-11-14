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
    // Shared input ownership for arcade name menus and native map customisation.
    public sealed class ChefGridLobby : MonoBehaviour
    {
        static readonly FieldInfo leave = AccessTools.Field(typeof(ClientLobbyFlowController), "m_leaveButton");
        static readonly FieldInfo localInputs = AccessTools.Field(typeof(ClientLobbyFlowController), "m_localUserInput");
        static readonly FieldInfo changeTeam = AccessTools.Field(
            AccessTools.Inner(typeof(ClientLobbyFlowController), "UserInput"), "m_changeTeamButton");
        static ChefGridLobby current;
        readonly ChefGridLobbySession session = new ChefGridLobbySession();
        readonly List<ILogicalButton> watched = new List<ILogicalButton>();
        readonly List<Suppressor> suppressors = new List<Suppressor>();
        readonly HashSet<T17EventSystem> eventSystems = new HashSet<T17EventSystem>();
        readonly ChefGridReturnFocus returnFocus = new ChefGridReturnFocus();
        ChefGridLobbyMenu menu;
        internal UIPlayerRootMenu Players { get; private set; }
        internal static bool Blocking { get { return current != null && current.session.Blocking; } }
        internal static bool BlocksMenus(UIPlayerRootMenu players)
        {
            return Blocking && current.Players == players;
        }

        internal static ChefGridLobby Attach(UIPlayerRootMenu players)
        {
            if (players == null) return null;
            var entry = players.GetComponent<ChefGridLobby>() ?? players.gameObject.AddComponent<ChefGridLobby>();
            entry.Players = players;
            current = entry;
            return entry;
        }

        internal static bool CanShowFor(User user)
        {
            if (!ChefGridMenu.Enabled || user == null || user.SelectedChefData == null ||
                ChefGridLobbySelection.FindLocal(user) < 0) return false;
            return ChefGridPlayerContext.CurrentPlayers() != null;
        }

        internal static bool CanOpenFor(User user)
        {
            return CanShowFor(user) && !Blocking && ChefGridMenu.Active == null && Application.isFocused &&
                !T17DialogBoxManager.HasAnyOpenDialogs();
        }

        internal static void OpenFor(UIPlayerMenuBehaviour card)
        {
            if (card == null || !CanOpenFor(card.UserInfo) || card.m_rootMenu != ChefGridPlayerContext.CurrentPlayers()) return;
            Attach(card.m_rootMenu);
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
            returnFocus.Capture(card);
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
            ChefGridPlayerMenu.HideAll(Players);
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

        internal void EndInput()
        {
            session.End(Time.frameCount);
            ChefGridPlayerMenu.HideAll(Players);
        }

        internal void Committed()
        {
            ChefGridPlayerMenu.HideAll(Players);
        }

        internal static void BeforeLevelLoad()
        {
            if (current != null && current.menu != null) current.menu.Hide(false);
        }

        internal static void CloseForRoot(UIPlayerRootMenu players)
        {
            if (current != null && current.Players == players && current.menu != null) current.menu.Hide(false);
        }

        internal static void ShutdownActive() { if (current != null) current.Shutdown(); }

        internal static void UpdateFrame() { if (current != null) current.Advance(); }

        void Advance()
        {
            if (!session.Blocking)
            {
                RestoreFocus();
                return;
            }
            bool held = Input.GetMouseButton(0);
            foreach (var code in new[] { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Escape, KeyCode.Space,
                KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow }) held |= Input.GetKey(code);
            foreach (var logical in watched) if (logical != null) held |= logical.IsDown();
            session.Advance(Time.frameCount, held);
            if (!session.Blocking)
            {
                ReleaseInput();
                ChefGridPlayerMenu.HideAll(Players);
                ChefGridPlayerMenu.Refresh(Players);
                RestoreFocus();
            }
        }

        void RestoreFocus()
        {
            if (Application.isFocused && !T17DialogBoxManager.HasAnyOpenDialogs())
                returnFocus.Restore(ChefGridPlayerContext.CurrentPlayers());
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
            returnFocus.Clear();
            if (current == this) current = null;
        }

        void OnDestroy() { Shutdown(); }
        // The map pause root can deactivate on confirmation. The plugin continues
        // advancing the guard until the closing keys are up, even for an inactive root.
        void OnDisable() { if (menu != null) menu.Hide(false); }
    }

    [HarmonyPatch]
    internal static class ChefGridLobbyPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(LobbyFlowController), "Start")]
        static void Start(LobbyFlowController __instance) { ChefGridLobby.Attach(__instance.m_uiPlayerRoot); }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientLobbyFlowController), "Update")]
        static void Update(ClientLobbyFlowController __instance) { ChefGridLobby.ConsumeLobbyButtons(__instance); }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientLobbyFlowController), "ShowLeaveDialog")]
        static bool Leave() { return !ChefGridLobby.Blocking; }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientLobbyFlowController), "OnLoadLevel")]
        static void LevelLoad() { ChefGridLobby.BeforeLevelLoad(); }

        [HarmonyPrefix, HarmonyPatch(typeof(PauseMenuManager), "Update")]
        static void Pause(PauseMenuManager __instance) { ChefGridPauseGuard.BeforeUpdate(__instance, ChefGridLobby.Blocking); }

        [HarmonyPrefix, HarmonyPatch(typeof(InGamePauseMenu), "Hide")]
        static void HidePause(InGamePauseMenu __instance)
        {
            ChefGridLobby.CloseForRoot(ChefGridPlayerContext.PausePlayers(__instance));
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientEmoteWheel), "Update")]
        static bool Emotes(ClientEmoteWheel __instance)
        {
            return !ChefGridLobby.Blocking || !__instance.get_m_emoteWheel().ForUI;
        }
    }
}
