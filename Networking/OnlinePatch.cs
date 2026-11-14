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
    public static class OnlinePatch
    {
        public static void Patch(Harmony harmony)
        {
            harmony.PatchAll(typeof(OnlinePatch));
            harmony.Patch(AccessTools.Method("Mailbox:OnMessageReceived"), new HarmonyMethod(typeof(OnlinePatch).GetMethod("MailboxOnMessageReceivedPrefix")), null);
            harmony.Patch(AccessTools.Method("ClientMessenger:OnClientStarted"), null, new HarmonyMethod(typeof(OnlinePatch).GetMethod("ClientMessengerOnClientStartedPostfix")));
            harmony.Patch(AccessTools.Method("ServerMessenger:OnServerStarted"), null, new HarmonyMethod(typeof(OnlinePatch).GetMethod("ServerMessengerOnServerStartedPostfix")));
            harmony.Patch(AccessTools.Method("ServerMessenger:OnServerStopped"), null, new HarmonyMethod(typeof(OnlinePatch).GetMethod("ServerMessengerOnServerStoppedPostfix")));
        }

        public static bool MailboxOnMessageReceivedPrefix(MessageType type, Serialisable message)
        {
            if (type != DIYChefCustomisation.diyChefMessageType)
                return true;
            DIYChefMessage diyChefMessage = (DIYChefMessage)message;
            DIYChefCustomisation.OnMessageReceived(diyChefMessage);
            return false;
        }

        public static void ClientMessengerOnClientStartedPostfix(Client client)
        {
            DIYChefCustomisation.localClient = client;
        }

        public static void ServerMessengerOnServerStartedPostfix(Server server)
        {
            DIYChefCustomisation.localServer = server;
        }

        public static void ServerMessengerOnServerStoppedPostfix()
        {
            DIYChefCustomisation.localServer = null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Message), "Deserialise")]
        public static bool MessageDeserialisePatch(BitStreamReader reader, Message __instance, ref bool __result, ref bool __runOriginal)
        {
            if (!__runOriginal) return false;
            var messageType = (MessageType)reader.ReadByteAhead(8);
            if (messageType == DIYChefCustomisation.diyChefMessageType)
            {
                __instance.Type = (MessageType)reader.ReadByte(8);
                __instance.Payload = new DIYChefMessage();
                __instance.Payload.Deserialise(reader);
                __result = true;
                return false;
            }
            else
            {
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NetworkMessageTracker), "TrackSentGlobalEvent")]
        public static bool NetworkMessageTrackerTrackSentGlobalEventPatch(MessageType type)
        {
            return type != DIYChefCustomisation.diyChefMessageType;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NetworkMessageTracker), "TrackReceivedGlobalEvent")]
        public static bool NetworkMessageTrackerTrackReceivedGlobalEventPatch(MessageType type)
        {
            return type != DIYChefCustomisation.diyChefMessageType;
        }

    }

}
