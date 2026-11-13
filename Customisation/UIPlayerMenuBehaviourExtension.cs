using BitStream;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Team17.Online;

namespace OC2DIYChef.Extension
{
    public static class UIPlayerMenuBehaviourExtension
    {
        private static readonly FieldInfo fieldInfo_m_User = AccessTools.Field(typeof(UIPlayerMenuBehaviour), "m_User");
        private static readonly FieldInfo fieldInfo_m_chef = AccessTools.Field(typeof(UIPlayerMenuBehaviour), "m_chef");
        private static readonly FieldInfo fieldInfo_m_AmbientColor = AccessTools.Field(typeof(UIPlayerMenuBehaviour), "m_AmbientColor");

        public static User get_m_User(this UIPlayerMenuBehaviour instance)
        {
            return (User)fieldInfo_m_User.GetValue(instance);
        }

        public static FrontendChef get_m_chef(this UIPlayerMenuBehaviour instance)
        {
            return (FrontendChef)fieldInfo_m_chef.GetValue(instance);
        }

        public static void set_m_chef(this UIPlayerMenuBehaviour instance, FrontendChef chef)
        {
            fieldInfo_m_chef.SetValue(instance, chef);
        }

        public static Color get_m_AmbientColor(this UIPlayerMenuBehaviour instance)
        {
            return (Color)fieldInfo_m_AmbientColor.GetValue(instance);
        }
    }
}
