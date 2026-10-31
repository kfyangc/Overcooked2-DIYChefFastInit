using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Parse is worker-safe: it only creates managed arrays and Unity value types.
// CreateMesh is the only entry that touches Unity objects, on the main thread.
public static class ObjImporter
{
    public sealed class MeshData
    {
        public Vector3[] Vertices, Normals;
        public Vector2[] UV;
        public int[] Triangles;
        public long EstimatedBytes { get { return Vertices.LongLength * 32 + Triangles.LongLength * 4; } }
    }

    static readonly char[] WhiteSpace = { ' ', '\t' };
    static float Float(string value)
    {
        float result = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (float.IsNaN(result) || float.IsInfinity(result)) throw new FormatException("Non-finite OBJ coordinate");
        return result;
    }

    static int Index(string value, int count)
    {
        int index = int.Parse(value, CultureInfo.InvariantCulture);
        index = index < 0 ? count + index : index - 1;
        if (index < 0 || index >= count) throw new FormatException("OBJ index outside source array");
        return index;
    }

    public static MeshData ParseFile(string path, Func<bool> cancelled)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        var output = new List<Vector3>();
        var outNormals = new List<Vector3>();
        var outUV = new List<Vector2>();
        var triangles = new List<int>();
        using (var reader = File.OpenText(path))
        {
            string line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null)
            {
                if ((++lineNumber & 255) == 0 && cancelled != null && cancelled())
                    throw new OperationCanceledException();
                int comment = line.IndexOf('#');
                if (comment >= 0) line = line.Substring(0, comment);
                string[] fields = line.Split(WhiteSpace, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 0) continue;
                switch (fields[0])
                {
                    case "v":
                        positions.Add(new Vector3(-Float(fields[1]), Float(fields[2]), Float(fields[3])));
                        break;
                    case "vn":
                        normals.Add(new Vector3(-Float(fields[1]), Float(fields[2]), Float(fields[3])));
                        break;
                    case "vt":
                        uv.Add(new Vector2(Float(fields[1]), Float(fields[2])));
                        break;
                    case "f":
                        if (fields.Length < 4) throw new FormatException("OBJ face needs at least three vertices");
                        int start = output.Count;
                        for (int i = 1; i < fields.Length; i++)
                        {
                            string[] indices = fields[i].Split('/');
                            output.Add(positions[Index(indices[0], positions.Count)]);
                            outUV.Add(indices.Length > 1 && indices[1].Length != 0 ? uv[Index(indices[1], uv.Count)] : Vector2.zero);
                            outNormals.Add(indices.Length > 2 && indices[2].Length != 0 ? normals[Index(indices[2], normals.Count)] : Vector3.zero);
                        }
                        for (int i = 1; i < fields.Length - 2; i++)
                        {
                            triangles.Add(start);
                            triangles.Add(start + i + 1);
                            triangles.Add(start + i);
                        }
                        break;
                }
            }
        }
        // Empty optional parts intentionally hide a template part. Large parts use
        // Unity 2017.4's 32-bit index format rather than truncating vertex indices.
        return new MeshData { Vertices = output.ToArray(), Normals = outNormals.ToArray(),
            UV = outUV.ToArray(), Triangles = triangles.ToArray() };
    }

    public static Mesh CreateMesh(MeshData data)
    {
        return CreateMesh(data, null, null);
    }

    public static Mesh CreateMesh(MeshData data, BoneWeight[] weights, Matrix4x4[] bindposes)
    {
        var mesh = new Mesh();
        try
        {
            if (data.Vertices.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = data.Vertices;
            mesh.uv = data.UV;
            mesh.normals = data.Normals;
            // Supply the complete vertex layout before its first graphics upload.
            if (bindposes != null) mesh.bindposes = bindposes;
            if (weights != null) mesh.boneWeights = weights;
            mesh.triangles = data.Triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
        catch { UnityEngine.Object.Destroy(mesh); throw; }
    }

    public static Mesh ImportFile(string path) { return CreateMesh(ParseFile(path, null)); }
}
