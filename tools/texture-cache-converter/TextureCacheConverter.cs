using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using OC2DIYChef;

namespace OC2DIYChef.Research
{
    public static class MonoCacheProbe
    {
        public static void Run()
        {
            string root = Environment.GetEnvironmentVariable("DIYCHEF_TEXTURE_CACHE_ROOT");
            AssetDiskCache.Root = root;
            AssetDiskCache.Enabled = true;
            AssetDiskCache.BackgroundCompressionEnabled = false;
            AssetDiskCache.Log = Console.WriteLine;
            AssetHash.Warning = Console.WriteLine;
            AssetHash.Timings = null;
            AssetDiskCache.Timings = null;
            if (!Lz4Codec.Initialise(Path.Combine(root, "native"), Console.WriteLine)) throw new Exception("LZ4 native dependency unavailable");
            string[] paths = Directory.GetFiles(root, "*.texture");
            Array.Sort(paths, StringComparer.Ordinal);
            long before = 0, after = 0;
            int changed = 0, completed = 0;
            var timer = Stopwatch.StartNew();
            foreach (string path in paths)
            {
                before += new FileInfo(path).Length;
                if (AssetDiskCache.CompressTextureCache(path)) changed++;
                if (!AssetDiskCache.ValidateTextureCache(path)) throw new FormatException("Texture validation failed: " + path);
                after += new FileInfo(path).Length;
                if (NativePixelBuffer.AllocatedBytes != 0) throw new Exception("Conversion retained native pixels");
                Console.WriteLine("Validated " + (++completed) + "/" + paths.Length);
            }
            string report = "count=" + paths.Length + "\nconverted=" + changed + "\nbeforeBytes=" + before +
                "\nafterBytes=" + after + "\nelapsedMs=" + timer.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture) + "\n";
            Console.WriteLine(report);
            string output = Environment.GetEnvironmentVariable("DIYCHEF_TEXTURE_CACHE_REPORT");
            if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, report);
        }
    }
}
