using BitStream;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Team17.Online;

namespace OC2DIYChef.Extension
{
    public static class MultiplayerControllerExtension
    {
        private static readonly MethodInfo methodInfo_IsServer = AccessTools.Method(typeof(MultiplayerController), "IsServer");
        
        public static bool IsServer(this MultiplayerController instance)
        {
            return (bool)methodInfo_IsServer.Invoke(instance, null);
        }
    }
}
