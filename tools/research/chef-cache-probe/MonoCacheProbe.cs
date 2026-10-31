// Offline I/O and checksum probe. Does not initialize Unity or launch the game.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using OC2DIYChef;

namespace OC2DIYChef.Research
{
    public static class MonoCacheProbe
    {
        static string Source { get { return Environment.GetEnvironmentVariable("DIYCHEF_PROBE_RESOURCE_DIR"); } }
        static string Cache { get { return Environment.GetEnvironmentVariable("DIYCHEF_PROBE_CACHE_DIR"); } }
        static StreamWriter report;
        static void Measure(string name, Action action)
        {
            action();
            var samples = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                var timer = Stopwatch.StartNew(); action(); timer.Stop();
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            string row = name + "," + samples[2].ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            Console.WriteLine(row); report.WriteLine(row); report.Flush();
        }
        public static void Run()
        {
            AssetDiskCache.Root = Cache;
            AssetDiskCache.Log = Console.WriteLine;
            string png = Path.Combine(Source, "t_Head.png");
            byte[] pngBytes = File.ReadAllBytes(png);
            byte[] fingerprint = AssetDiskCache.Fingerprint(pngBytes);
            string cachePath = AssetDiskCache.CachePath(png, 2);
            byte[] cacheBytes = File.ReadAllBytes(cachePath);
            string[] meshes = Directory.GetFiles(Source, "*.obj");
            using (report = new StreamWriter(Environment.GetEnvironmentVariable("DIYCHEF_PROBE_REPORT"), false))
            {
                report.WriteLine("stage,median_ms");
                Console.WriteLine("PointerSize=" + IntPtr.Size + "; CLR=" + Environment.Version +
                    "; PNG=" + pngBytes.Length + "; textureCache=" + cacheBytes.Length);
                Measure("png_file_read", delegate { File.ReadAllBytes(png); });
                Measure("png_sha256", delegate { AssetDiskCache.Fingerprint(pngBytes); });
                Measure("texture_cache_file_read", delegate { File.ReadAllBytes(cachePath); });
                Measure("texture_payload_sha256_memory", delegate
                {
                    using (var sha = new SHA256Managed()) sha.ComputeHash(cacheBytes, 76, cacheBytes.Length - 76);
                });
                using (var sha = new SHA256Managed())
                    if (!sha.ComputeHash(cacheBytes, 76, cacheBytes.Length - 76).SequenceEqual(NativeSha256.Hash(cacheBytes, 76)))
                        throw new Exception("Native SHA256 result mismatch");
                Measure("texture_payload_sha256_windows", delegate { NativeSha256.Hash(cacheBytes, 76); });
                Measure("texture_payload_sha256_stream", delegate
                {
                    using (var input = File.OpenRead(cachePath))
                    using (var sha = new SHA256Managed()) { input.Position = 76; sha.ComputeHash(input); }
                });
                Measure("texture_cached_load_with_validation", delegate
                {
                    if (AssetDiskCache.ReadTexture(png, fingerprint) == null) throw new Exception("Texture cache missed");
                });
                Measure("all_mesh_source_sha256", delegate
                {
                    foreach (string mesh in meshes)
                        using (var input = File.OpenRead(mesh))
                        using (var sha = new SHA256Managed()) sha.ComputeHash(input);
                });
                Measure("all_mesh_cached_load_with_validation", delegate
                {
                    foreach (string mesh in meshes)
                    {
                        bool hit; AssetDiskCache.ReadMesh(mesh, null, out hit);
                        if (!hit) throw new Exception("Mesh cache missed: " + mesh);
                    }
                });
                Measure("full_worker_texture_and_meshes", delegate
                {
                    byte[] bytes = File.ReadAllBytes(png);
                    if (AssetDiskCache.ReadTexture(png, AssetDiskCache.Fingerprint(bytes)) == null)
                        throw new Exception("Texture cache missed");
                    foreach (string mesh in meshes)
                    {
                        bool hit; AssetDiskCache.ReadMesh(mesh, null, out hit);
                        if (!hit) throw new Exception("Mesh cache missed");
                    }
                });
            }
        }
    }
}

static class NativeSha256
{
    [DllImport("advapi32.dll", SetLastError=true)]
    static extern bool CryptAcquireContext(out IntPtr provider, string container, string name, uint type, uint flags);
    [DllImport("advapi32.dll", SetLastError=true)]
    static extern bool CryptCreateHash(IntPtr provider, uint algorithm, IntPtr key, uint flags, out IntPtr hash);
    [DllImport("advapi32.dll", SetLastError=true)]
    static extern bool CryptHashData(IntPtr hash, IntPtr data, uint length, uint flags);
    [DllImport("advapi32.dll", SetLastError=true)]
    static extern bool CryptGetHashParam(IntPtr hash, uint parameter, byte[] value, ref uint length, uint flags);
    [DllImport("advapi32.dll")] static extern bool CryptDestroyHash(IntPtr hash);
    [DllImport("advapi32.dll")] static extern bool CryptReleaseContext(IntPtr provider, uint flags);
    internal static byte[] Hash(byte[] bytes, int offset)
    {
        IntPtr provider, hash = IntPtr.Zero;
        if (!CryptAcquireContext(out provider, null, null, 24, 0xf0000000)) throw new Exception("Acquire: " + Marshal.GetLastWin32Error());
        try
        {
            if (!CryptCreateHash(provider, 0x800c, IntPtr.Zero, 0, out hash)) throw new Exception("Create hash");
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IntPtr data = new IntPtr(pin.AddrOfPinnedObject().ToInt64() + offset);
                if (!CryptHashData(hash, data, (uint)(bytes.Length-offset), 0)) throw new Exception("Hash data");
                byte[] result = new byte[32]; uint length=32;
                if (!CryptGetHashParam(hash, 2, result, ref length, 0)) throw new Exception("Hash param");
                return result;
            }
            finally { pin.Free(); }
        }
        finally { if (hash != IntPtr.Zero) CryptDestroyHash(hash); CryptReleaseContext(provider, 0); }
    }
}
