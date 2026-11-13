using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Team17.Online;
using Team17.Online.Multiplayer;
using Team17.Online.Multiplayer.Messaging;
using BitStream;
using OC2DIYChef.Extension;
using System.Collections;

namespace OC2DIYChef
{
    public static partial class DIYChefCustomisation
    {
        public static void SendClientDIYChefMessage(byte diyChefID, User user)
        {
            var message = new DIYChefMessage();
            message.InitialiseClient(diyChefID, user);
            localClient.SendMessageToServer(diyChefMessageType, message, true);
        }

        public static void SendServerDIYChefMessage()
        {
            if (localServer == null) return;
            var message = new DIYChefMessage();
            message.InitialiseServer();
            localServer.BroadcastMessageToAll(diyChefMessageType, message, true);
        }

        public static void OnMessageReceived(DIYChefMessage message)
        {
            if (!message.server2client)
            {
                FastList<User> users = ServerUserSystem.m_Users;
                User.MachineID machine = message.chefAvatarMessage.m_Machine;
                EngagementSlot engagementSlot = message.chefAvatarMessage.m_EngagementSlot;
                User.SplitStatus split = message.chefAvatarMessage.m_Split;
                User user = UserSystemUtils.FindUser(users, null, machine, engagementSlot, TeamID.Count, split);
                if (user == null) return;
                int player = users.FindIndex(x => x == user);
                user.ChangedThisFrame = true;
                userDIYChefID[player] = message.userDIYChefID[0];
                SendServerDIYChefMessage();
            }
            else
            {
                var multiplayerController = GameUtils.RequestManager<MultiplayerController>();
                if (multiplayerController == null || multiplayerController.IsServer()) return;
                byte[] newDIYChefID = new byte[4];
                Array.Copy(message.userDIYChefID, newDIYChefID, 4);
                FastList<User> users = ClientUserSystem.m_Users;

                for (int i = 0; i < message.usersChangedMessage.m_Users.Count; i++)
                {
                    var userData = message.usersChangedMessage.m_Users._items[i];
                    var Machine = userData.machine;
                    if (Machine != ClientUserSystem.s_LocalMachineId) continue;
                    var Engagement = userData.slot;
                    var Split = userData.splitStatus;
                    User user = UserSystemUtils.FindUser(users, null, Machine, Engagement, TeamID.Count, Split);
                    if (user == null && Split != User.SplitStatus.SplitPadGuest)
                        user = UserSystemUtils.FindUser(users, null, Machine, Engagement, TeamID.Count);
                    if (user != null)
                    {
                        int i1 = users.FindIndex(x => x == user);
                        if (newDIYChefID[i] != userDIYChefID[i1])
                        {
                            newDIYChefID[i] = userDIYChefID[i1];
                            SendClientDIYChefMessage(userDIYChefID[i1], user);
                        }
                    }
                }
                Array.Copy(newDIYChefID, userDIYChefID, 4);
                Patch.flag_ClientUserSystemOnUsersChangedPatch = false;
                localClient.GetUserSystem().OnUsersChanged(null, message.usersChangedMessage);
            }
        }

        private static void OnUserRemoved(User _user, int _idx)
        {
            for (int i = _idx; i < 3; i++) userDIYChefID[i] = userDIYChefID[i + 1];
            userDIYChefID[3] = 255;
            SendServerDIYChefMessage();
        }

        public static void OnUserAdded(User _user)
        {
            int player = ServerUserSystem.m_Users.FindIndex((User x) => x == _user);
            if (player != -1)
            {
                for (int i = 3; i > player; i--)
                    userDIYChefID[i] = userDIYChefID[i - 1];
                userDIYChefID[player] = 255;
            }
            SendServerDIYChefMessage();
        }

    }
}
