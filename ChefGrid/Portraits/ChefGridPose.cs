using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace OC2DIYChef
{
    internal static class ChefGridPose
    {
        static readonly MethodInfo selectHead = AccessTools.Method(typeof(ChefMeshReplacer), "SetHeadVisibility");

        internal static bool IsOpenEye(string name) { return ChefGridVisibility.IsOpenEye(name); }

        internal static void Prepare(ChefMeshReplacer replacer, ChefAvatarData avatar)
        {
            var model = replacer.ChefModel;
            foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            // Match MOD Manager's static preview: serialized transforms, without sampling a
            // controller's blink/visibility tracks. Use this chef's complete prefab, including
            // custom eye renderers, rather than the first Eyes bone in its body template.
            var prefab = avatar.FrontendModelPrefab;
            if (prefab != null) RestoreChildren(model.transform, prefab.transform);
            selectHead.Invoke(replacer, new object[] { avatar.HeadName });
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (ChefGridVisibility.IsBlink(skin.name)) skin.enabled = false;
                else if (IsOpenEye(skin.name))
                {
                    // An imported eye object can inherit the template head's inactive flag.
                    // Enabling its renderer alone does not make that object visible.
                    skin.gameObject.SetActive(true);
                    skin.enabled = true;
                }
            }
        }

        static void RestoreChildren(Transform target, Transform source)
        {
            for (int i = 0; i < source.childCount; i++)
            {
                var rest = source.GetChild(i);
                var child = i < target.childCount ? target.GetChild(i) : null;
                if (child == null || child.name != rest.name) child = target.Find(rest.name);
                if (child == null) continue; // Runtime hat attachments need not exist in the prefab.
                child.localPosition = rest.localPosition;
                child.localRotation = rest.localRotation;
                child.localScale = rest.localScale;
                var original = rest.GetComponent<SkinnedMeshRenderer>();
                var skin = child.GetComponent<SkinnedMeshRenderer>();
                if (original != null && skin != null && original.sharedMesh != null && skin.sharedMesh != null)
                {
                    skin.enabled = original.enabled;
                    for (int shape = 0; shape < original.sharedMesh.blendShapeCount; shape++)
                        skin.SetBlendShapeWeight(shape, original.GetBlendShapeWeight(shape));
                }
                RestoreChildren(child, rest);
            }
        }
    }
}
