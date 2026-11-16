using HarmonyLib;
using System;
using System.Globalization;
using UnityEngine;

namespace OC2DIYChef
{
    // Temporary, read-only comparison of the native chef and the isolated actor.
    // Two samples after a selection settles; no render/animation state is changed.
    internal sealed class ChefGridRenderTrace
    {
        const string Tag = "[DEBUG-grid-eyes] ";
        readonly FrontendChefCustomisation source;
        readonly int slot;
        ChefAvatarData previous;
        GameObject previousModel;
        float nextSample;
        int samples;
        bool stopped;

        internal ChefGridRenderTrace(FrontendChefCustomisation source, int slot)
        { this.source = source; this.slot = slot; }

        internal void Observe(ChefGridPreviewScene scene, ChefAvatarData avatar)
        {
            if (stopped) return;
            try
            {
                var model = scene.View.ChefModel;
                if (previous != avatar || previousModel != model)
                {
                    previous = avatar; previousModel = model;
                    nextSample = Time.realtimeSinceStartup + 0.75f; samples = 0;
                }
                if (samples >= 2 || Time.realtimeSinceStartup < nextSample) return;
                samples++; nextSample = Time.realtimeSinceStartup + 2;
                Write("sample=" + samples + " player=" + (slot + 1) + " requested=" + avatar.HeadName +
                    " frame=" + Time.frameCount + " unity=" + Application.unityVersion + " graphics=" + SystemInfo.graphicsDeviceType);
                Dump("grid", scene.Replacer, scene.View, avatar.HeadName);
                var native = source == null ? null :
                    (ChefMeshReplacer)AccessTools.Field(typeof(FrontendChefCustomisation), "m_chef").GetValue(source);
                Dump("native", native, native == null ? null : native.GetComponent<FrontendChef>(), avatar.HeadName);
                DumpCamera("grid", scene.Camera);
                foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>())
                    if (camera.enabled && (native == null || (camera.cullingMask & (1 << native.gameObject.layer)) != 0))
                        DumpCamera("native-candidate", camera);
                Write("end player=" + (slot + 1) + " requested=" + avatar.HeadName);
            }
            catch (Exception error)
            {
                stopped = true; // Diagnostics must never break or repeatedly delay rendering.
                DIYChefPlugin.Warn(Tag + "trace stopped: " + error.GetType().Name + ": " + error.Message);
            }
        }

