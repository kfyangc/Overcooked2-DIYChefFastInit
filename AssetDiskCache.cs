using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using UnityEngine;

namespace OC2DIYChef
{
    internal sealed class CachedTexture : IDisposable
    {
        internal int Width, Height, Format, MipCount;
        internal byte[] Pixels;
        internal NativePixelBuffer NativePixels;
        internal int PixelLength { get { return Pixels != null ? Pixels.Length : NativePixels != null ? NativePixels.Length : 0; } }
        public void Dispose() { if (NativePixels != null) NativePixels.Dispose(); }
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
        [ThreadStatic] internal static ReadTimings Timings;

        internal sealed class ReadTimings
        {
            internal long SourceHashTicks, CacheReadTicks, PayloadHashTicks, MeshDecodeTicks;
            internal double SourceHashMs { get { return SourceHashTicks * 1000.0 / Stopwatch.Frequency; } }
            internal double CacheReadMs { get { return CacheReadTicks * 1000.0 / Stopwatch.Frequency; } }
            internal double PayloadHashMs { get { return PayloadHashTicks * 1000.0 / Stopwatch.Frequency; } }
            internal double CacheMs { get { return CacheReadMs + PayloadHashMs; } }
            internal double MeshDecodeMs { get { return MeshDecodeTicks * 1000.0 / Stopwatch.Frequency; } }
        }

        internal static long PendingWriteBytes
        {
            get { var current = writer; return current == null || current.Completed ? 0 : current.Bytes; }
        }
        internal static bool WriterBusy { get { return writer != null && !writer.Completed; } }

