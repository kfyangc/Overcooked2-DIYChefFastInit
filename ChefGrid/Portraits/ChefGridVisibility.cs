using System;
using System.Collections.Generic;

namespace OC2DIYChef
{
    [Flags]
    internal enum ChefGridVertexRole : byte { None = 0, Head = 1, Eyes = 2, Blink = 4 }

    // Pure index processing shared by snapshot rendering and offline regression fixtures.
    internal static class ChefGridVisibility
    {
        internal static bool IsOpenEye(string name) { return name == "Eyes"; }
        internal static bool IsBlink(string name)
        {
            return name != null && name.StartsWith("Eyes2_Blinks", StringComparison.OrdinalIgnoreCase);
        }

        internal static ChefGridVertexRole BoneRole(string name)
        {
            if (IsBlink(name)) return ChefGridVertexRole.Blink;
            if (IsOpenEye(name)) return ChefGridVertexRole.Head | ChefGridVertexRole.Eyes;
            return name == "Head" || name == "Eyebrows" || name == "Hair" ? ChefGridVertexRole.Head : ChefGridVertexRole.None;
        }

        internal static ChefGridVertexRole VertexRole(float headWeight, float eyeWeight, float blinkWeight)
        {
            // Small neck/torso blend weights must not turn an entire merged body into a head.
            return (headWeight >= 0.5f ? ChefGridVertexRole.Head : ChefGridVertexRole.None) |
                (eyeWeight > 0 ? ChefGridVertexRole.Eyes : ChefGridVertexRole.None) |
                (blinkWeight > 0 ? ChefGridVertexRole.Blink : ChefGridVertexRole.None);
        }

        internal static int[] FilterTriangles(int[] indices, bool[] hidden)
        {
            var visible = new List<int>(indices.Length);
            for (int i = 0; i < indices.Length; i += 3)
                if (!hidden[indices[i]] && !hidden[indices[i + 1]] && !hidden[indices[i + 2]])
                {
                    visible.Add(indices[i]); visible.Add(indices[i + 1]); visible.Add(indices[i + 2]);
                }
            return visible.ToArray();
        }

        // Return the original vertex indices used by the visible faces; rewrite owned index
        // buffers to the compact layout. Unreferenced eyelid vertices cannot inflate framing.
        internal static int[] CompactIndices(int[][] submeshes, int vertexCount)
        {
            var remap = new int[vertexCount];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var used = new List<int>();
            foreach (var indices in submeshes)
                for (int i = 0; i < indices.Length; i++)
                {
                    int old = indices[i];
                    if (remap[old] < 0) { remap[old] = used.Count; used.Add(old); }
                    indices[i] = remap[old];
                }
            return used.ToArray();
        }

        internal static T[] Compact<T>(T[] values, int[] used)
        {
            if (values.Length == 0) return values;
            var result = new T[used.Length];
            for (int i = 0; i < result.Length; i++) result[i] = values[used[i]];
            return result;
        }
    }
}
