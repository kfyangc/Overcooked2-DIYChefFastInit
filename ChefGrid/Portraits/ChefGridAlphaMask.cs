using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    // Native chef UI shaders may write RGB only. Fill the visible geometry's alpha
    // after the normal colour pass, entirely on the GPU, using Unity's UI shader.
    internal sealed class ChefGridAlphaMask : IDisposable
    {
        readonly Camera camera;
        readonly Material material;
        readonly CommandBuffer commands;
        bool disposed;

        internal ChefGridAlphaMask(Camera camera)
        {
            this.camera = camera;
            try
            {
                var shader = Shader.Find("UI/Default");
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("UI alpha mask shader unavailable");
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetInt("_ColorMask", (int)ColorWriteMask.Alpha);
                material.SetColor("_Color", Color.white);
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                commands = new CommandBuffer { name = "DIYChef preview alpha" };
                camera.AddCommandBuffer(CameraEvent.AfterEverything, commands);
            }
            catch { Dispose(); throw; }
        }

        internal void Prepare(GameObject model)
        {
            commands.Clear();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer is ParticleSystemRenderer ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                // Match the opaque native chef geometry, including newly attached hats.
                // Keep original RGB, materials and animation untouched.
                for (int i = 0; i < renderer.sharedMaterials.Length; i++) commands.DrawRenderer(renderer, material, i, 0);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (commands != null)
            {
                camera.RemoveCommandBuffer(CameraEvent.AfterEverything, commands);
                commands.Release();
            }
            if (material != null) Object.Destroy(material);
        }
    }
}