        internal static byte[] Fingerprint(byte[] bytes)
        {
            return AssetHash.Compute(bytes);
        }
        static byte[] FingerprintFile(string path)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                using (var stream = File.OpenRead(path)) return AssetHash.Compute(stream);
            }
            finally { if (Timings != null) Timings.SourceHashTicks += Stopwatch.GetTimestamp() - start; }
        }
        static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
        internal static string CachePath(string source, int kind)
        {
            // A path is a tiny input. Do not acquire a native provider just to
            // derive the filename; the resulting SHA-256 and filenames stay identical.
            byte[] path = Encoding.UTF8.GetBytes(Path.GetFullPath(source).ToUpperInvariant());
            byte[] key;
            using (var digest = AssetHash.Create(false))
            {
                digest.Append(path, 0, path.Length);
                key = digest.Finish();
            }
            return Path.Combine(Root, BitConverter.ToString(key).Replace("-", "").ToLowerInvariant() +
                (kind == MeshKind ? ".mesh" : ".texture"));
        }
        static void Warning(string path, Exception error)
        {
            if (Log != null) Log("Disk cache fallback " + path + ": " + error.Message);
        }

        static CacheReader Open(string source, byte[] fingerprint, int kind)
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
                if (expected.Length != HashLength) throw new EndOfStreamException();
                // The payload is read once into its final pixels or temporary mesh buffer.
                // Validate those same bytes before any data is exposed to Unity.
                return new CacheReader(reader.BaseStream, expected);
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
                    output.Flush();
                    byte[] digest;
                    using (var sink = new HashingWriteStream(stream))
                    using (var buffered = new BufferedStream(sink, 65536))
                    using (var payloadOutput = new BinaryWriter(buffered))
                    {
                        payload(payloadOutput);
                        payloadOutput.Flush();
                        digest = sink.Finish();
                    }
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

        internal static CachedTexture ReadTexture(string source, byte[] fingerprint, bool nativePixels = false,
            Func<bool> cancelled = null)
        {
            using (var input = Open(source, fingerprint, TextureKind))
            {
                if (input == null) return null;
                try
                {
                    byte[] metadata = input.ReadCheckedBytes(20);
                    var texture = new CachedTexture { Width = BitConverter.ToInt32(metadata, 0), Height = BitConverter.ToInt32(metadata, 4),
                        Format = BitConverter.ToInt32(metadata, 8), MipCount = BitConverter.ToInt32(metadata, 12) };
                    int length = BitConverter.ToInt32(metadata, 16);
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
                    try
                    {
                        if (nativePixels) texture.NativePixels = input.ReadCheckedPixels(length, cancelled);
                        else texture.Pixels = input.ReadCheckedBytes(length);
                        input.Verify();
                        return texture;
                    }
                    catch { texture.Dispose(); throw; }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Warning(source, ex); return null; }
            }
        }

        internal static void WriteTexture(string source, byte[] fingerprint, CachedTexture texture)
        {
            Write(source, fingerprint, TextureKind, delegate(BinaryWriter output)
            {
                output.Write(texture.Width); output.Write(texture.Height); output.Write(texture.Format);
                output.Write(texture.MipCount); output.Write(texture.PixelLength);
                if (texture.Pixels != null) output.Write(texture.Pixels);
                else texture.NativePixels.WriteTo(output);
            });
        }

        internal static void QueueTextureWrite(string source, byte[] fingerprint, CachedTexture texture)
        {
            if (!Enabled || WriterBusy) return;
            var request = new TextureWrite { Bytes = texture.PixelLength };
            writer = request;
            new Thread(delegate()
            {
                try { WriteTexture(source, fingerprint, texture); }
                finally { request.Completed = true; }
            }) { IsBackground = true, Name = "DIYChef disk cache" }.Start();
        }

        sealed class CacheReader : BinaryReader
        {
            readonly byte[] expected;
            readonly AssetHash checksum = AssetHash.Create();
            internal CacheReader(Stream source, byte[] digest) : base(source) { expected = digest; }
            internal byte[] ReadCheckedBytes(int length)
            {
                long start = Stopwatch.GetTimestamp();
                byte[] bytes = ReadBytes(length);
                if (bytes.Length != length) throw new EndOfStreamException();
                long readEnd = Stopwatch.GetTimestamp();
                checksum.Append(bytes, 0, bytes.Length);
                if (Timings != null)
                {
                    Timings.CacheReadTicks += readEnd - start;
                    Timings.PayloadHashTicks += Stopwatch.GetTimestamp() - readEnd;
                }
                return bytes;
            }
            internal void Verify()
            {
                if (BaseStream.Position != BaseStream.Length || !Equal(checksum.Finish(), expected))
                    throw new FormatException("payload checksum mismatch");
            }
            internal NativePixelBuffer ReadCheckedPixels(int length, Func<bool> cancelled)
            {
                long start = Stopwatch.GetTimestamp();
                var pixels = NativePixelBuffer.Read(BaseStream, length, cancelled);
                try
                {
                    long readEnd = Stopwatch.GetTimestamp();
                    checksum.Append(pixels.Pointer, pixels.Length);
                    if (Timings != null)
                    {
                        Timings.CacheReadTicks += readEnd - start;
                        Timings.PayloadHashTicks += Stopwatch.GetTimestamp() - readEnd;
                    }
                    return pixels;
                }
                catch { pixels.Dispose(); throw; }
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) checksum.Dispose();
                base.Dispose(disposing);
            }
        }

        // BufferedStream batches tiny BinaryWriter writes before native hash calls.
        // The outer cache writer retains ownership of the file so it can update the header.
        sealed class HashingWriteStream : Stream
        {
            readonly Stream file;
            readonly AssetHash checksum = AssetHash.Create();
            internal HashingWriteStream(Stream file) { this.file = file; }
            internal byte[] Finish() { return checksum.Finish(); }
            public override void Write(byte[] buffer, int offset, int count)
            {
                file.Write(buffer, offset, count);
                checksum.Append(buffer, offset, count);
            }
            public override void Flush() { file.Flush(); }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long length) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing)
            {
                if (disposing) checksum.Dispose();
                base.Dispose(disposing);
            }
        }

        sealed class TextureWrite
        {
            internal long Bytes;
            internal volatile bool Completed;
        }
    }
}
