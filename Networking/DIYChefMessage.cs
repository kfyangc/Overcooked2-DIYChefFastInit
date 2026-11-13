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
    public class DIYChefMessage : Serialisable
    {
        public void InitialiseClient(byte diyChefID, User user)
        {
            chefAvatarMessage.Initialise(127U, user.Machine, user.Engagement, user.Split);
            server2client = false;
            userDIYChefID[0] = diyChefID;
        }

        public void InitialiseServer()
        {
            usersChangedMessage.Initialise(ServerUserSystem.m_Users);
            server2client = true;
            Array.Copy(DIYChefCustomisation.userDIYChefID, userDIYChefID, 4);
        }

        public void Serialise(BitStreamWriter writer)
        {
            writer.Write(server2client);
            if (server2client)
            {
                usersChangedMessage.Serialise(writer);
                for (int i = 0; i < 4; i++)
                    writer.Write(userDIYChefID[i], 8);
            }
            else
            {
                chefAvatarMessage.Serialise(writer);
                writer.Write(userDIYChefID[0], 8);
            }
        }

        public bool Deserialise(BitStreamReader reader)
        {
            server2client = reader.ReadBit();
            if (server2client)
            {
                usersChangedMessage.Deserialise(reader);
                for (int i = 0; i < 4; i++)
                    userDIYChefID[i] = reader.ReadByte(8);
            }
            else
            {
                chefAvatarMessage.Deserialise(reader);
                userDIYChefID[0] = reader.ReadByte(8);
            }
            return true;
        }

        public ChefAvatarMessage chefAvatarMessage = new ChefAvatarMessage();
        public UsersChangedMessage usersChangedMessage = new UsersChangedMessage();
        public bool server2client;
        public byte[] userDIYChefID = new byte[4];
    }
}
