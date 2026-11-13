// Offline format benchmark. The host embeds only the game's Mono DLL, without launching the game.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OC2DIYChef.Research
{
    // The shared MonoCacheHost invokes this exact type and method.
    public static class MonoCacheProbe
    {
        sealed class Sample
        {
            internal string PngPath, RawPath, Lz4Path;
            internal byte[] Png, Pixels, Fingerprint;
            internal int Width, Height;
        }
        static string Output { get { return Environment.GetEnvironmentVariable("DIYCHEF_PORTRAIT_REPORT_DIR"); } }
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
        static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", ""); }

        static void VerifyTexture(Sample sample, string path, int schema)
        {
            // Validate both native-buffer and managed-buffer production decoding paths.
            foreach (bool native in new[] { false, true })
            using (var texture = AssetDiskCache.ReadTextureFile(path, sample.Fingerprint, native, null, 512))
            {
                if (texture == null || texture.Width != sample.Width || texture.Height != sample.Height ||
                    texture.Format != (int)TextureFormat.BGRA32 || texture.MipCount != 1 || texture.StorageSchema != schema)
                    throw new InvalidOperationException("Production cache metadata differs: " + path);
                byte[] bytes = texture.Pixels;
                if (native)
                {
                    bytes = new byte[texture.PixelLength];
                    Marshal.Copy(texture.NativePixels.Pointer, bytes, 0, bytes.Length);
                }
                if (!Equal(bytes, sample.Pixels)) throw new InvalidOperationException("Production pixels differ: " + path);
            }
        }

        static Sample Prepare(string original, GdiPngDecoder decoder, StreamWriter manifest)
        {
            var sample = new Sample { Png = File.ReadAllBytes(original) };
            sample.Pixels = decoder.Decode(sample.Png, out sample.Width, out sample.Height);
            if (sample.Width != sample.Height || sample.Width != 128 && sample.Width != 512)
                throw new InvalidOperationException("Expected 128/512 square portrait: " + original);
            sample.Fingerprint = AssetDiskCache.Fingerprint(sample.Png);
            string filename = Path.GetFileName(original);
            // All timed files reside in sibling artifact directories on the same disk.
            sample.PngPath = Path.Combine(Path.Combine(Output, "png"), filename);
            sample.RawPath = Path.Combine(Path.Combine(Output, "schema1"), Path.ChangeExtension(filename, ".texture"));
            sample.Lz4Path = Path.Combine(Path.Combine(Output, "schema2"), Path.ChangeExtension(filename, ".texture"));
            File.WriteAllBytes(sample.PngPath, sample.Png);
            using (var texture = new CachedTexture { Width = sample.Width, Height = sample.Height,
                Format = (int)TextureFormat.BGRA32, MipCount = 1, Pixels = sample.Pixels })
                AssetDiskCache.WriteTextureFile(sample.RawPath, sample.Fingerprint, texture);
            VerifyTexture(sample, sample.RawPath, 1);
            File.Copy(sample.RawPath, sample.Lz4Path, true);
            if (!AssetDiskCache.CompressTextureCache(sample.Lz4Path))
                throw new InvalidOperationException("Production compression failed: " + sample.Lz4Path);
            VerifyTexture(sample, sample.Lz4Path, 2);
            manifest.WriteLine(string.Join(",", new[] { filename, sample.Width.ToString(Invariant), sample.Height.ToString(Invariant),
                sample.Png.Length.ToString(Invariant), new FileInfo(sample.RawPath).Length.ToString(Invariant),
                new FileInfo(sample.Lz4Path).Length.ToString(Invariant), Hex(sample.Fingerprint), Hex(AssetHash.Compute(sample.Pixels)) }));
            return sample;
        }

        static double Measure(Action action, int repeats)
        {
            action();
            var samples = new List<double>();
            for (int i = 0; i < 9; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                var timer = Stopwatch.StartNew();
                for (int j = 0; j < repeats; j++) action();
                samples.Add(timer.Elapsed.TotalMilliseconds / repeats);
            }
            samples.Sort(); return samples[4];
        }
        static void Report(StreamWriter report, string group, string operation, double milliseconds)
        {
            string row = group + "," + operation + "," + milliseconds.ToString("F4", Invariant);
            Console.WriteLine(row); report.WriteLine(row); report.Flush();
        }
        static void ReadProduction(List<Sample> samples, bool compressed, bool native)
        {
            using (AssetHash.BeginBatch())
            foreach (var sample in samples)
            using (var texture = AssetDiskCache.ReadTextureFile(compressed ? sample.Lz4Path : sample.RawPath, null, native, null, 512))
            {
                if (texture == null || texture.PixelLength != sample.Pixels.Length)
                    throw new InvalidOperationException("Production texture read missed");
            }
        }
        static void RunGroup(StreamWriter report, string name, List<Sample> samples, GdiPngDecoder decoder)
        {
            if (samples.Count == 0) return;
            long png = 0, raw = 0, compressed = 0;
            foreach (var sample in samples)
            {
                png += sample.Png.Length; raw += new FileInfo(sample.RawPath).Length;
                compressed += new FileInfo(sample.Lz4Path).Length;
            }
            Console.WriteLine("GROUP " + name + " count=" + samples.Count + " PNG=" + png + " schema1=" + raw + " schema2=" + compressed);
            int repeats = name == "128x128_50" ? 3 : 1;
            Report(report, name, "png_file_read", Measure(delegate
            { foreach (var sample in samples) File.ReadAllBytes(sample.PngPath); }, repeats));
            Report(report, name, "png_memory_gdiplus_decode_bgra_copy", Measure(delegate
            { foreach (var sample in samples) { int w, h; decoder.Decode(sample.Png, out w, out h); } }, repeats));
            Report(report, name, "png_readallbytes_gdiplus_decode_bgra_copy", Measure(delegate
            { foreach (var sample in samples) { int w, h; decoder.Decode(File.ReadAllBytes(sample.PngPath), out w, out h); } }, repeats));
            Report(report, name, "production_schema1_native_read_verify", Measure(delegate { ReadProduction(samples, false, true); }, repeats));
            Report(report, name, "production_schema2_native_read_decode_verify", Measure(delegate { ReadProduction(samples, true, true); }, repeats));
            Report(report, name, "production_schema1_managed_read_verify", Measure(delegate { ReadProduction(samples, false, false); }, repeats));
            Report(report, name, "production_schema2_managed_read_decode_verify", Measure(delegate { ReadProduction(samples, true, false); }, repeats));
        }
        static void RunCore()
        {
            string source = Environment.GetEnvironmentVariable("DIYCHEF_PORTRAIT_SOURCE");
            if (string.IsNullOrEmpty(Output) || string.IsNullOrEmpty(source))
                throw new ArgumentException("Set DIYCHEF_PORTRAIT_SOURCE and DIYCHEF_PORTRAIT_REPORT_DIR using the run script");
            foreach (string child in new[] { "png", "schema1", "schema2" }) Directory.CreateDirectory(Path.Combine(Output, child));
            AssetDiskCache.Root = Output;
            AssetDiskCache.Enabled = true;
            AssetDiskCache.Log = Console.WriteLine;
            AssetDiskCache.Timings = null;
            AssetDiskCache.BackgroundCompressionEnabled = false;
            AssetHash.Warning = Console.WriteLine;
            AssetHash.Timings = null;
            if (!Lz4Codec.Initialise(Path.Combine(Output, "native"), Console.WriteLine))
                throw new InvalidOperationException("Pinned native x86 LZ4 unavailable");
            string[] paths = Directory.GetFiles(source, "*.png");
            Array.Sort(paths, StringComparer.Ordinal);
            if (paths.Length == 0) throw new ArgumentException("No source PNG portraits in " + source);
            var thumbnails = new List<Sample>();
            var details = new List<Sample>();
            Console.WriteLine("PointerSize=" + IntPtr.Size + " CLR=" + Environment.Version + " source=" + source + " files=" + paths.Length);
            using (var decoder = new GdiPngDecoder())
            using (var manifest = new StreamWriter(Path.Combine(Output, "samples.csv"), false))
            using (var report = new StreamWriter(Path.Combine(Output, "timings.csv"), false))
            {
                manifest.WriteLine("file,width,height,png_bytes,schema1_bytes,schema2_bytes,png_sha256,bgra_sha256");
                report.WriteLine("sample,stage,median_ms");
                using (AssetHash.BeginBatch())
                foreach (string path in paths)
                {
                    var sample = Prepare(path, decoder, manifest);
                    (sample.Width == 128 ? thumbnails : details).Add(sample);
                }
                manifest.Flush();
                Console.WriteLine("Verified production schema1/schema2 managed/native pixels for all " + paths.Length + " files");
                RunGroup(report, "128x128_50", thumbnails.GetRange(0, Math.Min(50, thumbnails.Count)), decoder);
                RunGroup(report, "128x128_all", thumbnails, decoder);
                RunGroup(report, "512x512_all", details, decoder);
                if (details.Count > 0) RunGroup(report, "512x512_one", details.GetRange(0, 1), decoder);
            }
            if (NativePixelBuffer.AllocatedBytes != 0) throw new InvalidOperationException("Native pixel buffer leak");
            Console.WriteLine("PASS; native pixel bytes retained=0; no game or Unity scene was launched");
        }
        public static void Run()
        {
            try { RunCore(); }
            catch (Exception error)
            {
                // Old Mono may stall while formatting external stack traces; keep errors direct.
                Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
                Environment.Exit(1);
            }
        }
    }
}
