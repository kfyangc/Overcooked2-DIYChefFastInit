using System;
using Team17.Online;
using Team17.Online.Multiplayer.Messaging;

namespace OC2DIYChef
{
    internal static class PlayerHatNetwork
    {
        internal const MessageType Type = (MessageType)68;

        internal static void Send(User user)
        {
            if (DIYChefCustomisation.localClient == null) return;
            var identity = new ChefAvatarMessage();
            identity.Initialise(127U, user.Machine, user.Engagement, user.Split);
            DIYChefCustomisation.localClient.SendMessageToServer(Type, new PlayerHatMessage {
                Users = new[] { identity }, Hats = new[] { PlayerHats.Get(user) } }, true);
        }

        internal static void Broadcast()
        {
            if (DIYChefCustomisation.localServer == null) return;
            var users = ServerUserSystem.m_Users;
            int count = Math.Min(4, users.Count);
            var packet = new PlayerHatMessage { ServerSnapshot = true,
                Users = new ChefAvatarMessage[count], Hats = new string[count] };
            for (int i = 0; i < count; i++)
            {
                var user = users._items[i];
                packet.Users[i] = new ChefAvatarMessage();
                packet.Users[i].Initialise(127U, user.Machine, user.Engagement, user.Split);
                packet.Hats[i] = PlayerHats.Get(user);
            }
            DIYChefCustomisation.localServer.BroadcastMessageToAll(Type, packet, true);
        }

        internal static void Receive(PlayerHatMessage packet)
        {
            var users = packet.ServerSnapshot ? ClientUserSystem.m_Users : ServerUserSystem.m_Users;
            if (!packet.ServerSnapshot && DIYChefCustomisation.localServer == null) return;
            if (packet.ServerSnapshot && DIYChefCustomisation.localServer != null) return;
            for (int i = 0; i < packet.Users.Length; i++)
            {
                var identity = packet.Users[i];
                if (packet.ServerSnapshot)
                {
                    PlayerHats.Receive(identity.m_Machine, identity.m_EngagementSlot, identity.m_Split, packet.Hats[i]);
                    continue;
                }
                var user = UserSystemUtils.FindUser(users, null, identity.m_Machine,
                    identity.m_EngagementSlot, TeamID.Count, identity.m_Split);
                if (user != null && !user.IsLocal) PlayerHats.Receive(user, packet.Hats[i]);
            }
            if (!packet.ServerSnapshot) Broadcast();
        }
    }
}
