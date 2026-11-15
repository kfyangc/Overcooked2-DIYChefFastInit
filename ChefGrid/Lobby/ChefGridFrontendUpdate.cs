using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OC2DIYChef
{
    // Some frontend instances stop receiving Unity's Update after customisation.
    // Keep the native lifecycle and cancel dispatcher, rather than synthesizing
    // a second Escape/B handler or clearing another menu's suppression flags.
    [HarmonyPatch]
    internal static class ChefGridFrontendUpdate
    {
        static readonly MethodInfo update = AccessTools.Method(typeof(FrontendRootMenu), "Update");
        static FrontendRootMenu armed;
        static readonly Dictionary<FrontendRootMenu, int> updatedFrames = new Dictionary<FrontendRootMenu, int>();
        static bool reported;

        internal static void Arm(FrontendRootMenu root)
        {
            if (armed != root) reported = false;
            armed = root;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(FrontendRootMenu), "Update")]
        internal static void Updated(FrontendRootMenu __instance)
        {
            updatedFrames[__instance] = Time.frameCount;
        }

        internal static void Tick()
        {
            int updatedFrame;
            if (armed == null || !armed.isActiveAndEnabled || ChefGridMenu.Active != null ||
                (updatedFrames.TryGetValue(armed, out updatedFrame) && updatedFrame == Time.frameCount)) return;
            try
            {
                if (!reported)
                {
                    reported = true;
                    DIYChefPlugin.Log("[CancelTrace] Restoring missing native FrontendRootMenu.Update after chef grid.");
                }
                updatedFrames[armed] = Time.frameCount;
                update.Invoke(armed, null);
            }
            catch (Exception error)
            {
                armed = null;
                DIYChefPlugin.Warn("Frontend native update recovery stopped: " + error);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(FrontendRootMenu), "OnDestroy")]
        static void Destroy(FrontendRootMenu __instance)
        {
            updatedFrames.Remove(__instance);
            if (armed == __instance) armed = null;
        }
    }
}
