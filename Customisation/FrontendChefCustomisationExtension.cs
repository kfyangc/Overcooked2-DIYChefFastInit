using BitStream;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Team17.Online;

namespace OC2DIYChef.Extension
{
    public static class FrontendChefCustomisationExtension
    {
        private static readonly FieldInfo fieldInfo_m_unlockedAvatars = AccessTools.Field(typeof(FrontendChefCustomisation), "m_unlockedAvatars");
        private static readonly FieldInfo fieldInfo_m_chefSelection = AccessTools.Field(typeof(FrontendChefCustomisation), "m_chefSelection");
        private static readonly FieldInfo fieldInfo_m_actualPlayer = AccessTools.Field(typeof(FrontendChefCustomisation), "m_actualPlayer");

        public static ChefAvatarData[] get_m_unlockedAvatars(this FrontendChefCustomisation instance)
        {
            return (ChefAvatarData[])fieldInfo_m_unlockedAvatars.GetValue(instance);
        }

        public static int get_m_actualPlayer(this FrontendChefCustomisation instance)
        {
            return (int)fieldInfo_m_actualPlayer.GetValue(instance);
        }

        public static void set_m_chefSelection(this FrontendChefCustomisation instance, int index)
        {
            fieldInfo_m_chefSelection.SetValue(instance, index);
        }
    }
}
