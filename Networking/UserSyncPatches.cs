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
        public static void PatchInternal(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method("DisconnectionHandler:GoOffline"), new HarmonyMethod(typeof(Patch).GetMethod("DisconnectionHandlerGoOfflinePrefix")), null);
        }

        public static void DisconnectionHandlerGoOfflinePrefix()
        {
            byte[] newDIYChefID = Enumerable.Repeat((byte)255, 4).ToArray();
            int i1 = 0;
            FastList<User> clientUsers = ClientUserSystem.m_Users;
            for (int i = 0; i < clientUsers.Count; i++)
            {
                User user = clientUsers._items[i];
                if (user.IsLocal) newDIYChefID[i1++] = DIYChefCustomisation.userDIYChefID[i];
            }
            Array.Copy(newDIYChefID, DIYChefCustomisation.userDIYChefID, 4);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ServerUserSystem), "ResetUsersToOfflineState")]
        public static void ServerUserSystemResetUsersToOfflineStatePrefix(out byte[] __state)
        {
            __state = Enumerable.Repeat((byte)255, 4).ToArray();
            int i1 = 0;
            FastList<User> clientUsers = ClientUserSystem.m_Users;
            for (int i = 0; i < clientUsers.Count; i++)
            {
                User user = clientUsers._items[i];
                if (user.IsLocal) __state[i1++] = DIYChefCustomisation.userDIYChefID[i];
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ServerUserSystem), "ResetUsersToOfflineState")]
        public static void ServerUserSystemResetUsersToOfflineStatePostfix(byte[] __state)
        {
            Array.Copy(__state, DIYChefCustomisation.userDIYChefID, 4);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NetworkSystemConfigurator), "Client")]
        public static void NetworkSystemConfiguratorClientPatch()
        {
            for (int i = 0; i < ClientUserSystem.m_Users.Count; i++)
            {
                User user = ClientUserSystem.m_Users._items[i];
                if (user.IsLocal)
                    DIYChefCustomisation.SendClientDIYChefMessage(DIYChefCustomisation.userDIYChefID[i], user);
            }
        }

        public static bool flag_ClientUserSystemOnUsersChangedPatch = true;
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ClientUserSystem), "OnUsersChanged")]
        public static void ClientUserSystemOnUsersChangedPatch(Serialisable message)
        {
            if (flag_ClientUserSystemOnUsersChangedPatch)
            {
                var multiplayerController = GameUtils.RequestManager<MultiplayerController>();
                if (multiplayerController == null || multiplayerController.IsServer()) return;
                var usersChangedMessage = (UsersChangedMessage)message;
                byte[] newDIYChefID = Enumerable.Repeat((byte)255, 4).ToArray();
                FastList<User> users = ClientUserSystem.m_Users;

                for (int i = 0; i < usersChangedMessage.m_Users.Count; i++)
                {
                    var userData = usersChangedMessage.m_Users._items[i];
                    var Machine = userData.machine;
                    var Engagement = userData.slot;
                    var Split = userData.splitStatus;
                    User user = UserSystemUtils.FindUser(users, null, Machine, Engagement, TeamID.Count, Split);
                    if (user == null && Split != User.SplitStatus.SplitPadGuest)
                        user = UserSystemUtils.FindUser(users, null, Machine, Engagement, TeamID.Count);
                    if (user != null)
                    {
                        int i1 = users.FindIndex(x => x == user);
                        newDIYChefID[i] = DIYChefCustomisation.userDIYChefID[i1];
                    }
                }
                Array.Copy(newDIYChefID, DIYChefCustomisation.userDIYChefID, 4);
            }
            flag_ClientUserSystemOnUsersChangedPatch = true;
        }

    }
}
