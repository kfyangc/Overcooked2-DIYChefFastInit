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
        readonly ChefGridPreviewScene scene;
        readonly RenderTexture target;
        readonly ChefGridAlphaMask alpha;
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

        internal ChefGridLivePreview(FrontendChefCustomisation source, int slot)
        {
            // Spatial isolation keeps the four cameras and thumbnail studio apart.
            try
            {
                scene = new ChefGridPreviewScene(source,
                    new Vector3(10000 + (slot + 1) * 100, -10000, 10000), Color.clear);
                scene.View.SetAnimationSet((FrontendChef.AnimationSet)slot);
                target = scene.CreateTarget(512, RenderTextureFormat.ARGB32);
                target.filterMode = FilterMode.Bilinear; target.wrapMode = TextureWrapMode.Clamp;
                scene.Camera.targetTexture = target;
                alpha = new ChefGridAlphaMask(scene.Camera);
            }
            catch { Dispose(); throw; }
        }

        internal void Select(ChefAvatarData avatar, User user, float angle)
        {
            if (failed && requested == avatar) return;
            try
            {
                PlayerHats.Bind(scene.Actor, user);
                if (requested != avatar)
                {
                    failed = false;
                    requested = avatar; ready = false; prepared = null;
                    changedFrame = Time.frameCount;
                    started = Time.realtimeSinceStartup;
                    scene.View.SetChefData(new GameSession.SelectedChefData(avatar, user.SelectedChefData.Colour), true);
                }
                if (yaw != angle)
                {
                    yaw = angle;
                    scene.Actor.transform.localRotation = Quaternion.Euler(0, angle, 0);
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
                var data = scene.Replacer.GetChefData();
                if (data == null || data.Character != requested || scene.View.ChefModel == null)
                {
                    var custom = requested as DIYChefAvatarData;
                    if ((custom != null && custom.Resources.Failed(ChefMeshReplacer.ChefModelType.FrontEnd)) ||
                        Time.realtimeSinceStartup - started > 30) throw new InvalidOperationException("Chef model could not be prepared");
                    return;
                }
                var model = scene.View.ChefModel;
                var owner = scene.Actor.GetComponent<PlayerHatOwner>();
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
                    scene.SilenceModel();
                    scene.View.SetChefHat(HatMeshVisibility.VisState.Fancy);
                    FrameModel(model);
                    prepared = model;
                }
                var previous = RenderTexture.active;
                alpha.Prepare(model);
                try { scene.Camera.Render(); }
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
                var matrix = scene.Stage.transform.worldToLocalMatrix * renderer.localToWorldMatrix;
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
                if (!ChefGridFraming.ContributesToFraming(plane, head)) continue;
                if (!any) { bounds = part; any = true; }
                else bounds.Encapsulate(part);
            }
            if (!any) throw new InvalidOperationException("No visible chef geometry");
            // Keep framing stable during idle animation; leave room for head sway.
            scene.Camera.orthographicSize = Mathf.Max(0.1f, Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.18f);
            scene.Camera.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, bounds.max.z + 8);
            scene.Camera.transform.localRotation = Quaternion.Euler(0, 180, 0);
        }

        public void Dispose()
        {
            Object.Destroy(measure);
            if (alpha != null) alpha.Dispose();
            if (scene != null) scene.Dispose();
        }
    }
}
