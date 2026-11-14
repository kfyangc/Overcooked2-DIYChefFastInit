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
    public static partial class Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIPlayerRootMenu), "Start")]
        public static void UIPlayerRootMenuStartPatch(UIPlayerRootMenu __instance)
        {
            Light[] lights = GameObject.FindObjectsOfType<Light>();
            bool hasHatLight = lights.Any(x => x.name == "hat light");

            foreach (var light in lights)
            {
                if (light.name == "day light" && (light.gameObject.scene.name == "InGameMenu" || light.gameObject.scene.name == "Lobbies"))
                {
                    light.GetComponent<Light>().intensity = 0.5f;
                    if (!hasHatLight)
                    {
                        var hatLight = GameObject.Instantiate(light.gameObject);
                        SceneManager.MoveGameObjectToScene(hatLight, light.gameObject.scene);
                        hatLight.SetObjectLayer(light.gameObject.layer);
                        hatLight.name = "hat light";
                        hatLight.transform.SetParent(light.transform.parent);
                        hatLight.GetComponent<Light>().intensity = 1.8f;
                        hatLight.GetComponent<Light>().cullingMask = 1 << HatData.hatLayer;
                    }
                    break;
                }
            }
            Camera camera = (Camera)AccessTools.Field(typeof(UIPlayerRootMenu), "m_chefCamera").GetValue(__instance);
            camera.cullingMask |= 1 << HatData.hatLayer;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FrontendChef), "SetChefHat")]
        public static void FrontendChefSetChefHatPatch(FrontendChef __instance, ref HatMeshVisibility.VisState _hat)
        {
            HatData.SetChefHat(__instance.gameObject, ref _hat);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BodyMeshVisibility), "Awake")]
        public static void BodyMeshVisibilityAwakePatch(BodyMeshVisibility __instance)
        {
            if (!__instance.m_meshes.Contains("Body_NeckTie"))
                __instance.m_meshes = __instance.m_meshes.AddRangeToArray(new string[]
                {
                    "Body_NeckTie",
                    "Body_Top",
                    "Body_Bottom",
                    "Body_Tail",
                    "Body_Body",
                });
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FrontendChef), "SetChefData", new Type[] { typeof(GameSession.SelectedChefData), typeof(bool) })]
        public static bool FrontendChefSetData(FrontendChef __instance, GameSession.SelectedChefData chefData, bool force)
        {
            return ChefLoading.Applying || ChefLoading.Submit(__instance, chefData, force);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ChefMeshReplacer), "ReplaceModel")]
        public static IEnumerable<CodeInstruction> ResolveCustomBody(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var find = AccessTools.Method(typeof(GameObjectUtils), "FindChildRecursive", new[] { typeof(Transform), typeof(string) });
            var replacement = AccessTools.Method(typeof(Patch), "FindChefBody");
            for (int i = 1; i < codes.Count; i++)
                if (codes[i].Calls(find) && codes[i - 1].opcode == OpCodes.Ldstr &&
                    (string)codes[i - 1].operand == "Body")
                    codes[i].operand = replacement;
            return codes;
        }

        public static Transform FindChefBody(Transform root, string name)
        {
            return root.FindChildRecursive(name) ?? root.FindChildRecursive("Body_Body");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FrontendChef), "SetChefData", new Type[] { typeof(GameSession.SelectedChefData), typeof(bool) })]
        public static void FrontendChefSetDataComplete(FrontendChef __instance)
        {
            ChefLoading.Capture(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChefMeshReplacer), "SetChefData")]
        public static void ReplacerSetData(ChefMeshReplacer __instance, ref GameSession.SelectedChefData chefData,
            out GameObject __state)
        {
            __state = __instance.ChefModel;
            ChefLoading.EnsureImmediate(ref chefData, __instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChefMeshReplacer), "SetChefData")]
        public static void ReplacerSetDataComplete(ChefMeshReplacer __instance, GameSession.SelectedChefData chefData,
            GameObject __state)
        {
            ChefLoading.Track(__instance, chefData);
            if (__instance.GetComponent<ChefGridPortraitActor>() == null)
                ModelTestCompatibility.ModelReplaced(__state, __instance.ChefModel);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChefMeshReplacer), "AssignBodyColour")]
        public static bool ChefMeshReplacerAssignBodyColourPatch(GameObject _root, GameSession.SelectedChefData _data)
        {
            _root.SetActive(true);  // prefab is an inactive gameobject
            int maskParamID = Shader.PropertyToID("_MaskColor");
            Transform tMesh = _root.transform.Find("Mesh");
            for (int i = 0; i < tMesh.childCount; i++)
                if (tMesh.GetChild(i).name.StartsWith("Body"))
                {
                    GameObject gameObject = tMesh.GetChild(i).gameObject;
                    if (gameObject.name.Equals("Body") && // diy body uses SwapColourValue
                        _data.Character.ColourisationMode == ChefMeshReplacer.ChefColourisationMode.SwapMaterial)
                    {
                        gameObject.RequireComponent<SkinnedMeshRenderer>().material = _data.Colour.ChefMaterial;
                    }
                    else
                    {
                        Material material = gameObject.RequireComponent<SkinnedMeshRenderer>().material;
                        material.SetColor(maskParamID, _data.Colour.MaskColour);
                    }
                }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ClientMeshVisibilityBase<HatMeshVisibility.VisState>), "Setup")]
        public static void ClientHatMeshVisibilitySetupPatch(ClientHatMeshVisibility __instance, ref HatMeshVisibility.VisState _state)
        {
            HatData.SetChefHat(__instance.gameObject, ref _state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIPlayerMenuBehaviour), "SetupChefModel")]
        public static bool UIPlayerMenuBehaviourSetupChefModelPatch(UIPlayerMenuBehaviour __instance, bool _force)
        {
            User user = __instance.get_m_User();
            FrontendChef chef = __instance.get_m_chef();
            if (user == null)
            {
                if (chef != null && chef.ChefModel != null)
                    chef.ChefModel.SetActive(false);
                return false;
            }
            if (chef != null && chef.ChefModel != null)
                chef.ChefModel.SetActive(true);
            __instance.gameObject.layer = LayerMask.NameToLayer(__instance.m_rootMenu.m_uiChefLayer);
            chef = __instance.gameObject.RequireComponent<FrontendChef>();
            __instance.set_m_chef(chef);
            GameObject chefModel = chef.ChefModel;

            GameSession.SelectedChefData selectedChefData = user.SelectedChefData;
            int player = ClientUserSystem.m_Users.FindIndex(x => x == user);
            if (player != -1)
            {
                byte diyChefID = DIYChefCustomisation.userDIYChefID[player];
                var diyChef = DIYChefCustomisation.diyChefs.Find(x => x.id == diyChefID);
                if (diyChef != null)
                {
                    selectedChefData = new GameSession.SelectedChefData(diyChef, selectedChefData.Colour);
                }
            }

            chef.SetChefData(selectedChefData, _force);
            chef.SetChefHat(HatMeshVisibility.VisState.Fancy);
            chef.SetShaderMode(FrontendChef.ShaderMode.eUI);
            chef.SetUIChefAmbientLighting(__instance.get_m_AmbientColor());
            if (player != -1)
                chef.SetAnimationSet((FrontendChef.AnimationSet)player);

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FrontendPlayerLobby), "RecalculateChefAvatars")]
        public static bool FrontendPlayerLobbyRecalculateChefAvatarsPatch(FrontendPlayerLobby __instance, bool _force)
        {
            if (__instance.m_PlayerChefs == null) return false;
            for (int i = 0; i < __instance.m_PlayerChefs.Length; i++)
            {
                FrontendChef y = __instance.m_PlayerChefs[i];
                if (y == null) continue;
                if (i < ClientUserSystem.m_Users.Count)
                {
                    User user = ClientUserSystem.m_Users._items[i];
                    GameSession.SelectedChefData selectedChefData = user.SelectedChefData;
                    byte diyChefID = DIYChefCustomisation.userDIYChefID[i];
                    var diyChef = DIYChefCustomisation.diyChefs.Find(x => x.id == diyChefID);
                    if (diyChef != null)
                    {
                        selectedChefData = new GameSession.SelectedChefData(diyChef, selectedChefData.Colour);
                    }
                    if (selectedChefData != null)
                        __instance.m_PlayerChefs[i].SetChefData(selectedChefData, _force);
                    __instance.m_PlayerChefs[i].SetChefHat(__instance.m_ChefHat);
                    __instance.m_PlayerChefs[i].SetAnimationSet((FrontendChef.AnimationSet)i);
                    __instance.m_PlayerChefs[i].gameObject.SetActive(true);
                }
                else
                {
                    __instance.m_PlayerChefs[i].gameObject.SetActive(false);
                }
            }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ClientKitchenLoader), "ReplaceMesh")]
        public static bool ClientKitchenLoaderReplaceMeshPatch(uint uEntityID, ref GameSession.SelectedChefData selectedChef)
        {
            FastList<User> users = ClientUserSystem.m_Users;
            int player = users.FindIndex(x => x.EntityID == uEntityID || x.Entity2ID == uEntityID);
            if (player < 0 || player >= DIYChefCustomisation.userDIYChefID.Length) return true;
            byte diyChefID = DIYChefCustomisation.userDIYChefID[player];
            var diyChef = DIYChefCustomisation.diyChefs.Find(x => x.id == diyChefID);
            if (diyChef != null)
            {
                selectedChef = new GameSession.SelectedChefData(diyChef, selectedChef.Colour);
            }
            return true;
        }

    }
}
