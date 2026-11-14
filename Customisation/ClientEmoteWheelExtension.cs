using BitStream;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Team17.Online;

namespace OC2DIYChef.Extension
{
    public static class ClientEmoteWheelExtension
    {
        static readonly FieldInfo fieldInfo_m_downButton = AccessTools.Field(typeof(ClientEmoteWheel), "m_downButton");
        static readonly FieldInfo fieldInfo_m_upButton = AccessTools.Field(typeof(ClientEmoteWheel), "m_upButton");
        static readonly FieldInfo fieldInfo_m_emoteWheel = AccessTools.Field(typeof(ClientEmoteWheel), "m_emoteWheel");
        static readonly FieldInfo fieldInfo_m_emoteSelector = AccessTools.Field(typeof(ClientEmoteWheel), "m_emoteSelector");
        static readonly FieldInfo fieldInfo_m_xMovement = AccessTools.Field(typeof(ClientEmoteWheel), "m_xMovement");
        static readonly FieldInfo fieldInfo_m_yMovement = AccessTools.Field(typeof(ClientEmoteWheel), "m_yMovement");
        static readonly FieldInfo fieldInfo_m_player = AccessTools.Field(typeof(EmoteWheel), "m_player");
       
        public static bool DownJustPressed(this ClientEmoteWheel instance)
        {
            var button = (ILogicalButton)fieldInfo_m_downButton.GetValue(instance);
            return button.JustPressed();
        }

        public static bool UpJustPressed(this ClientEmoteWheel instance)
        {
            var button = (ILogicalButton)fieldInfo_m_upButton.GetValue(instance);
            return button.JustPressed();
        }

        public static float XValue(this ClientEmoteWheel instance)
        {
            var button = (ILogicalValue)fieldInfo_m_xMovement.GetValue(instance);
            return button.GetValue();
        }

        public static float YValue(this ClientEmoteWheel instance)
        {
            var button = (ILogicalValue)fieldInfo_m_yMovement.GetValue(instance);
            return button.GetValue();
        }

        public static EmoteWheel get_m_emoteWheel(this ClientEmoteWheel instance)
        {
            return (EmoteWheel)fieldInfo_m_emoteWheel.GetValue(instance);
        }

        public static EmoteSelector get_m_emoteSelector(this ClientEmoteWheel instance)
        {
            return (EmoteSelector)fieldInfo_m_emoteSelector.GetValue(instance);
        }

        public static PlayerInputLookup.Player get_m_player(this EmoteWheel instance)
        {
            return (PlayerInputLookup.Player)fieldInfo_m_player.GetValue(instance);
        }
    }
}
