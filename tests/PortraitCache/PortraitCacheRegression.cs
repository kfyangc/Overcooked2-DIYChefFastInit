using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using OC2DIYChef;
using UnityEngine;

internal static class PortraitCacheRegression
{
    static int assertions, downsampleCalls;
    static string workspace;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        assertions++;
    }
    static string Image(string name) { return Path.Combine(workspace, name + ".png"); }
    static string Binary(string image) { return Path.ChangeExtension(image, ".texture"); }
    static byte[] Pixels(int size, TextureFormat format, byte marker)
    {
        var pixels = new byte[size * size * Texture2D.Channels(format)];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)((i % 61) * 3 + marker);
        return pixels;
    }
    static void Fixture(string path, int size, TextureFormat format, byte marker)
    {
        using (var output = new BinaryWriter(File.Create(path)))
        {
            byte[] pixels = Pixels(size, format, marker);
            output.Write(Texture2D.FixtureMagic); output.Write(size); output.Write(size); output.Write((int)format);
            output.Write(pixels.Length); output.Write(pixels);
        }
    }
    static Texture2D Downsample(Texture2D source)
    {
        downsampleCalls++;
        var texture = new Texture2D(128, 128, source.format, false);
        byte[] from = source.GetRawTextureData();
        int channels = Texture2D.Channels(source.format);
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
                Buffer.BlockCopy(from, ((y * source.height / 128) * source.width + x * source.width / 128) * channels,
                    texture.Pixels, (y * 128 + x) * channels, channels);
        return texture;
    }
    static Texture2D Load(string image, string supplied = null, int size = 128)
    { return ChefGridPortraitCache.Load(image, supplied, size, Downsample); }
    static void Drain()
    {
        var wait = Stopwatch.StartNew();
        while (ChefGridPortraitCache.WriterBusy && wait.ElapsedMilliseconds < 10000) Thread.Sleep(1);
        Check(!ChefGridPortraitCache.WriterBusy && ChefGridPortraitCache.PendingWriteBytes == 0, "portrait queue drains without retained pending bytes");
    }
    internal static void Main()
    {
        workspace = Path.GetFullPath(Path.Combine("artifacts/portrait-cache-regression", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(workspace);
        AssetDiskCache.Root = workspace;
        AssetDiskCache.BackgroundCompressionEnabled = false;
        AssetDiskCache.Log = Console.WriteLine;
        if (IntPtr.Size == 4)
            Check(Lz4Codec.Initialise(Path.Combine(workspace, "native"), Console.WriteLine), "embedded native x86 LZ4 loads without Unity");
        try
        {
            MigrationAndFormats();
            SuppliedAndSizes();
            RecoveryAndInvalidData();
            GeneratedAndBatch();
            DisabledAndShutdown();
            Check(NativePixelBuffer.AllocatedBytes == 0, "all raw and compressed reads release native pixel buffers");
            Console.WriteLine("PASS: " + assertions + " production portrait migration/cache assertions; image/GPU APIs use explicit stubs, no Unity or game process created.");
        }
        finally { ChefGridPortraitCache.Shutdown(); AssetDiskCache.StopTextureCompression(); }
    }

    static void MigrationAndFormats()
    {
        foreach (TextureFormat format in new[] { TextureFormat.RGBA32, TextureFormat.ARGB32, TextureFormat.BGRA32, TextureFormat.RGB24 })
        {
            string image = Image("legacy-" + format);
            Fixture(image, 128, format, 17);
            byte[] expected = Pixels(128, format, 17);
            int png = Texture2D.PngLoads, raw = Texture2D.RawLoads;
            var first = Load(image);
            Check(first != null && Texture2D.PngLoads == png + 1 && Texture2D.RawLoads == raw,
                "legacy image uses the decode path before migration: " + format);
            Check(first.format == format && !first.Readable && first.UploadedPixels.SequenceEqual(expected),
                "decoded format, channel order, orientation and every alpha byte survive first display: " + format);
            Drain();
            using (var stored = AssetDiskCache.ReadTextureFile(Binary(image)))
                Check(stored != null && stored.Width == 128 && stored.Height == 128 && stored.MipCount == 1 &&
                    stored.Format == (int)format && stored.Pixels.SequenceEqual(expected), "migration stores actual decoded format and pixel bytes");
            Check(File.Exists(image), "migration preserves the existing image as a fallback");
            png = Texture2D.PngLoads; raw = Texture2D.RawLoads;
            var second = Load(image);
            Check(second != null && Texture2D.PngLoads == png && Texture2D.RawLoads == raw + 1,
                "warm binary hit does not call image decoding: " + format);
            Check(second.format == format && !second.Readable && second.UploadedPixels.SequenceEqual(expected),
                "binary display exactly matches first decode: " + format);
            if (Lz4Codec.Available)
            {
                Check(AssetDiskCache.CompressTextureCache(Binary(image)), "migrated texture upgrades to the shared LZ4 container");
                var compressed = Load(image);
                Check(compressed != null && compressed.UploadedPixels.SequenceEqual(expected) && Texture2D.PngLoads == png,
                    "compressed portrait preserves bytes and bypasses image decoding");
                UnityEngine.Object.Destroy(compressed);
            }
            UnityEngine.Object.Destroy(first); UnityEngine.Object.Destroy(second);
        }
    }

    static void SuppliedAndSizes()
    {
        string legacy = Image("author-choice"), supplied = Image("preview");
        Fixture(legacy, 128, TextureFormat.RGBA32, 1);
        Fixture(supplied, 128, TextureFormat.ARGB32, 2);
        var first = Load(legacy, supplied);
        Check(first != null && first.format == TextureFormat.ARGB32 && first.UploadedPixels.SequenceEqual(Pixels(128, TextureFormat.ARGB32, 2)),
            "author preview takes precedence over a rendered legacy PNG");
        Drain();
        int png = Texture2D.PngLoads;
        Fixture(supplied, 128, TextureFormat.RGBA32, 3);
        var hot = Load(legacy, supplied);
        Check(hot != null && hot.UploadedPixels.SequenceEqual(first.UploadedPixels) && Texture2D.PngLoads == png,
            "a binary at the same versioned key takes precedence and needs no PNG decode");
        string changedKey = Image("resource-version-changed");
        var changed = Load(changedKey, supplied);
        Check(changed != null && changed.UploadedPixels.SequenceEqual(Pixels(128, TextureFormat.RGBA32, 3)),
            "a different resource-version filename misses the old binary and loads the new author preview");
        Drain();

        string detail = Image("independent.512"), small = Image("small-preview");
        Fixture(detail, 512, TextureFormat.RGBA32, 7);
        Fixture(small, 128, TextureFormat.RGBA32, 8);
        var large = Load(detail, small, 512);
        Check(large != null && large.width == 512 && large.UploadedPixels.SequenceEqual(Pixels(512, TextureFormat.RGBA32, 7)),
            "a 128 author preview cannot replace a real 512 detail image");
        Drain();
        string thumbnail = Image("independent");
        var thumb = Load(thumbnail, detail);
        Check(thumb != null && thumb.width == 128 && thumb.height == 128 && downsampleCalls == 1,
            "a larger supplied preview is downsampled once for the thumbnail");
        Drain();
        Check(Binary(detail) != Binary(thumbnail) && File.Exists(Binary(detail)) && File.Exists(Binary(thumbnail)),
            "512 detail and 128 thumbnail retain independent binary files");
        if (Lz4Codec.Available)
            Check(AssetDiskCache.CompressTextureCache(Binary(detail)), "512 detail uses the same verified LZ4 container");
        png = Texture2D.PngLoads;
        var largeHot = Load(detail, null, 512); var thumbHot = Load(thumbnail);
        Check(largeHot != null && largeHot.width == 512 && largeHot.UploadedPixels.SequenceEqual(large.UploadedPixels) &&
            thumbHot != null && thumbHot.width == 128 && thumbHot.UploadedPixels.SequenceEqual(thumb.UploadedPixels) && Texture2D.PngLoads == png,
            "both image sizes load from their correct raw buffers");
        foreach (var texture in new[] { first, hot, changed, large, thumb, largeHot, thumbHot }) UnityEngine.Object.Destroy(texture);
    }

    static void RecoveryAndInvalidData()
    {
        string image = Image("recovery");
        Fixture(image, 128, TextureFormat.RGBA32, 11);
        UnityEngine.Object.Destroy(Load(image)); Drain();
        byte[] valid = File.ReadAllBytes(Binary(image));
        foreach (bool truncate in new[] { false, true })
        {
            byte[] bad = (byte[])valid.Clone();
            if (truncate) Array.Resize(ref bad, 12); else bad[bad.Length - 1] ^= 127;
            File.WriteAllBytes(Binary(image), bad);
            int png = Texture2D.PngLoads;
            var fallback = Load(image);
            Check(fallback != null && Texture2D.PngLoads == png + 1 && fallback.UploadedPixels.SequenceEqual(Pixels(128, TextureFormat.RGBA32, 11)),
                "bad binary falls back to the existing PNG and preserves pixels");
            Drain();
            Check(AssetDiskCache.ValidateTextureCache(Binary(image)), "fallback repairs the cache atomically");
            UnityEngine.Object.Destroy(fallback);
        }
        var invalid = new CachedTexture { Width = 1024, Height = 1024, Format = (int)TextureFormat.RGBA32, MipCount = 1, Pixels = new byte[4] };
        AssetDiskCache.WriteTextureFile(Binary(image), new byte[32], invalid);
        var sized = Load(image);
        Check(sized != null && sized.width == 128 && NativePixelBuffer.AllocatedBytes == 0,
            "oversized metadata is rejected before pixel allocation and falls back to PNG");
        Drain();
        string largePath = Binary(Image("valid-large-metadata"));
        AssetDiskCache.WriteTextureFile(largePath, new byte[32], new CachedTexture { Width = 512, Height = 512,
            Format = (int)TextureFormat.RGBA32, MipCount = 1, Pixels = Pixels(512, TextureFormat.RGBA32, 12) });
        int readChecks = 0;
        using (var rejected = AssetDiskCache.ReadTextureFile(largePath, null, true, delegate { readChecks++; return false; }, 128))
            Check(rejected == null && readChecks == 0 && NativePixelBuffer.AllocatedBytes == 0,
                "dimension limit rejects a valid 512 payload before native pixel allocation/read begins");
        var mips = new Texture2D(128, 128, TextureFormat.RGBA32, true);
        AssetDiskCache.WriteTextureFile(Binary(image), new byte[32], new CachedTexture { Width = 128, Height = 128,
            Format = (int)mips.format, MipCount = mips.mipmapCount, Pixels = mips.GetRawTextureData() });
        int before = Texture2D.PngLoads;
        var nonMip = Load(image);
        Check(nonMip != null && nonMip.mipmapCount == 1 && Texture2D.PngLoads == before + 1,
            "portrait rejects resource-style mip chains and repairs them from PNG");
        Drain();
        Texture2D.FailNextRawLoad = true;
        int created = Texture2D.Created.Count;
        var retry = Load(image);
        Check(retry != null && Texture2D.Created[created].Destroyed && NativePixelBuffer.AllocatedBytes == 0,
            "Unity upload failure destroys the partial texture and still falls back without native memory leaks");
        Drain();
        Check(Load(Image("missing")) == null, "missing binary and PNG return a render request rather than a broken texture");
        string broken = Image("broken-image"); File.WriteAllBytes(broken, new byte[] { 1, 2, 3 });
        Check(Load(Image("bad-source"), broken) == null, "invalid decoded-image input returns a render request");
        foreach (var texture in new[] { sized, mips, nonMip, retry }) UnityEngine.Object.Destroy(texture);
    }

    static void GeneratedAndBatch()
    {
        string image = Image("new-render");
        var render = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        render.Pixels = Pixels(128, TextureFormat.RGBA32, 21);
        byte[] expected = (byte[])render.Pixels.Clone();
        ChefGridPortraitCache.Save(image, render);
        render.Pixels[0] ^= 127; // Saving owns a copied snapshot, not a later mutable Unity buffer.
        UnityEngine.Object.Destroy(render); Drain();
        Check(!File.Exists(image) && File.Exists(Binary(image)), "fresh captures save binary directly without encoding or creating a PNG");
        var loaded = Load(image);
        Check(loaded != null && loaded.UploadedPixels.SequenceEqual(expected), "the saved snapshot survives source mutation/destruction");
        UnityEngine.Object.Destroy(loaded);
        for (int i = 0; i < 50; i++)
        {
            string path = Image("batch-" + i);
            Fixture(path, 128, TextureFormat.RGBA32, (byte)i);
            UnityEngine.Object.Destroy(Load(path));
        }
        Drain();
        int png = Texture2D.PngLoads;
        for (int i = 0; i < 50; i++)
        {
            var hot = Load(Image("batch-" + i));
            Check(hot != null && hot.UploadedPixels.SequenceEqual(Pixels(128, TextureFormat.RGBA32, (byte)i)),
                "all 50 migrated thumbnails remain available with matching pixels");
            UnityEngine.Object.Destroy(hot);
        }
        Check(Texture2D.PngLoads == png, "a full grid migration retains every request and the next visit performs zero image decodes");
    }

    static void DisabledAndShutdown()
    {
        string image = Image("disabled"); Fixture(image, 128, TextureFormat.RGBA32, 44);
        AssetDiskCache.Enabled = false;
        var fallback = Load(image);
        Check(fallback != null && !File.Exists(Binary(image)), "disabled cache still displays a PNG without writing binary");
        AssetDiskCache.Enabled = true;
        UnityEngine.Object.Destroy(fallback);
        ChefGridPortraitCache.Shutdown(); Drain();
        string after = Image("after-stop");
        var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        ChefGridPortraitCache.Save(after, texture);
        Drain();
        Check(!File.Exists(Binary(after)) && NativePixelBuffer.AllocatedBytes == 0,
            "shutdown rejects further snapshots without retaining memory or touching disk");
        UnityEngine.Object.Destroy(texture);
    }
}

namespace OC2DIYChef.Research
{
    public static class MonoCacheProbe
    {
        public static void Run() { global::PortraitCacheRegression.Main(); }
    }
}
