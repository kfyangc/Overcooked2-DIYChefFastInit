using HarmonyLib;
using System;
using System.Collections.Generic;
using Team17.Online;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    // One animated actor per local participant. No ReadPixels, PNG encoding or
    // cache writes occur during playback; RawImage consumes the GPU target.
    internal sealed class ChefGridLivePreview : IDisposable
    {
        readonly GameObject stage, actor;
        readonly FrontendChef view;
        readonly ChefMeshReplacer replacer;
        readonly Camera camera;
        readonly RenderTexture target;
        readonly Mesh measure = new Mesh();
        readonly List<Vector3> vertices = new List<Vector3>();
        ChefAvatarData requested;
        GameObject prepared;
        float yaw = float.NaN;
        int changedFrame;
        float started;
        string framedHat;
        bool ready, failed;
        internal bool Failed { get { return failed; } }
        internal Texture Image { get { return ready ? target : null; } }

        internal ChefGridLivePreview(FrontendChefCustomisation source, int slot, Color background)
        {
            stage = new GameObject("DIYChef live preview " + slot);
            // Spatial isolation keeps the four cameras and thumbnail studio apart.
            stage.transform.position = new Vector3(10000 + (slot + 1) * 100, -10000, 10000);
            try
            {
                var original = (ChefMeshReplacer)AccessTools.Field(typeof(FrontendChefCustomisation), "m_chef").GetValue(source);
                var template = AccessTools.Field(typeof(ChefMeshReplacer), "m_chefTemplatePrefab");
                actor = new GameObject("Animated chef");
                actor.transform.SetParent(stage.transform, false);
                actor.AddComponent<ChefGridPortraitActor>();
                var placeholder = new GameObject("Chef");
                placeholder.transform.SetParent(actor.transform, false);
                replacer = actor.AddComponent<ChefMeshReplacer>();
                template.SetValue(replacer, template.GetValue(original));
                AccessTools.Field(typeof(ChefMeshReplacer), "m_modelType").SetValue(replacer, ChefMeshReplacer.ChefModelType.FrontEnd);
                var originalHat = source.GetComponent<HatMeshVisibility>() ?? original.GetComponent<HatMeshVisibility>();
                if (originalHat != null)
                {
                    var hats = actor.AddComponent<HatMeshVisibility>();
                    hats.m_meshes = originalHat.m_meshes; hats.m_stateFlags = originalHat.m_stateFlags;
                }
                view = actor.AddComponent<FrontendChef>();
                var originalView = source.GetComponent<FrontendChef>() ?? original.GetComponent<FrontendChef>();
                if (originalView != null)
                    foreach (string name in new[] { "m_StandardSkinShader", "m_StandardClothesShader", "m_UISkinShader", "m_UIClothesShader" })
                    {
                        var field = AccessTools.Field(typeof(FrontendChef), name);
                        field.SetValue(view, field.GetValue(originalView));
                    }
                view.SetShaderMode(FrontendChef.ShaderMode.eUI);
                view.SetUIChefAmbientLighting(new Color(0.8f, 0.8f, 0.8f, 1));
                view.SetAnimationSet((FrontendChef.AnimationSet)slot);
                var cameraObject = new GameObject("Live camera");
                cameraObject.transform.SetParent(stage.transform, false);
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = background;
                camera.useOcclusionCulling = false;
                camera.nearClipPlane = 0.01f; camera.farClipPlane = 30;
                camera.cullingMask = (1 << actor.layer) | (1 << HatData.hatLayer);
                // Native UI chef shaders have unreliable output alpha. An RGB
                // target supplies alpha=1 and matches the panel's paper background.
                var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float)
                    ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.RGB565;
                target = new RenderTexture(512, 512, 24, format);
                target.filterMode = FilterMode.Bilinear; target.wrapMode = TextureWrapMode.Clamp;
                target.Create(); camera.targetTexture = target;
                var lamp = new GameObject("Live preview light");
                lamp.transform.SetParent(stage.transform, false);
                lamp.transform.localPosition = new Vector3(-2, 3, 4);
                var light = lamp.AddComponent<Light>();
                light.type = LightType.Point; light.range = 12; light.intensity = 2; light.shadows = LightShadows.None;
            }
            catch { Dispose(); throw; }
        }

        internal void Select(ChefAvatarData avatar, User user, float angle)
        {
            if (failed && requested == avatar) return;
            try
            {
                PlayerHats.Bind(actor, user);
                if (requested != avatar)
                {
                    failed = false;
                    requested = avatar; ready = false; prepared = null;
                    changedFrame = Time.frameCount;
                    started = Time.realtimeSinceStartup;
                    view.SetChefData(new GameSession.SelectedChefData(avatar, user.SelectedChefData.Colour), true);
                }
                if (yaw != angle)
                {
                    yaw = angle;
                    actor.transform.localRotation = Quaternion.Euler(0, angle, 0);
                    prepared = null;
                }
            }
            catch (Exception ex)
            {
                failed = true; ready = false;
                DIYChefPlugin.Warn("Live chef selection failed: " + ex);
            }
        }

        internal void Render()
        {
            if (failed || requested == null || Time.frameCount <= changedFrame) return;
            try
            {
                var data = replacer.GetChefData();
                if (data == null || data.Character != requested || view.ChefModel == null)
                {
                    var custom = requested as DIYChefAvatarData;
                    if ((custom != null && custom.Resources.Failed(ChefMeshReplacer.ChefModelType.FrontEnd)) ||
                        Time.realtimeSinceStartup - started > 30) throw new InvalidOperationException("Chef model could not be prepared");
                    return;
                }
                var model = view.ChefModel;
                var owner = actor.GetComponent<PlayerHatOwner>();
                if (owner != null && framedHat != owner.AppliedHat) { prepared = null; framedHat = owner.AppliedHat; }
                if (prepared != model)
                {
                    foreach (var animator in model.GetComponentsInChildren<Animator>(true))
                    {
                        animator.enabled = true;
                        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    }
                    foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
                    foreach (var audio in model.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
                    foreach (var audio in model.GetComponentsInChildren<AnimatorAudioComponent>(true)) audio.enabled = false;
                    foreach (var rumble in model.GetComponentsInChildren<AnimatorRumbleComponent>(true)) rumble.enabled = false;
                    view.SetChefHat(HatMeshVisibility.VisState.Fancy);
                    FrameModel(model);
                    prepared = model;
                }
                var previous = RenderTexture.active;
                try { camera.Render(); }
                finally { RenderTexture.active = previous; }
                ready = true;
            }
            catch (Exception ex)
            {
                failed = true; ready = false;
                DIYChefPlugin.Warn("Live chef preview failed: " + ex);
            }
        }

        void FrameModel(GameObject model)
        {
            var bounds = new Bounds(); bool any = false;
            var parts = new List<Bounds>();
            var head = new ChefPortraitBounds();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer is ParticleSystemRenderer) continue;
                Mesh mesh = null;
                var skin = renderer as SkinnedMeshRenderer;
                if (skin != null) { measure.Clear(); skin.BakeMesh(measure); mesh = measure; }
                else { var filter = renderer.GetComponent<MeshFilter>(); if (filter != null) mesh = filter.sharedMesh; }
                if (mesh == null || !mesh.isReadable) continue;
                vertices.Clear(); mesh.GetVertices(vertices);
                var matrix = stage.transform.worldToLocalMatrix * renderer.localToWorldMatrix;
                var part = new Bounds(); bool hasPart = false;
                foreach (var vertex in vertices)
                {
                    var point = matrix.MultiplyPoint3x4(vertex);
                    if (!hasPart) { part = new Bounds(point, Vector3.zero); hasPart = true; }
                    else part.Encapsulate(point);
                }
                if (!hasPart) continue;
                parts.Add(part);
                if (renderer.name == requested.HeadName)
                { head.Include(part.min.x, part.min.y); head.Include(part.max.x, part.max.y); }
            }
            foreach (var part in parts)
            {
                var plane = new ChefPortraitBounds();
                plane.Include(part.min.x, part.min.y); plane.Include(part.max.x, part.max.y);
                if (!ContributesToFrame(plane, head)) continue;
                if (!any) { bounds = part; any = true; }
                else bounds.Encapsulate(part);
            }
            if (!any) throw new InvalidOperationException("No visible chef geometry");
            // Keep framing stable during idle animation; leave room for head sway.
            camera.orthographicSize = Mathf.Max(0.1f, Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.18f);
            camera.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, bounds.max.z + 8);
            camera.transform.localRotation = Quaternion.Euler(0, 180, 0);
        }

        internal void Reframe() { prepared = null; }

        static bool ContributesToFrame(ChefPortraitBounds part, ChefPortraitBounds head)
        {
            if (!part.Valid) return false;
            if (!head.Valid) return true;
            // Optional parts exported far away must not make the live chef tiny.
            float margin = Math.Max(0.01f, Math.Max(head.Width, head.Height)) * 8;
            return part.MaxX >= head.MinX - margin && part.MinX <= head.MaxX + margin &&
                part.MaxY >= head.MinY - margin && part.MinY <= head.MaxY + margin;
        }

        public void Dispose()
        {
            if (replacer != null) ChefLoading.Cancel(replacer);
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.Destroy(target); }
            Object.Destroy(measure);
            if (stage != null) { stage.SetActive(false); Object.Destroy(stage); }
        }
    }
}
