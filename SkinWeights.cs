using UnityEngine;

namespace OC2DIYChef
{
    internal static class SkinWeights
    {
        // The two body bones retain the original plugin's height-based deformation.
        // Store influences in descending weight order, as required by Unity's skin layout.
        internal static BoneWeight[] Create(Vector3[] vertices, bool body)
        {
            var weights = new BoneWeight[vertices.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                if (!body)
                {
                    weights[i].boneIndex0 = 0;
                    weights[i].weight0 = 1;
                    continue;
                }
                float y = vertices[i].y;
                float top = y > 0.54 ? 1 : y > 0.46 ? 0.75f :
                    y > 0.38 ? 0.5f : y > 0.30 ? 0.3f : y > 0.22 ? 0.1f : 0;
                if (top >= 0.5f)
                {
                    weights[i].boneIndex0 = 0; weights[i].weight0 = top;
                    weights[i].boneIndex1 = 1; weights[i].weight1 = 1 - top;
                }
                else
                {
                    weights[i].boneIndex0 = 1; weights[i].weight0 = 1 - top;
                    weights[i].boneIndex1 = 0; weights[i].weight1 = top;
                }
            }
            return weights;
        }

    }
}
