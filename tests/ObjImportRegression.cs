using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using OC2DIYChef;
using UnityEngine;

static class ObjImportRegression
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static object Field(object obj, string name) { return obj.GetType().GetField(name).GetValue(obj); }
    static void Main(string[] args)
    {
        string resources = args[0];
        int files = 0, vertices = 0;
        var baseline = typeof(BaselineObjImporter).GetMethod("CreateMeshStruct", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (string path in Directory.GetFiles(resources, "*.obj", SearchOption.AllDirectories))
        {
            object old = baseline.Invoke(null, new object[] { path });
            var actual = ObjImporter.ParseFile(path, null);
            var positions = (Vector3[])Field(old, "vertices");
            var normals = (Vector3[])Field(old, "normals");
            var uv = (Vector2[])Field(old, "uv");
            var faces = (Array)Field(old, "faceData");
            Require(faces.Length == actual.Vertices.Length, "Expanded vertex count: " + path);
            Require(((int[])Field(old, "triangles")).SequenceEqual(actual.Triangles), "Triangle order: " + path);
            for (int i = 0; i < faces.Length; i++)
            {
                object face = faces.GetValue(i);
                int p = (int)Field(face, "i"), u = (int)Field(face, "j"), n = (int)Field(face, "k");
                var expectedPosition = positions[p - 1];
                var expectedUV = u > 0 ? uv[u - 1] : Vector2.zero;
                var expectedNormal = n > 0 ? normals[n - 1] : Vector3.zero;
                Require(expectedPosition.Equals(actual.Vertices[i]), "Position " + i + ": " + path);
                Require(expectedUV.Equals(actual.UV[i]), "UV " + i + ": " + path);
                Require(expectedNormal.Equals(actual.Normals[i]), "Normal " + i + ": " + path);
            }
            vertices += faces.Length;
            files++;
        }
        // Verify the body's semantic bone influences at every transition, and Unity's sorted order.
        var heights = new[] { 0.6f, 0.5f, 0.42f, 0.34f, 0.26f, 0.1f };
        var topWeights = new[] { 1f, 0.75f, 0.5f, 0.3f, 0.1f, 0f };
        var points = heights.Select(y => new Vector3(0, y, 0)).ToArray();
        var body = SkinWeights.Create(points, true);
        for (int i = 0; i < points.Length; i++)
        {
            var weight = body[i];
            float top = weight.boneIndex0 == 0 ? weight.weight0 : weight.weight1;
            Require(Math.Abs(top - topWeights[i]) < 0.000001, "Body top influence " + i);
            Require(Math.Abs(weight.weight0 + weight.weight1 - 1) < 0.000001, "Normalized body influence " + i);
            Require(weight.weight0 >= weight.weight1, "Sorted influence " + i);
            Require(weight.boneIndex0 < 2 && weight.boneIndex1 < 2, "Body bone palette " + i);
        }
        foreach (var weight in SkinWeights.Create(points, false))
            Require(weight.weight0 == 1 && weight.boneIndex0 == 0, "Rigid part uses exactly one bone");
        Console.WriteLine("PASS: baseline geometry matches for " + files + " OBJ files / " + vertices +
            " expanded vertices; rigid and body binding weights validated. No Unity objects or game process created.");
    }
}
