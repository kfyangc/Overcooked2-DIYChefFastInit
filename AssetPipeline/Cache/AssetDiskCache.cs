using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OC2DIYChef
{
    // This layer only handles files and managed data. It never calls Unity object APIs.
    internal static partial class AssetDiskCache
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
            internal long SourceHashTicks, CacheReadTicks, PayloadHashTicks, MeshDecodeTicks, TextureDecodeTicks;
            internal double SourceHashMs { get { return SourceHashTicks * 1000.0 / Stopwatch.Frequency; } }
            internal double CacheReadMs { get { return CacheReadTicks * 1000.0 / Stopwatch.Frequency; } }
            internal double PayloadHashMs { get { return PayloadHashTicks * 1000.0 / Stopwatch.Frequency; } }
            internal double CacheMs { get { return CacheReadMs + PayloadHashMs; } }
            internal double TextureDecodeMs { get { return TextureDecodeTicks * 1000.0 / Stopwatch.Frequency; } }
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
            return OpenPath(CachePath(source, kind), fingerprint, kind);
        }

        static CacheReader OpenPath(string path, byte[] fingerprint, int kind)
        {
            if (!File.Exists(path)) return null;
            BinaryReader reader = null;
            try
            {
                reader = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
                int magic = reader.ReadInt32(), schema = reader.ReadInt32(), actualKind = reader.ReadInt32();
                if (magic != Magic || actualKind != kind ||
                    (schema != Schema && (kind != TextureKind || schema != CompressedTextureSchema)))
                { reader.Close(); return null; }
                byte[] sourceHash = reader.ReadBytes(HashLength);
                if (sourceHash.Length != HashLength || fingerprint != null && !Equal(sourceHash, fingerprint))
                { reader.Close(); return null; }
                byte[] expected = reader.ReadBytes(HashLength);
                if (expected.Length != HashLength) throw new EndOfStreamException();
                return new CacheReader(reader.BaseStream, expected, schema, sourceHash);
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
            WritePath(CachePath(source, kind), fingerprint, kind, payload);
        }

        // Generated assets already have a versioned content key in their filename.
        // Share the container and atomic publication without hashing that path again.
        static void WritePath(string path, byte[] fingerprint, int kind, Action<BinaryWriter> payload)
        {
            if (!Enabled) return;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
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
                lock (publishGate)
                {
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
            }
            catch (Exception ex) { Warning(path, ex); }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) { Warning(temporary, ex); }
            }
        }

        sealed class CacheReader : BinaryReader
        {
            readonly byte[] expected;
            AssetHash checksum;
            AssetHash Checksum { get { if (checksum == null) checksum = AssetHash.Create(); return checksum; } }
            internal readonly int Schema;
            internal readonly byte[] SourceHash;
            internal byte[] Expected { get { return expected; } }
            internal CacheReader(Stream source, byte[] digest, int schema, byte[] sourceHash) : base(source)
            { expected = digest; Schema = schema; SourceHash = sourceHash; }
            internal byte[] ReadCheckedBytes(int length)
            {
                long start = Stopwatch.GetTimestamp();
                byte[] bytes = ReadBytes(length);
                if (bytes.Length != length) throw new EndOfStreamException();
                long readEnd = Stopwatch.GetTimestamp();
                Checksum.Append(bytes, 0, bytes.Length);
                if (Timings != null)
                {
                    Timings.CacheReadTicks += readEnd - start;
                    Timings.PayloadHashTicks += Stopwatch.GetTimestamp() - readEnd;
                }
                return bytes;
            }
            internal void Verify()
            {
                if (BaseStream.Position != BaseStream.Length || !Equal(Checksum.Finish(), expected))
                    throw new FormatException("payload checksum mismatch");
            }
            internal NativePixelBuffer ReadCheckedPixels(int length, Func<bool> cancelled)
            {
                long start = Stopwatch.GetTimestamp();
                var pixels = NativePixelBuffer.Read(BaseStream, length, cancelled);
                try
                {
                    long readEnd = Stopwatch.GetTimestamp();
                    Checksum.Append(pixels.Pointer, pixels.Length);
                    if (Timings != null)
                    {
                        Timings.CacheReadTicks += readEnd - start;
                        Timings.PayloadHashTicks += Stopwatch.GetTimestamp() - readEnd;
                    }
                    return pixels;
                }
                catch { pixels.Dispose(); throw; }
            }
            internal NativePixelBuffer ReadCompressedPixels(int storedLength, int length, Func<bool> cancelled)
            {
                long start = Stopwatch.GetTimestamp();
                using (var encoded = NativePixelBuffer.Read(BaseStream, storedLength, cancelled))
                {
                    long readEnd = Stopwatch.GetTimestamp();
                    var pixels = new NativePixelBuffer(length);
                    try
                    {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        Lz4Codec.Decompress(encoded.Pointer, storedLength, pixels.Pointer, length);
                        long decoded = Stopwatch.GetTimestamp();
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        Checksum.Append(pixels.Pointer, length);
                        if (Timings != null)
                        {
                            Timings.CacheReadTicks += readEnd - start;
                            Timings.TextureDecodeTicks += decoded - readEnd;
                            Timings.PayloadHashTicks += Stopwatch.GetTimestamp() - decoded;
                        }
                        return pixels;
                    }
                    catch { pixels.Dispose(); throw; }
                }
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing && checksum != null) checksum.Dispose();
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
