using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OC2DIYChef
{
    internal static class ChefGridPose
    {
        static readonly MethodInfo selectHead = AccessTools.Method(typeof(ChefMeshReplacer), "SetHeadVisibility");

        internal static bool IsOpenEye(string name) { return name == "Eyes"; }
        internal static bool IsBlink(string name)
        {
            return name.StartsWith("Eyes2_Blinks", StringComparison.OrdinalIgnoreCase);
        }

        internal static void Prepare(ChefMeshReplacer replacer, ChefAvatarData avatar)
        {
            var model = replacer.ChefModel;
            foreach (var animator in model.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0);
                animator.enabled = false;
            }
            // A controller rebind can reset animated visibility; restore the selected chef explicitly.
            selectHead.Invoke(replacer, new object[] { avatar.HeadName });
            var custom = avatar as DIYChefAvatarData;
            var prefab = custom == null ? avatar.FrontendModelPrefab : custom.template.FrontendModelPrefab;
            Transform openRest = null;
            if (prefab != null)
                foreach (var skin in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    foreach (var bone in skin.bones)
                        if (bone != null && IsOpenEye(bone.name)) { openRest = bone; break; }
                    if (openRest != null) break;
                }
            var visited = new HashSet<Transform>();
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsBlink(skin.name)) skin.enabled = false;
                else if (IsOpenEye(skin.name)) skin.enabled = true;
                foreach (var bone in skin.bones)
                {
                    if (bone == null || !visited.Add(bone)) continue;
                    if (IsBlink(bone.name)) bone.localScale = Vector3.zero;
                    else if (IsOpenEye(bone.name))
                    {
                        if (openRest != null)
                        {
                            bone.localPosition = openRest.localPosition;
                            bone.localRotation = openRest.localRotation;
                        }
                        bone.localScale = openRest != null && openRest.localScale.sqrMagnitude > 0 ? openRest.localScale : Vector3.one;
                    }
                }
            }
        }
    }
}
