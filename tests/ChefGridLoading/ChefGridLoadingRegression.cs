using System;
using OC2DIYChef;
using UnityEngine;
using UnityEngine.UI;

static class ChefGridLoadingRegression
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    static void Main()
    {
        Check(ChefGridLoadingIndicator.FindArtwork() == null, "unloaded native artwork selects the ring fallback");
        var original = new Sprite { name = "t_UIlobbyRefreshArrow_01" };
        Resources.Sprites = new[] { new Sprite { name = "Unrelated" }, original };
        Check(ChefGridLoadingIndicator.FindArtwork() == original, "loading borrows the exact native refresh sprite");
        var loading = new ChefGridLoadingIndicator { color = new Color(0.1f, 0.2f, 0.3f, 0.8f) };
        var mesh = new VertexHelper();
        loading.Populate(mesh);
        Check(mesh.Positions.Count == 48 && mesh.Indices.Count == 72 && loading.NativeBuilds == 0,
            "missing artwork draws a twelve-segment ring with complete triangles");
        Check(Math.Abs(mesh.Colours[0].a - 0.16f) < 0.001f && Math.Abs(mesh.Colours[47].a - 0.8f) < 0.001f &&
            mesh.Colours[0].r == loading.color.r, "the ring has a visible fading tail and retains its requested tint");
        foreach (var position in mesh.Positions)
        {
            float radius = (float)Math.Sqrt(position.x * position.x + position.y * position.y);
            Check(Math.Abs(radius - 13) < 0.001f || Math.Abs(radius - 8.06f) < 0.001f,
                "ring vertices fit inside the thumbnail and leave a hollow center");
        }
        foreach (int index in mesh.Indices) Check(index >= 0 && index < mesh.Positions.Count, "triangle indices refer to ring vertices");
        loading.Populate(mesh);
        Check(mesh.Positions.Count == 48, "canvas rebuild replaces the previous ring without accumulating geometry");
        Time.unscaledTime = 1; loading.Tick();
        var first = loading.rectTransform.localRotation;
        Time.unscaledTime = 1.25f; loading.Tick();
        Check(first.z == -240 && loading.rectTransform.localRotation.z == -300 && mesh.Positions.Count == 48,
            "unscaled time rotates the indicator without rebuilding its geometry");
        loading.sprite = original; loading.Populate(mesh);
        Check(loading.NativeBuilds == 1 && mesh.Positions.Count == 0 && loading.sprite == original,
            "available artwork uses native Image rendering instead of drawing a second ring");
        loading.sprite = null; loading.Populate(mesh);
        Check(mesh.Positions.Count == 48, "unloading borrowed artwork restores the drawable fallback");
        Console.WriteLine("PASS: " + checks + " loading artwork, fallback mesh and unscaled animation checks; no game process used.");
    }
}
