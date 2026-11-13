using BitStream;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Team17.Online;

namespace OC2DIYChef.Extension
{
    public static class ChefMeshReplacerExtension
    {
        private static readonly FieldInfo fieldInfo_m_currentHeadName = AccessTools.Field(typeof(ChefMeshReplacer), "m_currentHeadName");

        public static string get_m_currentHeadName(this ChefMeshReplacer instance)
        {
            return (string)fieldInfo_m_currentHeadName.GetValue(instance) ?? "";
        }
    }
}
