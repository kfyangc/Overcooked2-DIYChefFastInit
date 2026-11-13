using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OC2DIYChef
{
    internal static partial class AssetDiskCache
    {
        internal static ObjImporter.MeshData ReadMesh(string source, Func<bool> cancelled, out bool hit)
        {
            hit = false;
            byte[] fingerprint = Enabled ? FingerprintFile(source) : null;
            using (var input = Enabled ? Open(source, fingerprint, MeshKind) : null)
                if (input != null)
                {
                    try
                    {
                        byte[] counts = input.ReadCheckedBytes(8);
                        int vertices = BitConverter.ToInt32(counts, 0), indices = BitConverter.ToInt32(counts, 4);
                        long expected = checked((long)vertices * 32 + (long)indices * 4);
                        if (vertices < 0 || indices < 0 || indices % 3 != 0 || expected > int.MaxValue ||
                            input.BaseStream.Length - input.BaseStream.Position != expected)
                            throw new FormatException("invalid geometry counts");
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        byte[] geometry = input.ReadCheckedBytes((int)expected);
                        input.Verify();
                        long decodeStart = Stopwatch.GetTimestamp();
                        var data = MeshCacheCodec.Decode(geometry, vertices, indices, cancelled);
                        if (Timings != null) Timings.MeshDecodeTicks += Stopwatch.GetTimestamp() - decodeStart;
                        hit = true;
                        return data;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { Warning(source, ex); }
                }
            var parsed = ObjImporter.ParseFile(source, cancelled);
            if (cancelled != null && cancelled()) throw new OperationCanceledException();
            Write(source, fingerprint, MeshKind, delegate(BinaryWriter output)
            {
                output.Write(parsed.Vertices.Length); output.Write(parsed.Triangles.Length);
                for (int i = 0; i < parsed.Vertices.Length; i++)
                {
                    output.Write(parsed.Vertices[i].x); output.Write(parsed.Vertices[i].y); output.Write(parsed.Vertices[i].z);
                    output.Write(parsed.Normals[i].x); output.Write(parsed.Normals[i].y); output.Write(parsed.Normals[i].z);
                    output.Write(parsed.UV[i].x); output.Write(parsed.UV[i].y);
                }
                foreach (int index in parsed.Triangles) output.Write(index);
            });
            return parsed;
        }

    }
}
