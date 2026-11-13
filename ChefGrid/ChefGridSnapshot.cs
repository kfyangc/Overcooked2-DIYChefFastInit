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
            RemoveBlinks(skin, part.Mesh);
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

        static void RemoveBlinks(SkinnedMeshRenderer skin, Mesh mesh)
        {
            var bones = skin.bones;
            var blinkBones = new bool[bones.Length];
            bool hasBlink = false;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && ChefGridVisibility.IsBlink(bones[i].name))
                    hasBlink = blinkBones[i] = true;
            if (!hasBlink) return;
            var weights = skin.sharedMesh.boneWeights;
            if (weights.Length != mesh.vertexCount) return;
            var hidden = new bool[weights.Length];
            bool anyHidden = false;
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                hidden[i] = ChefGridVisibility.IsHidden(w.boneIndex0, w.weight0, blinkBones) ||
                    ChefGridVisibility.IsHidden(w.boneIndex1, w.weight1, blinkBones) ||
                    ChefGridVisibility.IsHidden(w.boneIndex2, w.weight2, blinkBones) ||
                    ChefGridVisibility.IsHidden(w.boneIndex3, w.weight3, blinkBones);
                anyHidden |= hidden[i];
            }
            if (!anyHidden) return;
            // Native heads combine open eyes, eyebrows and eyelids in one renderer. The
            // alternate layer is identified by skin weights, as in MOD Manager's preview.
            var submeshes = new int[mesh.subMeshCount][];
            for (int i = 0; i < submeshes.Length; i++)
                submeshes[i] = ChefGridVisibility.FilterTriangles(mesh.GetTriangles(i), hidden);
            var used = ChefGridVisibility.CompactIndices(submeshes, mesh.vertexCount);
            var vertices = ChefGridVisibility.Compact(mesh.vertices, used);
            var normals = ChefGridVisibility.Compact(mesh.normals, used);
            var tangents = ChefGridVisibility.Compact(mesh.tangents, used);
            var colours = ChefGridVisibility.Compact(mesh.colors32, used);
            var uv = ChefGridVisibility.Compact(mesh.uv, used);
            var uv2 = ChefGridVisibility.Compact(mesh.uv2, used);
            var uv3 = ChefGridVisibility.Compact(mesh.uv3, used);
            var uv4 = ChefGridVisibility.Compact(mesh.uv4, used);
            var format = mesh.indexFormat;
            mesh.Clear(false);
            mesh.indexFormat = format;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.colors32 = colours;
            mesh.uv = uv; mesh.uv2 = uv2; mesh.uv3 = uv3; mesh.uv4 = uv4;
            mesh.subMeshCount = submeshes.Length;
            for (int i = 0; i < submeshes.Length; i++) mesh.SetTriangles(submeshes[i], i);
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
