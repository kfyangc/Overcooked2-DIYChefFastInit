using UnityEngine;
using UnityEngine.UI;

namespace OC2DIYChef
{
    // Borrow the native lobby refresh artwork when loaded; otherwise draw a small
    // ring directly in the canvas. Both paths rotate without rebuilding meshes.
    internal sealed class ChefGridLoadingIndicator : Image
    {
        internal static Sprite FindArtwork()
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Sprite>())
                if (candidate.name == "t_UIlobbyRefreshArrow_01") return candidate;
            return null;
        }

        void Update()
        {
            rectTransform.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 240);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            if (sprite != null) { base.OnPopulateMesh(mesh); return; }
            mesh.Clear();
            var rect = rectTransform.rect;
            float radius = Mathf.Min(rect.width, rect.height) / 2;
            const int segments = 12;
            for (int i = 0; i < segments; i++)
            {
                float start = (i + 0.08f) * Mathf.PI * 2 / segments;
                float end = (i + 0.92f) * Mathf.PI * 2 / segments;
                var tint = color;
                tint.a *= 0.2f + 0.8f * i / (segments - 1);
                int first = mesh.currentVertCount;
                mesh.AddVert(Point(rect.center, radius, start), tint, Vector2.zero);
                mesh.AddVert(Point(rect.center, radius, end), tint, Vector2.zero);
                mesh.AddVert(Point(rect.center, radius * 0.62f, end), tint, Vector2.zero);
                mesh.AddVert(Point(rect.center, radius * 0.62f, start), tint, Vector2.zero);
                mesh.AddTriangle(first, first + 1, first + 2);
                mesh.AddTriangle(first + 2, first + 3, first);
            }
        }

        static Vector3 Point(Vector2 center, float radius, float angle)
        {
            return new Vector3(center.x + Mathf.Cos(angle) * radius, center.y + Mathf.Sin(angle) * radius, 0);
        }
    }
}
