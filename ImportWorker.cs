using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;

namespace OC2DIYChef
{
    internal sealed class RawResources : IDisposable
    {
        internal readonly Dictionary<string, RawTexture> Textures = new Dictionary<string, RawTexture>();
        internal readonly Dictionary<string, ObjImporter.MeshData> Meshes = new Dictionary<string, ObjImporter.MeshData>();
        internal readonly Dictionary<string, Dictionary<string, float>> Materials = new Dictionary<string, Dictionary<string, float>>();
        int disposed;
        internal long Bytes
        {
            get
            {
                long value = 0;
                foreach (var texture in Textures.Values)
                    value += texture.Cached != null ? texture.Cached.PixelLength : texture.PNG.LongLength;
                foreach (var mesh in Meshes.Values) value += mesh.EstimatedBytes;
                return value;
            }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            foreach (var texture in Textures.Values) if (texture.Cached != null) texture.Cached.Dispose();
            Textures.Clear(); Meshes.Clear(); Materials.Clear();
        }
    }

    internal sealed class RawTexture
    {
        internal string Source;
        internal byte[] Fingerprint, PNG;
        internal CachedTexture Cached;
    }

    internal sealed class ImportWorker
    {
        readonly string path;
        readonly bool hat;
        readonly Predicate<string> allowsPart;
        internal volatile bool Completed, Cancelled;
        internal RawResources Data;
        internal Exception Error;
        internal string Stage;
        internal double ReadParseMs;
        internal int MeshCacheHits, TextureCacheHits;
        internal double TextureReadMs, MeshReadMs, MaterialReadMs;
        internal AssetDiskCache.ReadTimings Timings;
        internal AssetHash.NativeTimings HashTimings;
        internal double CpuMs;
        internal int Collections;
        internal int Files;
        // FILETIME is an unsigned 64-bit count in 100 ns units.
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] static extern bool GetThreadTimes(IntPtr thread,
            out long creation, out long exit, out long kernel, out long user);

        static long CpuTime()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return -1;
            long creation, exit, kernel, user;
            return GetThreadTimes(GetCurrentThread(), out creation, out exit, out kernel, out user) ? kernel + user : -1;
        }

        internal ImportWorker(string path, bool hat, Predicate<string> allowsPart)
        { this.path = path; this.hat = hat; this.allowsPart = allowsPart; }
        internal void Start() { new Thread(Read) { IsBackground = true, Name = "DIYChef import" }.Start(); }
        internal void Cancel()
        {
            Cancelled = true;
            var data = Interlocked.Exchange(ref Data, null);
            if (data != null) data.Dispose();
        }
        void CheckCancelled() { if (Cancelled) throw new OperationCanceledException(); }
        void Read()
        {
            var timer = Stopwatch.StartNew();
            long cpuStart = CpuTime();
            int collectionsStart = GC.CollectionCount(0);
            var result = new RawResources();
            Timings = AssetDiskCache.Timings = new AssetDiskCache.ReadTimings();
            HashTimings = AssetHash.Timings = new AssetHash.NativeTimings();
            try
            {
                using (AssetHash.BeginBatch())
                {
                    // Enumerate once and classify; do not rescan the directory per asset kind.
                    string[] files = Directory.GetFiles(path);
                    Files = files.Length;
                    foreach (string file in files)
                    {
                        string name = System.IO.Path.GetFileName(file);
                        if (!name.StartsWith("t_", StringComparison.OrdinalIgnoreCase) ||
                            !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                        CheckCancelled(); Stage = "read PNG " + file;
                        // Register ownership before reading/allocating any native pixels.
                        var input = new RawTexture { Source = file };
                        result.Textures.Add(System.IO.Path.GetFileNameWithoutExtension(file), input);
                        byte[] png = null, fingerprint = null;
                        CachedTexture cached = null;
                        if (AssetDiskCache.Enabled)
                        {
                            // Cache hits need a source digest, not a full PNG byte array.
                            using (var source = File.OpenRead(file)) fingerprint = AssetHash.Compute(source);
                            cached = input.Cached = AssetDiskCache.ReadTexture(file, fingerprint, true, delegate { return Cancelled; });
                        }
                        if (cached == null)
                        {
                            CheckCancelled();
                            png = File.ReadAllBytes(file);
                            // Hash the exact bytes sent to LoadImage, even if the source
                            // changed between the initial lookup and this cache miss.
                            if (AssetDiskCache.Enabled) fingerprint = AssetDiskCache.Fingerprint(png);
                        }
                        if (cached != null) TextureCacheHits++;
                        input.Fingerprint = fingerprint;
                        input.PNG = cached == null ? png : null;
                    }
                    TextureReadMs = timer.Elapsed.TotalMilliseconds;
                    foreach (string file in files)
                    {
                        if (!file.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)) continue;
                        CheckCancelled(); Stage = "parse OBJ " + file;
                        string key = System.IO.Path.GetFileNameWithoutExtension(file);
                        if (hat || allowsPart(key))
                        {
                            bool hit;
                            result.Meshes.Add(key, AssetDiskCache.ReadMesh(file, delegate { return Cancelled; }, out hit));
                            if (hit) MeshCacheHits++;
                        }
                    }
                    MeshReadMs = timer.Elapsed.TotalMilliseconds - TextureReadMs;
                    foreach (string file in files)
                    {
                        string name = System.IO.Path.GetFileName(file);
                        if (!name.StartsWith("m_", StringComparison.OrdinalIgnoreCase) ||
                            !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) continue;
                        CheckCancelled(); Stage = "read material " + file;
                        var values = new Dictionary<string, float>();
                        foreach (string line in File.ReadAllLines(file))
                        {
                            int equals = line.IndexOf('=');
                            if (equals < 0) continue;
                            float value;
                            if (float.TryParse(line.Substring(equals + 1), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out value))
                                values[line.Substring(0, equals)] = value;
                        }
                        result.Materials.Add(System.IO.Path.GetFileNameWithoutExtension(file), values);
                    }
                    CheckCancelled();
                    Interlocked.Exchange(ref Data, result);
                    MaterialReadMs = timer.Elapsed.TotalMilliseconds - TextureReadMs - MeshReadMs;
                }
            }
            catch (Exception ex) { Error = ex; }
            finally
            {
                if (Cancelled || Error != null)
                {
                    Interlocked.CompareExchange(ref Data, null, result);
                    result.Dispose();
                }
                ReadParseMs = timer.Elapsed.TotalMilliseconds;
                long cpuEnd = CpuTime();
                CpuMs = cpuStart < 0 || cpuEnd < 0 ? -1 : (cpuEnd - cpuStart) / 10000.0;
                Collections = GC.CollectionCount(0) - collectionsStart;
                AssetDiskCache.Timings = null;
                AssetHash.Timings = null;
                Completed = true; // Publication barrier for Data/Error and timing.
            }
        }
    }

}
