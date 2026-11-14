// Worker-only probe: no Unity object creation and no game launch.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace OC2DIYChef.Research
{
    // The shared MonoCacheHost looks up this type and Run method.
    public static class MonoCacheProbe
    {
        const int Repeats = 7;
        static StreamWriter report;

        static void Line(string message)
        {
            Console.WriteLine(message);
            report.WriteLine(message);
            report.Flush();
        }

        static string F(double value) { return value.ToString("F3", CultureInfo.InvariantCulture); }

        static void Equal(ObjImporter.MeshData source, ObjImporter.MeshData cached, string name)
        {
            if (source.Vertices.Length != cached.Vertices.Length || source.Normals.Length != cached.Normals.Length ||
                source.UV.Length != cached.UV.Length || source.Triangles.Length != cached.Triangles.Length)
                throw new Exception("Array length mismatch: " + name);
            for (int i = 0; i < source.Vertices.Length; i++)
            {
                if (source.Vertices[i].x != cached.Vertices[i].x || source.Vertices[i].y != cached.Vertices[i].y ||
                    source.Vertices[i].z != cached.Vertices[i].z || source.Normals[i].x != cached.Normals[i].x ||
                    source.Normals[i].y != cached.Normals[i].y || source.Normals[i].z != cached.Normals[i].z ||
                    source.UV[i].x != cached.UV[i].x || source.UV[i].y != cached.UV[i].y)
                    throw new Exception("Vertex mismatch: " + name + " at " + i);
            }
            for (int i = 0; i < source.Triangles.Length; i++)
                if (source.Triangles[i] != cached.Triangles[i]) throw new Exception("Triangle mismatch: " + name);
        }

        sealed class Sample
        {
            internal double Total, SourceHash, CacheRead, PayloadHash, MeshDecode;
            internal int Collections;
        }

        static void Measure(string label, string[] files, bool cached)
        {
            var samples = new List<Sample>();
            AssetDiskCache.Enabled = cached;
            try
            {
                // One untimed warm-up, then seven timed loads with warm OS file cache.
                for (int repeat = -1; repeat < Repeats; repeat++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    var timings = AssetDiskCache.Timings = new AssetDiskCache.ReadTimings();
                    int collections = GC.CollectionCount(0);
                    long vertices = 0;
                    var timer = Stopwatch.StartNew();
                    using (AssetHash.BeginBatch())
                        foreach (string file in files)
                        {
                            bool hit;
                            var data = AssetDiskCache.ReadMesh(file, null, out hit);
                            if (cached && !hit) throw new Exception("Expected mesh hit: " + file);
                            vertices += data.Vertices.Length;
                        }
                    timer.Stop();
                    if (repeat < 0) continue;
                    var sample = new Sample { Total = timer.Elapsed.TotalMilliseconds,
                        SourceHash = timings.SourceHashMs, CacheRead = timings.CacheReadMs,
                        PayloadHash = timings.PayloadHashMs, MeshDecode = timings.MeshDecodeMs,
                        Collections = GC.CollectionCount(0) - collections };
                    samples.Add(sample);
                    Line("sample," + label + "," + repeat + "," + F(sample.Total) +
                        ",sourceHash=" + F(sample.SourceHash) + ",cacheRead=" + F(sample.CacheRead) +
                        ",payloadHash=" + F(sample.PayloadHash) + ",decode=" + F(sample.MeshDecode) +
                        ",gen0=" + sample.Collections + ",vertices=" + vertices);
                }
                samples.Sort(delegate(Sample left, Sample right) { return left.Total.CompareTo(right.Total); });
                var median = samples[Repeats / 2];
                Line("median," + label + "," + F(median.Total) +
                    ",min=" + F(samples[0].Total) + ",max=" + F(samples[Repeats - 1].Total) +
                    ",sourceHash=" + F(median.SourceHash) + ",cacheRead=" + F(median.CacheRead) +
                    ",payloadHash=" + F(median.PayloadHash) + ",decode=" + F(median.MeshDecode) +
                    ",gen0=" + median.Collections);
            }
            finally { AssetDiskCache.Timings = null; }
        }

        static string RequireEnvironment(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("Missing environment variable: " + name);
            return Path.GetFullPath(value);
        }

        public static void Run()
        {
            // The launcher fixes this path under repository artifacts/cache-formats.
            string output = RequireEnvironment("DIYCHEF_MESH_PROBE_OUTPUT");
            Directory.CreateDirectory(output);
            using (report = new StreamWriter(Path.Combine(output, "mesh-format-results.txt"), false))
            try
            {
                string resources = RequireEnvironment("DIYCHEF_MESH_PROBE_RESOURCES");
                string[] files = Directory.GetFiles(resources, "*.obj", SearchOption.AllDirectories);
                if (files.Length == 0) throw new ArgumentException("No OBJ files under: " + resources);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                Line("PointerSize=" + IntPtr.Size + "; CLR=" + Environment.Version + "; corpus=" + resources + "; files=" + files.Length);
                Line("Samples=" + Repeats + "; warmup=1; OS file cache=warm; stage=worker-only");
                int utf8Files = 0, nulFiles = 0;
                long sourceBytes = 0, vertices = 0, indices = 0, cacheBytes = 0;
                string largest = null;
                long largestBytes = -1;
                AssetDiskCache.Root = Path.Combine(output, "cache");
                AssetDiskCache.Enabled = true;
                AssetDiskCache.BackgroundCompressionEnabled = false;
                AssetDiskCache.Log = delegate(string message) { throw new Exception(message); };
                foreach (string file in files)
                {
                    byte[] bytes = File.ReadAllBytes(file);
                    sourceBytes += bytes.Length;
                    if (bytes.Length > largestBytes) { largestBytes = bytes.Length; largest = file; }
                    foreach (byte value in bytes) if (value == 0) { nulFiles++; break; }
                    try { new UTF8Encoding(false, true).GetString(bytes); utf8Files++; }
                    catch (DecoderFallbackException) { }
                    // A new run directory ensures the first load parses OBJ and writes its cache.
                    bool hit;
                    var parsed = AssetDiskCache.ReadMesh(file, null, out hit);
                    if (hit) throw new Exception("Expected isolated first-load miss: " + file);
                    var restored = AssetDiskCache.ReadMesh(file, null, out hit);
                    if (!hit) throw new Exception("Expected generated mesh hit: " + file);
                    Equal(parsed, restored, file);
                    vertices += parsed.Vertices.Length;
                    indices += parsed.Triangles.Length;
                    cacheBytes += new FileInfo(AssetDiskCache.CachePath(file, 1)).Length;
                }
                Line("inventory,files=" + files.Length + ",strictUTF8=" + utf8Files + ",containsNUL=" + nulFiles +
                    ",sourceBytes=" + sourceBytes + ",cacheBytes=" + cacheBytes +
                    ",expandedVertices=" + vertices + ",indices=" + indices);
                Line("equivalence,all_components_equal,files=" + files.Length);
                Line("largest," + largest + ",bytes=" + largestBytes);
                using (var reader = File.OpenText(largest))
                    for (int i = 0; i < 6; i++)
                    {
                        string line = reader.ReadLine();
                        if (line == null) break;
                        Line("source_preview," + line);
                    }
                Measure("all_obj_no_cache", files, false);
                Measure("all_mesh_cache_hit", files, true);
                Measure("largest_obj_no_cache", new[] { largest }, false);
                Measure("largest_mesh_cache_hit", new[] { largest }, true);
                string arctic = Path.Combine(resources, "01-ArcticFox");
                if (Directory.Exists(arctic))
                {
                    string[] group = Directory.GetFiles(arctic, "*.obj");
                    Measure("ArcticFox_obj_no_cache", group, false);
                    Measure("ArcticFox_mesh_cache_hit", group, true);
                }
                Line("PASS: no Unity objects created; no game process started; original resources/caches untouched.");
            }
            catch (Exception error)
            {
                // Keep exceptions away from the legacy Mono host's exception formatter.
                // The launcher requires the PASS marker as well as host exit code zero.
                Line("FAIL: " + error.GetType().FullName + ": " + error.Message);
            }
        }
    }
}
