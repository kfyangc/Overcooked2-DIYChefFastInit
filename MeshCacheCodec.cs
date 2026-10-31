using System;
using System.IO;
using UnityEngine;

namespace OC2DIYChef
{
    internal static class MeshCacheCodec
    {
        // Schema 1 stores eight little-endian floats per vertex, then Int32 indices.
        // On the Windows player, read those floats directly instead of making one
        // BinaryReader/FillBuffer/MemoryStream call for every scalar component.
        internal static unsafe ObjImporter.MeshData Decode(byte[] bytes, int vertices, int indices, Func<bool> cancelled)
        {
            if (vertices < 0 || indices < 0 || indices % 3 != 0 ||
                (long)vertices * 32 + (long)indices * 4 != bytes.LongLength)
                throw new FormatException("invalid geometry buffer");
            var data = new ObjImporter.MeshData { Vertices = new Vector3[vertices],
                Normals = new Vector3[vertices], UV = new Vector2[vertices], Triangles = new int[indices] };
            if (!BitConverter.IsLittleEndian) return DecodePortable(bytes, data, cancelled);
            fixed (byte* raw = bytes)
            fixed (Vector3* positions = data.Vertices, normals = data.Normals)
            fixed (Vector2* uv = data.UV)
            {
                float* source = (float*)raw;
                for (int i = 0; i < vertices; i++, source += 8)
                {
                    if ((i & 1023) == 0 && cancelled != null && cancelled()) throw new OperationCanceledException();
                    positions[i].x = source[0]; positions[i].y = source[1]; positions[i].z = source[2];
                    normals[i].x = source[3]; normals[i].y = source[4]; normals[i].z = source[5];
                    uv[i].x = source[6]; uv[i].y = source[7];
                }
            }
            Buffer.BlockCopy(bytes, vertices * 32, data.Triangles, 0, indices * 4);
            for (int i = 0; i < indices; i++)
            {
                if ((i & 1023) == 0 && cancelled != null && cancelled()) throw new OperationCanceledException();
                if (data.Triangles[i] < 0 || data.Triangles[i] >= vertices)
                    throw new FormatException("invalid triangle index");
            }
            return data;
        }

        static ObjImporter.MeshData DecodePortable(byte[] bytes, ObjImporter.MeshData data, Func<bool> cancelled)
        {
            using (var fields = new BinaryReader(new MemoryStream(bytes, false)))
            {
                for (int i = 0; i < data.Vertices.Length; i++)
                {
                    if ((i & 1023) == 0 && cancelled != null && cancelled()) throw new OperationCanceledException();
                    data.Vertices[i] = new Vector3(fields.ReadSingle(), fields.ReadSingle(), fields.ReadSingle());
                    data.Normals[i] = new Vector3(fields.ReadSingle(), fields.ReadSingle(), fields.ReadSingle());
                    data.UV[i] = new Vector2(fields.ReadSingle(), fields.ReadSingle());
                }
                for (int i = 0; i < data.Triangles.Length; i++)
                {
                    int index = fields.ReadInt32();
                    if (index < 0 || index >= data.Vertices.Length) throw new FormatException("invalid triangle index");
                    data.Triangles[i] = index;
                }
            }
            return data;
        }
    }
}
