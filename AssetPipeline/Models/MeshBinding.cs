using System;
using UnityEngine;

namespace OC2DIYChef
{
    internal static class MeshBinding
    {
        internal static void Bind(ResourceCache resources, ObjImporter.MeshData geometry,
            SkinnedMeshRenderer renderer, string name, Transform[] bones, bool body)
        {
            var bindposes = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) throw new FormatException(name + ": missing binding bone");
                bindposes[i] = BindPoses.GetMatrix(bones[i].name);
                if (Math.Abs(bindposes[i].determinant) < 0.00001f)
                    throw new FormatException(name + ": no bind pose for " + bones[i].name);
            }
            var weights = SkinWeights.Create(geometry.Vertices, body);
            Mesh mesh = resources.Own(ObjImporter.CreateMesh(geometry, weights, bindposes),
                (geometry.EstimatedBytes + geometry.Vertices.LongLength * 48) * 2);
            mesh.name = name;
            // Detach imported renderer data before assigning the new skin layout.
            renderer.sharedMesh = null;
            renderer.bones = bones;
            renderer.quality = body ? SkinQuality.Bone2 : SkinQuality.Bone1;
            renderer.sharedMesh = mesh;
            renderer.localBounds = mesh.bounds;
            // Keep CPU mesh data for the target player's skinning path. Eviction owns its lifetime.
            // In particular, do not UploadMeshData(true) on a prefab before cloning its renderer.
            if (ChefLoading.Diagnostics)
                DIYChefPlugin.Log("Bind " + resources.Name + "/" + name + " vertices=" + geometry.Vertices.Length +
                    " bones=" + bones.Length + " readable=" + mesh.isReadable);
        }
    }
}
