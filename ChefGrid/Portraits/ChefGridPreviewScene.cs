using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    // Identifies render-only instances to optional integrations, without naming real player chefs.
    public sealed class ChefGridPortraitActor : MonoBehaviour { }

    // Internal seam shared by the static and animated renderers. This module owns
    // the native chef setup, its pending load, camera, lighting and GPU targets;
    // callers only supply pose/framing and decide when to render.
    internal sealed class ChefGridPreviewScene : IDisposable
    {
        static readonly FieldInfo cachedAnimator = AccessTools.Field(typeof(FrontendChef), "m_ChefAnimator");
        static readonly int choosingChef = Animator.StringToHash("TrigChooseChef");
        // Native frontend P1 yaw 158.3638°, viewed by its zero-yaw camera.
        // Our portrait camera faces 180°, so retain the same relative turn.
        internal const float FixedYaw = -21.6362f;
        internal readonly GameObject Stage, Actor;
        internal readonly FrontendChef View;
        internal readonly ChefMeshReplacer Replacer;
        internal readonly Camera Camera;
        readonly List<RenderTexture> targets = new List<RenderTexture>();
        bool disposed;

        internal ChefGridPreviewScene(FrontendChefCustomisation source, Vector3 position, Color background)
        {
            Stage = new GameObject("DIYChef preview scene");
            Stage.transform.position = position;
            try
            {
                var original = (ChefMeshReplacer)AccessTools.Field(typeof(FrontendChefCustomisation), "m_chef").GetValue(source);
                var template = AccessTools.Field(typeof(ChefMeshReplacer), "m_chefTemplatePrefab");
                Actor = new GameObject("Preview chef");
                Actor.transform.SetParent(Stage.transform, false);
                Actor.transform.localRotation = Quaternion.Euler(0, FixedYaw, 0);
                Actor.AddComponent<ChefGridPortraitActor>();
                var placeholder = new GameObject("Chef");
                placeholder.transform.SetParent(Actor.transform, false);
                Replacer = Actor.AddComponent<ChefMeshReplacer>();
                template.SetValue(Replacer, template.GetValue(original));
                AccessTools.Field(typeof(ChefMeshReplacer), "m_modelType").SetValue(Replacer, ChefMeshReplacer.ChefModelType.FrontEnd);
                var originalHat = source.GetComponent<HatMeshVisibility>() ?? original.GetComponent<HatMeshVisibility>();
                if (originalHat != null)
                {
                    var hats = Actor.AddComponent<HatMeshVisibility>();
                    hats.m_meshes = originalHat.m_meshes;
                    hats.m_stateFlags = originalHat.m_stateFlags;
                }
                View = Actor.AddComponent<FrontendChef>();
                var originalView = source.GetComponent<FrontendChef>() ?? original.GetComponent<FrontendChef>();
                if (originalView != null)
                    foreach (string name in new[] { "m_StandardSkinShader", "m_StandardClothesShader", "m_UISkinShader", "m_UIClothesShader" })
                    {
                        var field = AccessTools.Field(typeof(FrontendChef), name);
                        field.SetValue(View, field.GetValue(originalView));
                    }
                View.SetShaderMode(FrontendChef.ShaderMode.eUI);
                View.SetUIChefAmbientLighting(new Color(0.8f, 0.8f, 0.8f, 1));
                var cameraObject = new GameObject("Preview camera");
                cameraObject.transform.SetParent(Stage.transform, false);
                Camera = cameraObject.AddComponent<Camera>();
                Camera.enabled = false;
                Camera.orthographic = true;
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = background;
                Camera.useOcclusionCulling = false;
                Camera.nearClipPlane = 0.01f;
                Camera.farClipPlane = 30;
                Camera.cullingMask = (1 << Actor.layer) | (1 << HatData.hatLayer);
                var lamp = new GameObject("Preview light");
                lamp.transform.SetParent(Stage.transform, false);
                lamp.transform.localPosition = new Vector3(-2, 3, 4);
                var light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 12;
                light.intensity = 2;
                light.shadows = LightShadows.None;
            }
            catch { Dispose(); throw; }
        }

        internal RenderTexture CreateTarget(int size, RenderTextureFormat format)
        {
            if (disposed) throw new ObjectDisposedException("ChefGridPreviewScene");
            var target = new RenderTexture(size, size, 24, format);
            targets.Add(target); // Own it even if GPU allocation fails.
            target.Create();
            return target;
        }

        internal void PlaySelectingAnimation(int slot)
        {
            // Native SetChefData caches this animator before discovering the
            // AnimationSet parameter, but only discovers it when customisation
            // is present. Our render-only actor intentionally has no such menu.
            // Let SetCorrectAnimations acquire it again and perform its own check.
            cachedAnimator.SetValue(View, null);
            View.SetAnimationSet((FrontendChef.AnimationSet)slot);
            // Same trigger as FrontendChefCustomisation.PlaySelectingAnimation.
            // AnimationSet alone enters the van's normal idle, not its selector pose.
            var animator = cachedAnimator.GetValue(View) as Animator;
            if (animator != null && animator.HasParameter(choosingChef)) animator.SetTrigger(choosingChef);
        }

        internal void SilenceModel()
        {
            var model = View.ChefModel;
            foreach (var audio in model.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
            foreach (var audio in model.GetComponentsInChildren<AnimatorAudioComponent>(true)) audio.enabled = false;
            foreach (var rumble in model.GetComponentsInChildren<AnimatorRumbleComponent>(true)) rumble.enabled = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Replacer != null) ChefLoading.Cancel(Replacer);
            if (Camera != null) Camera.targetTexture = null;
            foreach (var target in targets)
                if (target != null) { target.Release(); Object.Destroy(target); }
            targets.Clear();
            if (Stage != null) { Stage.SetActive(false); Object.Destroy(Stage); }
        }
    }
}
