using BitStream;
using HarmonyLib;
using OC2DIYChef.Extension;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Team17.Online;
using Team17.Online.Multiplayer;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OC2DIYChef
{
    public static partial class Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(NetworkUtils), "SelectRandomAvatar")]
        public static bool NetworkUtilsSelectRandomAvatarPatch()
        {
            if (!DIYChefCustomisation.Enabled)
                return true;
            DIYChefCustomisation.ApplyDefaultChefSelection();
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ClientEmoteWheel), "Update")]
        public static void ClientEmoteWheelUpdatePatch(ClientEmoteWheel __instance)
        {
            if (ChefGridMenu.Enabled || ChefGridLobby.Blocking) return;
            if (!DIYChefCustomisation.Enabled || !DIYChefCustomisation.enableLobbySwitchChef) return;
            EmoteWheel emoteWheel = __instance.get_m_emoteWheel();
            EmoteSelector emoteSelector = __instance.get_m_emoteSelector();
            if (!emoteWheel.ForUI || !emoteWheel.IsLocal || emoteSelector.IsActive()) return;
            int step = __instance.UpJustPressed() ? 1 : (__instance.DownJustPressed() ? -1 : 0);
            if (step == 0) return;
            if (Mathf.Abs(__instance.XValue()) > 0.01 || Mathf.Abs(__instance.YValue()) < 0.99) return;

            int player = (int)emoteWheel.get_m_player();
            FastList<User> users = ClientUserSystem.m_Users;
            if (player >= users.Count) return;
            MetaGameProgress metaGameProgress = GameUtils.GetMetaGameProgress();
            if (metaGameProgress == null) return;
            var unlockedAvatars = metaGameProgress.GetUnlockedAvatars();
            if (unlockedAvatars.Length == 0) return;
            DIYChefCustomisation.SwitchChef(player, step, out uint uAvatarID, out byte uDIYAvatarID);

            User user = users._items[player];
            var m_ChefAvatar = new ChefAvatarMessage();
            m_ChefAvatar.Initialise(uAvatarID, user.Machine, user.Engagement, user.Split);
            DIYChefCustomisation.localClient.SendMessageToServer(MessageType.ChefAvatar, m_ChefAvatar, true);
            DIYChefCustomisation.userDIYChefID[player] = uDIYAvatarID;
            DIYChefCustomisation.SendClientDIYChefMessage(uDIYAvatarID, user);
            ClientUserSystem.usersChanged();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChefCustomiser), "CacheCurrentAvatars")]
        public static void CacheDIYChefSelection(ChefCustomiser __instance)
        {
            ChefSelectionMenu.Capture(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChefCustomiser), "RevertAvatars")]
        public static void RevertDIYChefSelection(ChefCustomiser __instance, out bool __state)
        {
            __state = ChefSelectionMenu.RestoreBeforeAvatars(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChefCustomiser), "RevertAvatars")]
        public static void RefreshRestoredDIYChefSelection(bool __state)
        {
            if (__state) ChefSelectionMenu.SynchronizeRestoredChoices();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FrontendChefMenu), "Hide")]
        public static void ReleaseChefSelectionOnHide(FrontendChefMenu __instance, bool __result)
        {
            if (__result) ChefSelectionMenu.Release(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FrontendChefMenu), "OnDestroy")]
        public static void ReleaseChefSelectionOnDestroy(FrontendChefMenu __instance)
        {
            ChefSelectionMenu.Release(__instance);
        }

        public static class FrontendChefCustomisationPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(FrontendChefCustomisation), "OnClickLeftButton")]
            public static bool OnClickLeftButtonPrefix(FrontendChefCustomisation __instance)
            {
                if (ChefGridMenu.Active != null && ChefGridMenu.Active.Owns(__instance)) return false;
                OnClickLeftOrRightButtonPrefix(__instance, -1);
                return true;
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(FrontendChefCustomisation), "OnClickRightButton")]
            public static bool OnClickRightButtonPrefix(FrontendChefCustomisation __instance)
            {
                if (ChefGridMenu.Active != null && ChefGridMenu.Active.Owns(__instance)) return false;
                OnClickLeftOrRightButtonPrefix(__instance, 1);
                return true;
            }

            public static void OnClickLeftOrRightButtonPrefix(FrontendChefCustomisation __instance, int step)
            {
                if (!DIYChefCustomisation.Enabled) return;
                int player = __instance.get_m_actualPlayer();
                FastList<User> users = ClientUserSystem.m_Users;
                if (player >= users.Count) return;
                MetaGameProgress metaGameProgress = GameUtils.GetMetaGameProgress();
                if (metaGameProgress == null) return;
                var allAvatars = GameUtils.GetAvatarDirectoryData().Avatars;
                var unlockedAvatars = __instance.get_m_unlockedAvatars();
                if (unlockedAvatars.Length == 0) return;

                DIYChefCustomisation.SwitchChef(player, step, out uint uAvatarID, out byte uDIYAvatarID);
                int newIndex = unlockedAvatars.FindIndex_Predicate(x => x == allAvatars[uAvatarID]);
                __instance.set_m_chefSelection(newIndex - step);
                DIYChefCustomisation.userDIYChefID[player] = uDIYAvatarID;
                DIYChefCustomisation.SendClientDIYChefMessage(uDIYAvatarID, users._items[player]);
                ClientUserSystem.usersChanged();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MetaGameProgress), "ByteLoad")]
        public static void MetaGameProgressByteLoadPatch(MetaGameProgress __instance, bool __result)
        {
            if (__result) DIYChefCustomisation.OnAwake(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChefAvatarData), "IsAvailableOnThisPlatform")]
        public static bool ChefAvatarDataIsAvailableOnThisPlatformPatch(ref bool __result)
        {
            __result = true;
            return false;
        }

    }
}
