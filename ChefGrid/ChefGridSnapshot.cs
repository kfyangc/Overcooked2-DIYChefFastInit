using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    // Render the very same baked geometry that is measured for framing. This avoids the
    // original renderer's bone-space bounds and offscreen skinning state culling closeups.
    internal sealed class ChefGridSnapshot : IDisposable
    {
        sealed class Part
        {
            internal SkinnedMeshRenderer Source;
            internal GameObject Proxy;
            internal Mesh Mesh;
        }
        readonly List<Part> parts = new List<Part>();
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();

        internal Renderer Freeze(Renderer source)
        {
            var skin = source as SkinnedMeshRenderer;
            if (skin == null || skin.sharedMesh == null) return source;
            var part = new Part { Source = skin, Mesh = new Mesh {
                name = "DIYChef portrait snapshot", hideFlags = HideFlags.HideAndDontSave } };
            parts.Add(part);
            skin.BakeMesh(part.Mesh);
            part.Mesh.RecalculateBounds();
            part.Proxy = new GameObject(source.name + " portrait", typeof(MeshFilter), typeof(MeshRenderer));
            part.Proxy.hideFlags = HideFlags.HideAndDontSave;
            part.Proxy.layer = source.gameObject.layer;
            part.Proxy.transform.SetParent(source.transform, false);
            part.Proxy.GetComponent<MeshFilter>().sharedMesh = part.Mesh;
            var renderer = part.Proxy.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = source.sharedMaterials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            properties.Clear();
            source.GetPropertyBlock(properties);
            renderer.SetPropertyBlock(properties);
            skin.enabled = false;
            return renderer;
        }

        public void Dispose()
        {
            foreach (var part in parts)
            {
                if (part.Proxy != null) { part.Proxy.SetActive(false); Object.Destroy(part.Proxy); }
                if (part.Source != null) part.Source.enabled = true;
                if (part.Mesh != null) Object.Destroy(part.Mesh);
            }
            parts.Clear();
        }
    }
}
