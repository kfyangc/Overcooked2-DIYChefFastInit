using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace OC2DIYChef
{
    internal sealed class CachedTexture
    {
        internal int Width, Height, Format, MipCount;
        internal byte[] Pixels;
    }

    // This layer only handles files and managed data. It never calls Unity object APIs.
    internal static class AssetDiskCache
    {
        const int Magic = 0x46434844;
        // Increment when the parser or serialized representation changes.
        const int Schema = 1;
        const int MeshKind = 1, TextureKind = 2;
        const int HashLength = 32;
        const int PayloadOffset = 12 + HashLength * 2;
        internal static string Root;
        internal static bool Enabled = true;
        internal static Action<string> Log;
        static TextureWrite writer;

        internal static long PendingWriteBytes
        {
            get { var current = writer; return current == null || current.Completed ? 0 : current.Bytes; }
        }
        internal static bool WriterBusy { get { return writer != null && !writer.Completed; } }

        internal static byte[] Fingerprint(byte[] bytes)
        {
            using (var hash = new SHA256Managed()) return hash.ComputeHash(bytes);
        }
        static byte[] FingerprintFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = new SHA256Managed()) return hash.ComputeHash(stream);
        }
        static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
        internal static string CachePath(string source, int kind)
        {
            byte[] key = Fingerprint(Encoding.UTF8.GetBytes(Path.GetFullPath(source).ToUpperInvariant()));
            return Path.Combine(Root, BitConverter.ToString(key).Replace("-", "").ToLowerInvariant() +
                (kind == MeshKind ? ".mesh" : ".texture"));
        }
        static void Warning(string path, Exception error)
        {
            if (Log != null) Log("Disk cache fallback " + path + ": " + error.Message);
        }

        static BinaryReader Open(string source, byte[] fingerprint, int kind)
        {
            if (!Enabled || string.IsNullOrEmpty(Root)) return null;
            string path = CachePath(source, kind);
            if (!File.Exists(path)) return null;
            BinaryReader reader = null;
            try
            {
                reader = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Schema || reader.ReadInt32() != kind ||
                    !Equal(reader.ReadBytes(HashLength), fingerprint))
                {
                    reader.Close();
                    return null;
                }
                byte[] expected = reader.ReadBytes(HashLength);
                byte[] actual;
                using (var hash = new SHA256Managed()) actual = hash.ComputeHash(reader.BaseStream);
                if (!Equal(actual, expected)) throw new FormatException("payload checksum mismatch");
                reader.BaseStream.Position = PayloadOffset;
                return reader;
            }
            catch (Exception ex)
            {
                if (reader != null) reader.Close();
                Warning(path, ex);
                return null;
            }
        }

        static void Write(string source, byte[] fingerprint, int kind, Action<BinaryWriter> payload)
        {
            if (!Enabled || string.IsNullOrEmpty(Root)) return;
            string path = CachePath(source, kind);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Root);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var output = new BinaryWriter(stream))
                {
                    output.Write(Magic); output.Write(Schema); output.Write(kind);
                    output.Write(fingerprint); output.Write(new byte[HashLength]);
                    payload(output);
                    output.Flush();
                    stream.Position = PayloadOffset;
                    byte[] digest;
                    using (var hash = new SHA256Managed()) digest = hash.ComputeHash(stream);
                    stream.Position = 12 + HashLength;
                    output.Write(digest);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (Exception ex) { Warning(path, ex); }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) { Warning(temporary, ex); }
            }
        }

        internal static ObjImporter.MeshData ReadMesh(string source, Func<bool> cancelled, out bool hit)
        {
            hit = false;
            byte[] fingerprint = Enabled ? FingerprintFile(source) : null;
            using (var input = Enabled ? Open(source, fingerprint, MeshKind) : null)
                if (input != null)
                {
                    try
                    {
                        int vertices = input.ReadInt32(), indices = input.ReadInt32();
                        long expected = checked((long)vertices * 32 + (long)indices * 4);
                        if (vertices < 0 || indices < 0 || indices % 3 != 0 ||
                            input.BaseStream.Length - input.BaseStream.Position != expected)
                            throw new FormatException("invalid geometry counts");
                        var data = new ObjImporter.MeshData { Vertices = new Vector3[vertices],
                            Normals = new Vector3[vertices], UV = new Vector2[vertices], Triangles = new int[indices] };
                        for (int i = 0; i < vertices; i++)
                        {
                            if ((i & 1023) == 0 && cancelled != null && cancelled()) throw new OperationCanceledException();
                            data.Vertices[i] = new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
                            data.Normals[i] = new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
                            data.UV[i] = new Vector2(input.ReadSingle(), input.ReadSingle());
                        }
                        for (int i = 0; i < indices; i++)
                        {
                            int index = input.ReadInt32();
                            if (index < 0 || index >= vertices) throw new FormatException("invalid triangle index");
                            data.Triangles[i] = index;
                        }
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

        internal static CachedTexture ReadTexture(string source, byte[] fingerprint)
        {
            using (var input = Open(source, fingerprint, TextureKind))
            {
                if (input == null) return null;
                try
                {
                    var texture = new CachedTexture { Width = input.ReadInt32(), Height = input.ReadInt32(),
                        Format = input.ReadInt32(), MipCount = input.ReadInt32() };
                    int length = input.ReadInt32();
                    if (texture.Width < 1 || texture.Height < 1 || texture.MipCount < 1 || length < 0 ||
                        input.BaseStream.Length - input.BaseStream.Position != length)
                        throw new FormatException("invalid texture dimensions or payload");
                    // PNG imports use these uncompressed formats; validate the entire mip chain.
                    int channels = texture.Format == (int)TextureFormat.RGB24 ? 3 :
                        texture.Format == (int)TextureFormat.RGBA32 || texture.Format == (int)TextureFormat.ARGB32 ||
                        texture.Format == (int)TextureFormat.BGRA32 ? 4 : 0;
                    long expected = 0;
                    int width = texture.Width, height = texture.Height, mips = 0;
                    do
                    {
                        expected += (long)width * height * channels;
                        mips++;
                        if (width == 1 && height == 1) break;
                        width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
                    } while (texture.MipCount > 1);
                    if (channels == 0 || mips != texture.MipCount || expected != length)
                        throw new FormatException("invalid texture format or mip chain");
                    texture.Pixels = input.ReadBytes(length);
                    return texture;
                }
                catch (Exception ex) { Warning(source, ex); return null; }
            }
        }

        internal static void WriteTexture(string source, byte[] fingerprint, CachedTexture texture)
        {
            Write(source, fingerprint, TextureKind, delegate(BinaryWriter output)
            {
                output.Write(texture.Width); output.Write(texture.Height); output.Write(texture.Format);
                output.Write(texture.MipCount); output.Write(texture.Pixels.Length); output.Write(texture.Pixels);
            });
        }

        internal static void QueueTextureWrite(string source, byte[] fingerprint, CachedTexture texture)
        {
            if (!Enabled || WriterBusy) return;
            var request = new TextureWrite { Bytes = texture.Pixels.LongLength };
            writer = request;
            new Thread(delegate()
            {
                try { WriteTexture(source, fingerprint, texture); }
                finally { request.Completed = true; }
            }) { IsBackground = true, Name = "DIYChef disk cache" }.Start();
        }

        sealed class TextureWrite
        {
            internal long Bytes;
            internal volatile bool Completed;
        }
    }
}
