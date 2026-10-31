using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Security.Cryptography;
using OC2DIYChef;
using UnityEngine;

static class DiskCacheRegression
{
    static int assertions;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        assertions++;
    }
    internal static void Main(string[] args)
    {
        AssetDiskCache.BackgroundCompressionEnabled = false;
        AssetDiskCache.Log = Console.WriteLine;
        AssetHash.Warning = Console.WriteLine;
        AssetDiskCache.Timings = new AssetDiskCache.ReadTimings();
        if (args.Length == 3 && args[0] == "--verify-source")
        {
            AssetDiskCache.Root = args[2];
            int total = 0, existing = 0;
            foreach (string path in Directory.GetFiles(args[1], "*.obj", SearchOption.AllDirectories))
            {
                var sourceData = ObjImporter.ParseFile(path, null);
                bool cacheHit;
                var cachedData = AssetDiskCache.ReadMesh(path, null, out cacheHit);
                if (cacheHit) existing++;
                byte[] sourceBytes = EncodeMesh(sourceData), cachedBytes = EncodeMesh(cachedData);
                if (!sourceBytes.SequenceEqual(cachedBytes))
                {
                    int different = Enumerable.Range(0, Math.Min(sourceBytes.Length, cachedBytes.Length))
                        .First(i => sourceBytes[i] != cachedBytes[i]);
                    Console.WriteLine("First different float at byte " + different + " source=" +
                        BitConverter.ToSingle(sourceBytes, different / 4 * 4).ToString("R") + " cached=" +
                        BitConverter.ToSingle(cachedBytes, different / 4 * 4).ToString("R"));
                }
                Check(sourceBytes.SequenceEqual(cachedBytes),
                    "cached geometry is byte-identical to source parse: " + path);
                Check(EncodeMesh(AssetDiskCache.ReadMesh(path, null, out cacheHit)).SequenceEqual(sourceBytes) && cacheHit,
                    "second read hits the existing schema without changing geometry: " + path);
                total++;
            }
            Console.WriteLine("PASS: " + total + " mesh caches match source geometry byte for byte; existing=" + existing +
                "; created=" + (total - existing) + ".");
            return;
        }
        if (args.Length == 3 && args[0] == "--warm")
        {
            Warm(args[1], args[2]);
            return;
        }
        CheckHashing();
        CheckHashBatches();
        CheckMeshDecoder();
        string workspace = Path.GetFullPath(Path.Combine(Path.Combine("artifacts", "cache-regression"), Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(workspace);
        if (IntPtr.Size == 4) Check(Lz4Codec.Initialise(Path.Combine(workspace, "native"), Console.WriteLine), "embedded x86 LZ4 library loads");
        string source = Path.Combine(workspace, "中文 空格.obj");
        AssetDiskCache.Root = Path.Combine(workspace, "cache");
        string obj = "v 0 0 0\nv 0.25 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3 4\n";
        File.WriteAllText(source, obj);
        bool hit;
        var first = AssetDiskCache.ReadMesh(source, null, out hit);
        Check(!hit && first.Triangles.Length == 6, "cold source is parsed and cached");
        var second = AssetDiskCache.ReadMesh(source, null, out hit);
        Check(hit && first.Vertices.SequenceEqual(second.Vertices) &&
            first.Triangles.SequenceEqual(second.Triangles), "warm cache roundtrip preserves geometry");

        DateTime timestamp = File.GetLastWriteTimeUtc(source);
        File.WriteAllText(source, obj.Replace("0.25", "0.75"));
        File.SetLastWriteTimeUtc(source, timestamp);
        var changed = AssetDiskCache.ReadMesh(source, null, out hit);
        Check(!hit && changed.Vertices[1].x == -0.75f, "content hash detects same-size same-time source edits");

        string meshCache = AssetDiskCache.CachePath(source, 1);
        byte[] damaged = File.ReadAllBytes(meshCache);
        using (var sha = new SHA256Managed())
            Check(damaged.Skip(44).Take(32).SequenceEqual(sha.ComputeHash(damaged, 76, damaged.Length - 76)) &&
                BitConverter.ToInt32(damaged, 4) == 1, "one-pass writer preserves existing cache schema and SHA-256 bytes");
        damaged[damaged.Length - 1] ^= 1;
        File.WriteAllBytes(meshCache, damaged);
        var repaired = AssetDiskCache.ReadMesh(source, null, out hit);
        Check(!hit && repaired.Vertices[1].x == -0.75f, "corrupted payload falls back to source");
        AssetDiskCache.ReadMesh(source, null, out hit);
        Check(hit, "corrupted payload was rebuilt");
        File.WriteAllBytes(meshCache, new byte[] { 1, 2, 3 });
        AssetDiskCache.ReadMesh(source, null, out hit);
        Check(!hit, "truncated file falls back to source");
        damaged = File.ReadAllBytes(meshCache);
        damaged[4] ^= 1;
        File.WriteAllBytes(meshCache, damaged);
        AssetDiskCache.ReadMesh(source, null, out hit);
        Check(!hit, "cache schema changes trigger rebuild");

        bool cancelled = false;
        try { AssetDiskCache.ReadMesh(source, delegate { return true; }, out hit); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "warm read respects cancellation");

        string empty = Path.Combine(workspace, "empty.obj");
        File.WriteAllText(empty, "# intentionally hidden part\n");
        AssetDiskCache.ReadMesh(empty, null, out hit);
        Check(AssetDiskCache.ReadMesh(empty, null, out hit).Vertices.Length == 0 && hit,
            "empty optional meshes survive cache roundtrip");

        string png = Path.Combine(workspace, "贴图.png");
        byte[] fingerprint = AssetDiskCache.Fingerprint(new byte[] { 10, 20, 30 });
        foreach (var format in new[] { TextureFormat.RGBA32, TextureFormat.ARGB32, TextureFormat.BGRA32, TextureFormat.RGB24 })
        {
            int channels = format == TextureFormat.RGB24 ? 3 : 4;
            var pixels = Enumerable.Range(0, 5 * channels).Select(i => (byte)i).ToArray();
            var texture = new CachedTexture { Width = 2, Height = 2, Format = (int)format,
                MipCount = 2, Pixels = pixels };
            AssetDiskCache.WriteTexture(png, fingerprint, texture);
            var loaded = AssetDiskCache.ReadTexture(png, fingerprint);
            Check(loaded != null && loaded.Format == (int)format && loaded.MipCount == 2 &&
                loaded.Pixels.SequenceEqual(pixels), "decoded mip chain roundtrip: " + format);
        }
        Check(AssetDiskCache.ReadTexture(png, AssetDiskCache.Fingerprint(new byte[] { 30, 20, 10 })) == null,
            "changed PNG source invalidates decoded pixels");
        string textureCache = AssetDiskCache.CachePath(png, 2);
        damaged = File.ReadAllBytes(textureCache);
        damaged[damaged.Length - 1] ^= 1;
        File.WriteAllBytes(textureCache, damaged);
        Check(AssetDiskCache.ReadTexture(png, fingerprint) == null, "corrupt texture checksum falls back to PNG");

        var invalid = new CachedTexture { Width = 2, Height = 2, Format = (int)TextureFormat.RGBA32,
            MipCount = 3, Pixels = new byte[20] };
        AssetDiskCache.WriteTexture(png, fingerprint, invalid);
        Check(AssetDiskCache.ReadTexture(png, fingerprint) == null, "invalid mip count cannot reach Unity upload");
        invalid.MipCount = 2;
        AssetDiskCache.QueueTextureWrite(png, fingerprint, invalid);
        var wait = Stopwatch.StartNew();
        while (AssetDiskCache.WriterBusy && wait.ElapsedMilliseconds < 5000) Thread.Sleep(1);
        Check(!AssetDiskCache.WriterBusy && AssetDiskCache.PendingWriteBytes == 0 &&
            AssetDiskCache.ReadTexture(png, fingerprint) != null, "bounded background write completes and releases its buffer");

        AssetDiskCache.Enabled = false;
        AssetDiskCache.ReadMesh(source, null, out hit);
        Check(!hit && AssetDiskCache.ReadTexture(png, fingerprint) == null, "disabled cache uses original source");
        AssetDiskCache.Enabled = true;
        string root = AssetDiskCache.Root;
        AssetDiskCache.Root = source; // A file is not a writable cache directory.
        Check(AssetDiskCache.ReadMesh(source, null, out hit).Vertices.Length == 4 && !hit,
            "cache write failure does not fail source loading");
        AssetDiskCache.Root = root;
        CheckWorkerBatches(workspace);
        CheckNativePixels(workspace);
        if (Lz4Codec.Available) CheckCompressedTextures(workspace);
        Console.WriteLine("PASS: " + assertions + " disk cache assertions; no Unity objects or game process created.");
    }

    static void CheckHashing()
    {
        foreach (byte[] data in new[] { new byte[0], System.Text.Encoding.UTF8.GetBytes("abc"),
            Enumerable.Range(0, 131091).Select(i => (byte)(i * 17)).ToArray() })
        {
            byte[] expected;
            using (var sha = new SHA256Managed()) expected = sha.ComputeHash(data);
            Check(AssetHash.Compute(data).SequenceEqual(expected), "native/default SHA-256 matches reference");
            using (var fallback = AssetHash.Create(false))
            {
                fallback.Append(data, 0, data.Length);
                Check(fallback.Finish().SequenceEqual(expected), "managed fallback matches reference");
            }
        }
        byte[] buffer = Enumerable.Range(0, 131091).Select(i => (byte)(i * 17)).ToArray();
        byte[] reference;
        using (var sha = new SHA256Managed()) reference = sha.ComputeHash(buffer, 7, buffer.Length - 14);
        using (var incremental = AssetHash.Create())
        {
            incremental.Append(buffer, 7, 65536);
            incremental.Append(buffer, 65543, buffer.Length - 65550);
            Check(incremental.Finish().SequenceEqual(reference), "incremental native hashing handles nonzero offsets and chunk boundaries");
        }
        using (var source = new MemoryStream(buffer, false))
        {
            source.Position = 7;
            using (var sha = new SHA256Managed()) reference = sha.ComputeHash(buffer, 7, buffer.Length - 7);
            Check(AssetHash.Compute(source).SequenceEqual(reference) && source.Position == source.Length,
                "stream fingerprint hashes from current position to EOF");
        }
    }

    static byte[] EncodeMesh(ObjImporter.MeshData data)
    {
        using (var buffer = new MemoryStream())
        using (var writer = new BinaryWriter(buffer))
        {
            for (int i = 0; i < data.Vertices.Length; i++)
            {
                writer.Write(data.Vertices[i].x); writer.Write(data.Vertices[i].y); writer.Write(data.Vertices[i].z);
                writer.Write(data.Normals[i].x); writer.Write(data.Normals[i].y); writer.Write(data.Normals[i].z);
                writer.Write(data.UV[i].x); writer.Write(data.UV[i].y);
            }
            foreach (int index in data.Triangles) writer.Write(index);
            return buffer.ToArray();
        }
    }

    static void CheckHashBatches()
    {
        var metrics = AssetHash.Timings = new AssetHash.NativeTimings();
        byte[] a = System.Text.Encoding.UTF8.GetBytes("first independent digest");
        byte[] b = System.Text.Encoding.UTF8.GetBytes("第二份独立数据");
        byte[] expectedA, expectedB;
        using (var sha = new SHA256Managed()) { expectedA = sha.ComputeHash(a); expectedB = sha.ComputeHash(b); }
        bool native;
        using (AssetHash.BeginBatch())
        using (var first = AssetHash.Create())
        {
            native = first.IsNative;
            first.Append(a, 0, 5);
            using (var second = AssetHash.Create())
            {
                second.Append(b, 0, b.Length);
                Check(second.Finish().SequenceEqual(expectedB), "overlapping digests retain independent hash state");
            }
            using (AssetHash.BeginBatch())
                Check(AssetHash.Compute(b).SequenceEqual(expectedB), "nested hash batches preserve outer digest state");
            first.Append(a, 5, a.Length - 5);
            Check(first.Finish().SequenceEqual(expectedA), "outer digest remains correct after nested batch");
        }
        Check(!native || metrics.Contexts == 1 && metrics.Releases == 1 && metrics.Hashes == 3,
            "nested and overlapping native digests acquire and release one provider");
        AssetHash retained;
        using (AssetHash.BeginBatch())
        {
            retained = AssetHash.Create();
            retained.Append(a, 0, a.Length);
        }
        Check(!native || metrics.Releases == 1, "a digest keeps its provider alive after batch disposal");
        Check(retained.Finish().SequenceEqual(expectedA), "digest can finish after its owner batch exits");
        retained.Dispose(); retained.Dispose();
        Check(!native || metrics.Contexts == 2 && metrics.Releases == 2, "provider is released once after last digest exits");
        try
        {
            using (AssetHash.BeginBatch())
            using (var digest = AssetHash.Create())
            {
                digest.Append(b, 0, b.Length);
                throw new OperationCanceledException();
            }
        }
        catch (OperationCanceledException) { }
        using (AssetHash.BeginBatch())
            Check(AssetHash.Compute(a).SequenceEqual(expectedA), "exception unwinding restores usable batch state");
        Check(!native || metrics.Contexts == 4 && metrics.Releases == 4, "cancelled hash batches release provider references");

        Exception[] errors = new Exception[4];
        var threads = new Thread[errors.Length];
        for (int i = 0; i < threads.Length; i++)
        {
            int index = i;
            threads[i] = new Thread(delegate()
            {
                try
                {
                    var local = AssetHash.Timings = new AssetHash.NativeTimings();
                    using (AssetHash.BeginBatch())
                        for (int j = 0; j < 10; j++)
                            if (!AssetHash.Compute(index % 2 == 0 ? a : b).SequenceEqual(index % 2 == 0 ? expectedA : expectedB))
                                throw new Exception("cross-thread hash state was mixed");
                    if (native && (local.Contexts != 1 || local.Releases != 1)) throw new Exception("thread provider was not scoped");
                }
                catch (Exception error) { errors[index] = error; }
                finally { AssetHash.Timings = null; }
            });
            threads[i].Start();
        }
        foreach (var thread in threads) thread.Join();
        Check(errors.All(x => x == null), "parallel imports use independent scoped providers and produce correct hashes");
        string oldRoot = AssetDiskCache.Root;
        AssetDiskCache.Root = Path.GetFullPath("artifacts/hash-path-check");
        string source = Path.GetFullPath("artifacts/中文 路径.obj");
        byte[] key;
        using (var sha = new SHA256Managed()) key = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(source.ToUpperInvariant()));
        string expectedPath = Path.Combine(AssetDiskCache.Root, BitConverter.ToString(key).Replace("-", "").ToLowerInvariant() + ".mesh");
        int contextsBefore = metrics.Contexts;
        Check(AssetDiskCache.CachePath(source, 1) == expectedPath && metrics.Contexts == contextsBefore,
            "cache filenames stay compatible without acquiring native providers");
        AssetDiskCache.Root = oldRoot;
        AssetHash.Timings = null;
    }

    static void WaitWorker(ImportWorker worker)
    {
        var timer = Stopwatch.StartNew();
        while (!worker.Completed && timer.ElapsedMilliseconds < 5000) Thread.Sleep(1);
        if (!worker.Completed) { worker.Cancelled = true; throw new Exception("worker timed out"); }
    }

    static void CheckWorkerBatches(string workspace)
    {
        string folder = Path.Combine(workspace, "worker");
        Directory.CreateDirectory(folder);
        const string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";
        foreach (string name in new[] { "Head", "Eyes" })
        {
            string path = Path.Combine(folder, name + ".obj");
            File.WriteAllText(path, obj);
            bool hit; AssetDiskCache.ReadMesh(path, null, out hit);
        }
        string png = Path.Combine(folder, "t_Head.png");
        byte[] source = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aL1sAAAAASUVORK5CYII=");
        File.WriteAllBytes(png, source);
        AssetDiskCache.WriteTexture(png, AssetHash.Compute(source), new CachedTexture
        { Width = 1, Height = 1, Format = (int)TextureFormat.RGBA32, MipCount = 1, Pixels = new byte[] { 0, 0, 0, 255 } });
        File.WriteAllText(Path.Combine(folder, "m_Head.txt"), "_Glossiness=0.25\n");
        var worker = new ImportWorker(folder, false, delegate { return true; });
        worker.Start(); WaitWorker(worker);
        Check(worker.Error == null && worker.MeshCacheHits == 2 && worker.TextureCacheHits == 1 &&
            worker.Data.Materials["m_Head"]["_Glossiness"] == 0.25f, "actual worker restores meshes, pixels and material values");
        Check(worker.HashTimings.Contexts == 0 || worker.HashTimings.Contexts == 1 && worker.HashTimings.Releases == 1,
            "actual import shares and releases one native provider");
        Check(worker.Data.Textures["t_Head"].Cached.Pixels == null && NativePixelBuffer.AllocatedBytes == 4,
            "actual worker uses native pixel storage instead of a large managed array");
        worker.Cancel();
        Check(worker.Data == null && NativePixelBuffer.AllocatedBytes == 0, "completed import releases unconsumed native pixels");
        ImportWorker cancelled = null;
        cancelled = new ImportWorker(folder, false, delegate { cancelled.Cancelled = true; return true; });
        cancelled.Start(); WaitWorker(cancelled);
        Check(cancelled.Error is OperationCanceledException && cancelled.Data == null &&
            NativePixelBuffer.AllocatedBytes == 0 &&
            (cancelled.HashTimings.Contexts == 0 || cancelled.HashTimings.Contexts == 1 && cancelled.HashTimings.Releases == 1),
            "cancelling an actual import discards data and releases its provider");
        var failed = new ImportWorker(folder, false, delegate { throw new FormatException("broken resource"); });
        failed.Start(); WaitWorker(failed);
        Check(failed.Error is FormatException && failed.Data == null && NativePixelBuffer.AllocatedBytes == 0,
            "a later mesh failure frees already-read native textures");
        File.Delete(AssetDiskCache.CachePath(png, 2));
        var cold = new ImportWorker(folder, false, delegate { return true; });
        cold.Start(); WaitWorker(cold);
        Check(cold.Error == null && cold.TextureCacheHits == 0 && cold.Data.Textures["t_Head"].PNG.SequenceEqual(source) &&
            cold.Data.Textures["t_Head"].Fingerprint.SequenceEqual(AssetHash.Compute(source)),
            "cache miss retains the exact PNG bytes and matching source fingerprint");
        cold.Cancel();
        AssetDiskCache.Enabled = false;
        var disabled = new ImportWorker(folder, false, delegate { return true; });
        disabled.Start(); WaitWorker(disabled);
        Check(disabled.Error == null && disabled.HashTimings.Contexts == 0 && disabled.MeshCacheHits == 0 &&
            disabled.Data.Textures["t_Head"].PNG.SequenceEqual(source), "disabled cache uses PNG/OBJ source without native providers");
        disabled.Cancel();
        AssetDiskCache.Enabled = true;
    }

    static void CheckNativePixels(string workspace)
    {
        string source = Path.Combine(workspace, "native-pixels.png");
        byte[] fingerprint = AssetHash.Compute(new byte[] { 1, 2, 3, 4 });
        byte[] pixels = Enumerable.Range(0, 87381 * 4).Select(i => (byte)(i * 13)).ToArray();
        AssetDiskCache.WriteTexture(source, fingerprint, new CachedTexture { Width = 256, Height = 256,
            Format = (int)TextureFormat.RGBA32, MipCount = 9, Pixels = pixels });
        var cached = AssetDiskCache.ReadTexture(source, fingerprint, true);
        byte[] copied = new byte[pixels.Length];
        System.Runtime.InteropServices.Marshal.Copy(cached.NativePixels.Pointer, copied, 0, copied.Length);
        Check(cached.Pixels == null && cached.PixelLength == pixels.Length && copied.SequenceEqual(pixels),
            "native cache reads preserve every pixel byte across chunk boundaries");
        byte[] prefix = new byte[] { 7, 8, 9 };
        byte[] expected;
        using (var sha = new SHA256Managed()) expected = sha.ComputeHash(prefix.Concat(pixels).ToArray());
        foreach (bool native in new[] { true, false })
            using (var hash = AssetHash.Create(native))
            {
                hash.Append(prefix, 0, prefix.Length);
                hash.Append(cached.NativePixels.Pointer, cached.NativePixels.Length);
                Check(hash.Finish().SequenceEqual(expected), "pointer hashing matches reference with native preference=" + native);
            }
        string rewritten = source + ".copy";
        AssetDiskCache.WriteTexture(rewritten, fingerprint, cached);
        Check(AssetDiskCache.ReadTexture(rewritten, fingerprint).Pixels.SequenceEqual(pixels),
            "native pixel writer keeps the existing cache schema and checksum");
        cached.Dispose(); cached.Dispose();
        Check(NativePixelBuffer.AllocatedBytes == 0, "native texture buffers release exactly once");
        bool disposed = false;
        try { var pointer = cached.NativePixels.Pointer; }
        catch (ObjectDisposedException) { disposed = true; }
        Check(disposed, "released pixels cannot be passed back to Unity");
        string file = AssetDiskCache.CachePath(source, 2);
        byte[] damaged = File.ReadAllBytes(file);
        damaged[damaged.Length - 1] ^= 1;
        File.WriteAllBytes(file, damaged);
        Check(AssetDiskCache.ReadTexture(source, fingerprint, true) == null && NativePixelBuffer.AllocatedBytes == 0,
            "native checksum rejection frees its pixel allocation");
        bool cancelled = false;
        int reads = 0;
        try { AssetDiskCache.ReadTexture(rewritten, fingerprint, true, delegate { return ++reads == 2; }); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && NativePixelBuffer.AllocatedBytes == 0, "cancelled native read releases partial pixels");
        bool truncated = false;
        try { NativePixelBuffer.Read(new MemoryStream(new byte[3]), 4, null); }
        catch (EndOfStreamException) { truncated = true; }
        Check(truncated && NativePixelBuffer.AllocatedBytes == 0, "truncated native stream releases partial pixels");
    }

    static void CheckCompressedTextures(string workspace)
    {
        string source = Path.Combine(workspace, "lz4-texture.png");
        byte[] fingerprint = AssetHash.Compute(new byte[] { 50, 60, 70 });
        byte[] pixels = Enumerable.Range(0, 87381 * 4).Select(i => (byte)(i % 23)).ToArray();
        var texture = new CachedTexture { Width = 256, Height = 256, Format = (int)TextureFormat.ARGB32, MipCount = 9, Pixels = pixels };
        AssetDiskCache.WriteTexture(source, fingerprint, texture);
        string path = AssetDiskCache.CachePath(source, 2);
        byte[] legacy = File.ReadAllBytes(path);
        Check(BitConverter.ToInt32(legacy, 4) == 1 && AssetDiskCache.ReadTexture(source, fingerprint).Pixels.SequenceEqual(pixels),
            "legacy raw textures remain readable before migration");
        Check(AssetDiskCache.CompressTextureCache(path), "legacy texture migrates to LZ4HC");
        byte[] compressed = File.ReadAllBytes(path);
        Check(BitConverter.ToInt32(compressed, 4) == 2 && BitConverter.ToInt32(compressed, 96) == 1 && compressed.Length < legacy.Length / 5,
            "compressed texture schema saves space and records LZ4 codec");
        Check(compressed.Skip(44).Take(32).SequenceEqual(legacy.Skip(44).Take(32)), "migration preserves decoded-pixel checksum");
        Check(AssetDiskCache.ReadTexture(source, fingerprint).Pixels.SequenceEqual(pixels), "managed compressed read is byte-identical");
        using (var native = AssetDiskCache.ReadTexture(source, fingerprint, true))
        {
            var copied = new byte[pixels.Length];
            System.Runtime.InteropServices.Marshal.Copy(native.NativePixels.Pointer, copied, 0, copied.Length);
            Check(native.Compressed && native.Pixels == null && copied.SequenceEqual(pixels), "native compressed read preserves pixels and avoids large managed storage");
        }
        Check(NativePixelBuffer.AllocatedBytes == 0 && !AssetDiskCache.CompressTextureCache(path), "processed cache is not recompressed or retained");
        byte[] damaged = (byte[])compressed.Clone(); damaged[damaged.Length - 1] ^= 1;
        File.WriteAllBytes(path, damaged);
        Check(AssetDiskCache.ReadTexture(source, fingerprint, true) == null && NativePixelBuffer.AllocatedBytes == 0,
            "corrupt LZ4 stream or decoded checksum is rejected without leaking memory");
        damaged = (byte[])compressed.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(99), 0, damaged, 96, 4); File.WriteAllBytes(path, damaged);
        Check(AssetDiskCache.ReadTexture(source, fingerprint, true) == null, "unknown compression codec falls back");
        File.WriteAllBytes(path, compressed.Take(compressed.Length - 1).ToArray());
        Check(AssetDiskCache.ReadTexture(source, fingerprint, true) == null, "truncated compressed texture falls back");
        File.WriteAllBytes(path, compressed);
        bool cancelled = false;
        try { AssetDiskCache.ReadTexture(source, fingerprint, true, delegate { return true; }); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && NativePixelBuffer.AllocatedBytes == 0, "cancelled compressed read releases native buffers");
        File.WriteAllBytes(path, legacy);
        Check(!AssetDiskCache.CompressTextureCache(path, delegate { return true; }) && File.ReadAllBytes(path).SequenceEqual(legacy),
            "cancelled migration keeps the original file");
        byte[] newerFingerprint = AssetHash.Compute(new byte[] { 80, 90, 100 });
        byte[] newerPixels = pixels.Select(x => (byte)(x + 1)).ToArray();
        bool replaced = false;
        Check(!AssetDiskCache.CompressTextureCache(path, delegate
        {
            if (!replaced && NativePixelBuffer.AllocatedBytes > pixels.Length)
            {
                replaced = true;
                AssetDiskCache.WriteTexture(source, newerFingerprint, new CachedTexture { Width = 256, Height = 256,
                    Format = (int)TextureFormat.ARGB32, MipCount = 9, Pixels = newerPixels });
            }
            return false;
        }) && replaced && AssetDiskCache.ReadTexture(source, newerFingerprint).Pixels.SequenceEqual(newerPixels),
            "migration cannot overwrite a newer source cache published during compression");
        Check(NativePixelBuffer.AllocatedBytes == 0 && Directory.GetFiles(AssetDiskCache.Root, "*.tmp").Length == 0,
            "migration releases workspaces and temporary files on all exits");
        string tiny = source + ".tiny";
        AssetDiskCache.WriteTexture(tiny, fingerprint, new CachedTexture { Width = 1, Height = 1,
            Format = (int)TextureFormat.RGBA32, MipCount = 1, Pixels = new byte[] { 1, 2, 3, 4 } });
        string tinyPath = AssetDiskCache.CachePath(tiny, 2);
        Check(AssetDiskCache.CompressTextureCache(tinyPath) && BitConverter.ToInt32(File.ReadAllBytes(tinyPath), 96) == 0 &&
            AssetDiskCache.ReadTexture(tiny, fingerprint).Pixels.SequenceEqual(new byte[] { 1, 2, 3, 4 }),
            "incompressible pixels are stored raw with a processed schema");
        AssetDiskCache.BackgroundCompressionEnabled = true;
        AssetDiskCache.QueueTextureWrite(source, fingerprint, texture);
        var wait = Stopwatch.StartNew();
        while ((AssetDiskCache.WriterBusy || AssetDiskCache.CompressionBusy) && wait.ElapsedMilliseconds < 5000) Thread.Sleep(1);
        Check(!AssetDiskCache.WriterBusy && !AssetDiskCache.CompressionBusy && AssetDiskCache.ReadTexture(source, fingerprint).Compressed &&
            NativePixelBuffer.AllocatedBytes == 0, "background PNG write is followed by bounded native compression");
        AssetDiskCache.BackgroundCompressionEnabled = false;
        AssetDiskCache.StopTextureCompression();
        string previousRoot = AssetDiskCache.Root;
        AssetDiskCache.Root = Path.Combine(workspace, "startup-compression");
        AssetDiskCache.WriteTexture(source, fingerprint, texture);
        string startupPath = AssetDiskCache.CachePath(source, 2);
        AssetDiskCache.BackgroundCompressionEnabled = true;
        AssetDiskCache.StartTextureCompression();
        wait = Stopwatch.StartNew();
        while (AssetDiskCache.CompressionBusy && wait.ElapsedMilliseconds < 5000) Thread.Sleep(1);
        Check(!AssetDiskCache.CompressionBusy && AssetDiskCache.ReadTexture(source, fingerprint).Compressed,
            "startup scan upgrades existing raw texture files");
        byte[] processed = File.ReadAllBytes(startupPath);
        AssetDiskCache.StartTextureCompression();
        wait = Stopwatch.StartNew();
        while (AssetDiskCache.CompressionBusy && wait.ElapsedMilliseconds < 5000) Thread.Sleep(1);
        Check(!AssetDiskCache.CompressionBusy && File.ReadAllBytes(startupPath).SequenceEqual(processed),
            "startup scan leaves already-compressed files unchanged");
        AssetDiskCache.BackgroundCompressionEnabled = false;
        AssetDiskCache.WriteTexture(source, fingerprint, texture);
        AssetDiskCache.BackgroundCompressionEnabled = true;
        AssetDiskCache.StartTextureCompression();
        wait = Stopwatch.StartNew();
        while (AssetDiskCache.CompressionBusy && NativePixelBuffer.AllocatedBytes == 0 && wait.ElapsedMilliseconds < 5000) Thread.Sleep(1);
        AssetDiskCache.StopTextureCompression();
        wait = Stopwatch.StartNew();
        while (AssetDiskCache.CompressionBusy && wait.ElapsedMilliseconds < 5000) Thread.Sleep(1);
        Check(!AssetDiskCache.CompressionBusy && NativePixelBuffer.AllocatedBytes == 0 && AssetDiskCache.ValidateTextureCache(startupPath),
            "shutdown cancels or completes the active upgrade without losing valid data or native memory");
        AssetDiskCache.BackgroundCompressionEnabled = false;
        AssetDiskCache.Root = previousRoot;
    }

    static void CheckMeshDecoder()
    {
        const int count = 70001;
        var data = new ObjImporter.MeshData { Vertices = new Vector3[count], Normals = new Vector3[count],
            UV = new Vector2[count], Triangles = new[] { 0, 65536, count - 1 } };
        for (int i = 0; i < count; i++)
        {
            data.Vertices[i] = new Vector3(-i * 0.125f, i * 0.25f, -0.0f);
            data.Normals[i] = new Vector3(1.0f / (i + 1), float.MaxValue, float.Epsilon);
            data.UV[i] = new Vector2(i % 7, -i % 11);
        }
        byte[] bytes = EncodeMesh(data);
        Check(EncodeMesh(MeshCacheCodec.Decode(bytes, count, 3, null)).SequenceEqual(bytes),
            "direct mesh decoding preserves every float/index byte, including 32-bit vertex indices");
        bool cancelled = false;
        int checks = 0;
        try { MeshCacheCodec.Decode(bytes, count, 3, delegate { return ++checks == 2; }); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && checks == 2, "direct decoder cancels during vertex restoration");
        byte[] invalidIndex = (byte[])bytes.Clone();
        Buffer.BlockCopy(BitConverter.GetBytes(count), 0, invalidIndex, count * 32, 4);
        bool rejected = false;
        try { MeshCacheCodec.Decode(invalidIndex, count, 3, null); }
        catch (FormatException) { rejected = true; }
        Check(rejected, "direct decoder validates indices before returning geometry");
        rejected = false;
        try { MeshCacheCodec.Decode(new byte[31], 1, 0, null); }
        catch (FormatException) { rejected = true; }
        Check(rejected, "direct decoder rejects truncated buffers before unsafe access");
        Check(MeshCacheCodec.Decode(new byte[0], 0, 0, null).Vertices.Length == 0,
            "direct decoder handles intentionally empty parts");
    }

    static void Warm(string resources, string output)
    {
        AssetDiskCache.Root = output;
        int hits = 0, total = 0;
        var timer = Stopwatch.StartNew();
        double warmReadMs = 0;
        foreach (string path in Directory.GetFiles(resources, "*.obj", SearchOption.AllDirectories))
        {
            bool hit;
            var original = AssetDiskCache.ReadMesh(path, null, out hit);
            if (hit) hits++;
            var readTimer = Stopwatch.StartNew();
            var restored = AssetDiskCache.ReadMesh(path, null, out hit);
            warmReadMs += readTimer.Elapsed.TotalMilliseconds;
            Check(hit && original.Vertices.SequenceEqual(restored.Vertices) &&
                original.Normals.SequenceEqual(restored.Normals) && original.UV.SequenceEqual(restored.UV) &&
                original.Triangles.SequenceEqual(restored.Triangles), "prewarmed geometry roundtrip: " + path);
            total++;
        }
        Console.WriteLine("Warmed " + total + " OBJ caches; hits=" + hits +
            "; verified=" + total + "; diskReadMs=" + warmReadMs.ToString("F2") +
            "; totalMs=" + timer.Elapsed.TotalMilliseconds.ToString("F2") + "; path=" + output);
    }
}