        static void Dump(string kind, ChefMeshReplacer replacer, FrontendChef view, string requested)
        {
            if (replacer == null || replacer.ChefModel == null) { Write(kind + " model=<missing>"); return; }
            var model = replacer.ChefModel;
            var data = replacer.GetChefData();
            string actual = data == null || data.Character == null ? "<none>" : data.Character.HeadName;
            Write(kind + " actual=" + actual + " mode=" + (view == null ? "<none>" : view.CurrentShaderMode.ToString()) +
                " actor{" + TransformState(replacer.transform) + "} model{" + TransformState(model.transform) + "}");
            if (view != null)
                foreach (string fieldName in new[] { "m_StandardSkinShader", "m_StandardClothesShader", "m_UISkinShader", "m_UIClothesShader" })
                {
                    var shader = AccessTools.Field(typeof(FrontendChef), fieldName).GetValue(view) as Shader;
                    Write(kind + " " + fieldName + "=" + (shader == null ? "<null>" : shader.name));
                }
            foreach (var animator in model.GetComponentsInChildren<Animator>(true))
            {
                string state = "<inactive>";
                if (animator.isActiveAndEnabled && animator.runtimeAnimatorController != null && animator.layerCount > 0)
                {
                    var current = animator.GetCurrentAnimatorStateInfo(0);
                    state = current.fullPathHash + ":" + F(current.normalizedTime);
                }
                Write(kind + " animator=" + animator.name + " enabled=" + animator.enabled + " active=" + animator.gameObject.activeInHierarchy +
                    " controller=" + (animator.runtimeAnimatorController == null ? "<null>" : animator.runtimeAnimatorController.name) +
                    " update=" + animator.updateMode + " culling=" + animator.cullingMode + " rootMotion=" + animator.applyRootMotion + " state=" + state);
            }
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                bool selectedHead = renderer.name == requested || renderer.name == actual;
                bool eyePart = renderer.name.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    renderer.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!selectedHead && (!eyePart || (!renderer.gameObject.activeInHierarchy &&
                    !WithinHead(renderer.transform, requested) && !WithinHead(renderer.transform, actual)))) continue;
                Write(kind + " part=" + renderer.name + " enabled=" + renderer.enabled + " active=" + renderer.gameObject.activeInHierarchy +
                    " layer=" + renderer.gameObject.layer + " sorting=" + renderer.sortingLayerID + ":" + renderer.sortingOrder +
                    " bounds=" + V(renderer.bounds.min) + ".." + V(renderer.bounds.max) + " transform{" + TransformState(renderer.transform) + "}");
                var skin = renderer as SkinnedMeshRenderer;
                if (skin != null)
                {
                    var mesh = skin.sharedMesh;
                    Write(kind + " part=" + renderer.name + " mesh=" + (mesh == null ? "<null>" : mesh.name + "#" + mesh.GetInstanceID() +
                        " vertices=" + mesh.vertexCount + " submeshes=" + mesh.subMeshCount + " readable=" + mesh.isReadable) +
                        " offscreen=" + skin.updateWhenOffscreen + " skinQuality=" + skin.quality);
                    foreach (var bone in skin.bones)
                    {
                        if (bone == null || (bone.name != "Head" && bone.name != "Eyes" && bone.name != "Eyes2_Blinks" && bone.name != "Eyebrows")) continue;
                        Write(kind + " part=" + renderer.name + " bone=" + bone.name + " rendererRelative=" +
                            V(renderer.transform.InverseTransformPoint(bone.position)) + " transform{" + TransformState(bone) + "}");
                    }
                }
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    string values = "";
                    foreach (string property in new[] { "_ZWrite", "_ZTest", "_Cull", "_Mode", "_SrcBlend", "_DstBlend" })
                        if (material.HasProperty(property)) values += " " + property + "=" + F(material.GetFloat(property));
                    Write(kind + " part=" + renderer.name + " material=" + material.name + " shader=" +
                        (material.shader == null ? "<null>" : material.shader.name) + " queue=" + material.renderQueue +
                        " renderType=" + material.GetTag("RenderType", false) + values);
                }
            }
        }

        static bool WithinHead(Transform node, string head)
        {
            for (; node != null; node = node.parent) if (node.name == head) return true;
            return false;
        }

        static void DumpCamera(string kind, Camera camera)
        {
            Write(kind + " camera=" + camera.name + " enabled=" + camera.enabled + " orthographic=" + camera.orthographic +
                " size=" + F(camera.orthographicSize) + " fov=" + F(camera.fieldOfView) + " near=" + F(camera.nearClipPlane) +
                " far=" + F(camera.farClipPlane) + " path=" + camera.actualRenderingPath + " clear=" + camera.clearFlags +
                " mask=" + camera.cullingMask + " transform{" + TransformState(camera.transform) + "}");
        }

        static string TransformState(Transform transform)
        {
            return "world=" + V(transform.position) + " local=" + V(transform.localPosition) +
                " rotation=" + V(transform.localEulerAngles) + " localScale=" + V(transform.localScale) + " worldScale=" + V(transform.lossyScale);
        }
        static string V(Vector3 value) { return F(value.x) + "," + F(value.y) + "," + F(value.z); }
        static string F(float value) { return value.ToString("F6", CultureInfo.InvariantCulture); }
        static void Write(string message) { DIYChefPlugin.Log(Tag + message); }
    }
}
