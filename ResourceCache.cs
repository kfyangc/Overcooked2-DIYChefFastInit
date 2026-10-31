using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    // All Unity objects created by an import belong here. Borrowed game assets never do.
    internal sealed class ResourceCache
    {
        internal static readonly List<ResourceCache> All = new List<ResourceCache>();
        internal static ImportWorker Worker;
        internal readonly string Name, Path;
        internal readonly bool IsHat;
        internal DIYChefAvatarData Chef;
        internal HatData Hat;
        internal int References;
        internal float LastUsed;
        internal long Bytes;
        internal bool Imported;
        internal readonly HashSet<ChefMeshReplacer.ChefModelType> FailedModels = new HashSet<ChefMeshReplacer.ChefModelType>();
        internal bool ImportFailed;
        internal readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        internal readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        internal readonly Dictionary<string, Dictionary<string, float>> Materials = new Dictionary<string, Dictionary<string, float>>();
        readonly List<Object> owned = new List<Object>();

        internal ResourceCache(string name, string path, bool hat)
        {
            Name = name; Path = path; IsHat = hat;
            All.Add(this);
        }

        internal T Own<T>(T value, long bytes) where T : Object
        {
            owned.Add(value);
            Bytes += bytes;
            return value;
        }

        internal bool Ready(ChefMeshReplacer.ChefModelType type)
        {
            return IsHat ? Hat.prefab != null : Chef.GetPrefab(type) != null;
        }

        internal bool Failed(ChefMeshReplacer.ChefModelType type)
        {
            return ImportFailed || FailedModels.Contains(type);
        }

        internal IEnumerator Prepare(ChefMeshReplacer.ChefModelType type)
        {
            int checkpoint = owned.Count;
            long previousBytes = Bytes;
            bool completed = false;
            var clock = Stopwatch.StartNew();
            try
            {
                if (!Imported)
                {
                    while (Worker != null && !Worker.Completed) yield return null;
                    Worker = new ImportWorker(Path, IsHat);
                    var worker = Worker;
                    worker.Start();
                    try
                    {
                        while (!worker.Completed) yield return null;
                        if (worker.Error != null) throw new FormatException(worker.Stage, worker.Error);
                        DIYChefPlugin.Log("Import " + Name + " readParseMs=" + worker.ReadParseMs.ToString("F2") +
                            " temporaryBytes=" + worker.Data.Bytes);
                        var raw = worker.Data;
                        // Clear each byte/array buffer as it is consumed, before the next upload.
                        foreach (string key in new List<string>(raw.Textures.Keys))
                        {
                            var upload = Stopwatch.StartNew();
                            Texture2D texture = Own(new Texture2D(2, 2, TextureFormat.RGBA32, true), 0);
                            if (!texture.LoadImage(raw.Textures[key], true))
                                throw new FormatException("PNG decode: " + key);
                            Bytes += (long)texture.width * texture.height * 4 * 4 / 3;
                            Textures.Add(key, texture);
                            raw.Textures.Remove(key);
                            DIYChefPlugin.Log("Texture " + Name + "/" + key + " decodeUploadMs=" + upload.Elapsed.TotalMilliseconds.ToString("F2"));
                            yield return null;
                        }
                        foreach (string key in new List<string>(raw.Meshes.Keys))
                        {
                            var data = raw.Meshes[key];
                            Meshes.Add(key, Own(ObjImporter.CreateMesh(data), data.EstimatedBytes * 2));
                            raw.Meshes.Remove(key);
                            yield return null;
                        }
                        foreach (var pair in raw.Materials) Materials.Add(pair.Key, pair.Value);
                        Imported = true;
                    }
                    finally
                    {
                        worker.Cancelled = true;
                        worker.Data = null;
                    }
                }
                if (!Ready(type))
                {
                    var build = IsHat ? Hat.Build() : Chef.CreatePrefabModel(type);
                    try { while (build.MoveNext()) yield return null; }
                    finally { var disposable = build as IDisposable; if (disposable != null) disposable.Dispose(); }
                }
                LastUsed = Time.realtimeSinceStartup;
                completed = true;
                DIYChefPlugin.Log("Ready " + Name + "/" + type + " prepareMs=" +
                    clock.Elapsed.TotalMilliseconds.ToString("F2") + " cacheBytes=" + Bytes);
            }
            finally
            {
                if (!completed)
                {
                    // A failed/cancelled variant must not destroy an already published variant.
                    for (int i = owned.Count - 1; i >= checkpoint; i--)
                    {
                        if (owned[i] != null) Object.Destroy(owned[i]);
                        owned.RemoveAt(i);
                    }
                    Bytes = previousBytes;
                    if (!Imported || checkpoint == 0)
                    {
                        Textures.Clear(); Meshes.Clear(); Materials.Clear(); Imported = false;
                    }
                }
            }
        }

        internal void Evict()
        {
            if (Chef != null) Chef.ClearPrefabs();
            if (Hat != null && Bytes != 0) Hat.prefab = null;
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.Destroy(owned[i]);
            owned.Clear(); Textures.Clear(); Meshes.Clear(); Materials.Clear();
            Bytes = 0; Imported = false;
        }

        internal static bool OwnsMaterial(Material material)
        {
            foreach (var entry in All)
                if (entry.owned.Contains(material)) return true;
            return false;
        }
    }

    internal sealed class RawResources
    {
        internal readonly Dictionary<string, byte[]> Textures = new Dictionary<string, byte[]>();
        internal readonly Dictionary<string, ObjImporter.MeshData> Meshes = new Dictionary<string, ObjImporter.MeshData>();
        internal readonly Dictionary<string, Dictionary<string, float>> Materials = new Dictionary<string, Dictionary<string, float>>();
        internal long Bytes
        {
            get
            {
                long value = 0;
                foreach (var bytes in Textures.Values) value += bytes.LongLength;
                foreach (var mesh in Meshes.Values) value += mesh.EstimatedBytes;
                return value;
            }
        }
    }

    internal sealed class ImportWorker
    {
        readonly string path;
        readonly bool hat;
        internal volatile bool Completed, Cancelled;
        internal RawResources Data;
        internal Exception Error;
        internal string Stage;
        internal double ReadParseMs;

        internal ImportWorker(string path, bool hat) { this.path = path; this.hat = hat; }
        internal void Start() { new Thread(Read) { IsBackground = true, Name = "DIYChef import" }.Start(); }
        void CheckCancelled() { if (Cancelled) throw new OperationCanceledException(); }
        void Read()
        {
            var timer = Stopwatch.StartNew();
            var result = new RawResources();
            try
            {
                foreach (string file in Directory.GetFiles(path, "t_*.png"))
                {
                    CheckCancelled(); Stage = "read PNG " + file;
                    result.Textures.Add(System.IO.Path.GetFileNameWithoutExtension(file), File.ReadAllBytes(file));
                }
                foreach (string file in Directory.GetFiles(path, "*.obj"))
                {
                    CheckCancelled(); Stage = "parse OBJ " + file;
                    string key = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (hat || DIYChefAvatarData.AllowsPart(key))
                        result.Meshes.Add(key, ObjImporter.ParseFile(file, delegate { return Cancelled; }));
                }
                foreach (string file in Directory.GetFiles(path, "m_*.txt"))
                {
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
                Data = result;
            }
            catch (Exception ex) { Error = ex; }
            finally
            {
                ReadParseMs = timer.Elapsed.TotalMilliseconds;
                Completed = true; // Publication barrier for Data/Error and timing.
            }
        }
    }

    // Attached to actual instances, never to cache prefabs. Inactive instances also pin resources.
    public sealed class ResourceUse : MonoBehaviour
    {
        readonly List<ResourceCache> resources = new List<ResourceCache>();
        readonly HashSet<Material> materials = new HashSet<Material>();

        internal static void Attach(GameObject model, ResourceCache resource)
        {
            if (model == null) return;
            var use = model.GetComponent<ResourceUse>() ?? model.AddComponent<ResourceUse>();
            if (resource != null && !use.resources.Contains(resource))
            {
                use.resources.Add(resource);
                resource.References++;
                resource.LastUsed = Time.realtimeSinceStartup;
            }
            use.CaptureMaterials();
        }

        internal void CaptureMaterials()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.name.EndsWith(" (Instance)", StringComparison.Ordinal) &&
                        !ResourceCache.OwnsMaterial(material))
                        materials.Add(material);
        }

        void OnDestroy()
        {
            // Renderers might already be destroyed by the game; capture during setters as well.
            foreach (var material in materials) if (material != null) Object.Destroy(material);
            foreach (var resource in resources)
            {
                resource.References--;
                resource.LastUsed = Time.realtimeSinceStartup;
            }
        }
    }
}

