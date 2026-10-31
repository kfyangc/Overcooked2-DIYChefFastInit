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
    static void Main(string[] args)
    {
        AssetDiskCache.Log = Console.WriteLine;
        AssetHash.Warning = Console.WriteLine;
        if (args.Length == 3 && args[0] == "--warm")
        {
            Warm(args[1], args[2]);
            return;
        }
        CheckHashing();
        string workspace = Path.GetFullPath(Path.Combine("artifacts", "cache-regression", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(workspace);
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
