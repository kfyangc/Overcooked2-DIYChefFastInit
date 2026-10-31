// Offline I/O and checksum probe. Does not initialize Unity or launch the game.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;
using OC2DIYChef;

namespace OC2DIYChef.Research
{
    public static class MonoCacheProbe
    {
        static string Source { get { return Environment.GetEnvironmentVariable("DIYCHEF_PROBE_RESOURCE_DIR"); } }
        static string Cache { get { return Environment.GetEnvironmentVariable("DIYCHEF_PROBE_CACHE_DIR"); } }
        static StreamWriter report;
        sealed class GeometryPayload
        {
            internal byte[] Bytes;
            internal int Vertices, Indices;
        }
        static ObjImporter.MeshData LegacyDecode(GeometryPayload payload)
        {
            using (var fields = new BinaryReader(new MemoryStream(payload.Bytes, false)))
            {
                var data = new ObjImporter.MeshData { Vertices = new UnityEngine.Vector3[payload.Vertices],
                    Normals = new UnityEngine.Vector3[payload.Vertices], UV = new UnityEngine.Vector2[payload.Vertices],
                    Triangles = new int[payload.Indices] };
                for (int i = 0; i < payload.Vertices; i++)
                {
                    data.Vertices[i] = new UnityEngine.Vector3(fields.ReadSingle(), fields.ReadSingle(), fields.ReadSingle());
                    data.Normals[i] = new UnityEngine.Vector3(fields.ReadSingle(), fields.ReadSingle(), fields.ReadSingle());
                    data.UV[i] = new UnityEngine.Vector2(fields.ReadSingle(), fields.ReadSingle());
                }
                for (int i = 0; i < payload.Indices; i++)
                {
                    int index = fields.ReadInt32();
                    if (index < 0 || index >= payload.Vertices) throw new FormatException("invalid triangle index");
                    data.Triangles[i] = index;
                }
                return data;
            }
        }
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
            AssetHash.Warning = Console.WriteLine;
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
                var payloads = new List<GeometryPayload>();
                foreach (string mesh in meshes)
                {
                    byte[] bytes = File.ReadAllBytes(AssetDiskCache.CachePath(mesh, 1));
                    var payload = new GeometryPayload { Vertices = BitConverter.ToInt32(bytes, 76),
                        Indices = BitConverter.ToInt32(bytes, 80), Bytes = new byte[bytes.Length - 84] };
                    Buffer.BlockCopy(bytes, 84, payload.Bytes, 0, payload.Bytes.Length);
                    var oldData = LegacyDecode(payload);
                    var newData = MeshCacheCodec.Decode(payload.Bytes, payload.Vertices, payload.Indices, null);
                    if (!oldData.Vertices.SequenceEqual(newData.Vertices) || !oldData.Normals.SequenceEqual(newData.Normals) ||
                        !oldData.UV.SequenceEqual(newData.UV) || !oldData.Triangles.SequenceEqual(newData.Triangles))
                        throw new Exception("Direct decoder differs from BinaryReader on game Mono");
                    payloads.Add(payload);
                }
                Measure("all_mesh_decode_binary_reader", delegate
                { foreach (var payload in payloads) LegacyDecode(payload); });
                Measure("all_mesh_decode_direct", delegate
                { foreach (var payload in payloads) MeshCacheCodec.Decode(payload.Bytes, payload.Vertices, payload.Indices, null); });
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
                Action load = delegate
                {
                    byte[] bytes = File.ReadAllBytes(png);
                    if (AssetDiskCache.ReadTexture(png, AssetDiskCache.Fingerprint(bytes)) == null)
                        throw new Exception("Texture cache missed");
                    foreach (string mesh in meshes)
                    {
                        bool hit; AssetDiskCache.ReadMesh(mesh, null, out hit);
                        if (!hit) throw new Exception("Mesh cache missed");
                    }
                };
                Measure("full_worker_background_thread", delegate
                {
                    Exception error = null;
                    var thread = new Thread(delegate() { try { load(); } catch (Exception ex) { error = ex; } });
                    thread.Start(); thread.Join();
                    if (error != null) throw error;
                });
                // Keep an explicit live heap and avoid collecting outside the timed load.
                // This is a controlled allocation test, not a substitute for game profiling.
                var retained = new byte[128][];
                for (int i = 0; i < retained.Length; i++) retained[i] = new byte[1024 * 1024];
                for (int i = 0; i < 8; i++)
                {
                    int before = GC.CollectionCount(0);
                    var timer = Stopwatch.StartNew(); load();
                    Console.WriteLine("steady_worker_" + i + "," + timer.Elapsed.TotalMilliseconds.ToString("F3") +
                        ",collections=" + (GC.CollectionCount(0) - before));
                }
                GC.KeepAlive(retained);
                for (int i = 0; i < 3; i++)
                {
                    var worker = new ImportWorker(Source, false, delegate { return true; });
                    worker.Cancelled = false;
                    worker.Start();
                    while (!worker.Completed) Thread.Sleep(1);
                    if (worker.Error != null) throw worker.Error;
                    Console.WriteLine("actual_import_worker total=" + worker.ReadParseMs.ToString("F3") +
                        " texture=" + worker.TextureReadMs.ToString("F3") + " mesh=" + worker.MeshReadMs.ToString("F3") +
                        " material=" + worker.MaterialReadMs.ToString("F3") +
                        " sourceHash=" + worker.Timings.SourceHashMs.ToString("F3") +
                        " payloadReadHash=" + worker.Timings.CacheMs.ToString("F3") +
                        " meshDecode=" + worker.Timings.MeshDecodeMs.ToString("F3") +
                        " cpu=" + worker.CpuMs.ToString("F3") + " collections=" + worker.Collections);
                }
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
