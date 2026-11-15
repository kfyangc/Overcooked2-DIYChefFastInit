using BepInEx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    // One isolated, deferred FrontendChef renders small portraits. No roster-sized set of 3D models.
    internal sealed class ChefGridPortraits : IDisposable
    {
        const int Size = 128;
        int capacity = 96;
        readonly Dictionary<ChefGridEntry, Texture2D> images = new Dictionary<ChefGridEntry, Texture2D>();
        readonly LinkedList<ChefGridEntry> recent = new LinkedList<ChefGridEntry>();
        readonly HashSet<ChefGridEntry> failed = new HashSet<ChefGridEntry>();
        readonly Dictionary<ChefGridEntry, string> paths = new Dictionary<ChefGridEntry, string>();
        readonly ChefGridPreviewScene scene;
        readonly RenderTexture target;
        readonly List<Vector3> bakedVertices = new List<Vector3>();
        readonly ChefColourData colour;
        readonly ChefGridCacheWarmup<ChefGridEntry> thumbnailCache = new ChefGridCacheWarmup<ChefGridEntry>();
        ChefGridEntry pending;
        float started;
        int readyFrame;

        internal ChefGridPortraits(FrontendChefCustomisation source, float yaw)
        {
            // A single neutral, hatless outfit shares thumbnails across players,
            // teams and scenes. Keep the existing colour name and image cache key.
            colour = ChefGridPortraitColour.Get();
            try
            {
                scene = new ChefGridPreviewScene(source, new Vector3(10000, -10000, 10000), Color.clear);
                scene.Actor.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                PlayerHats.BindThumbnail(scene.Actor);
                target = scene.CreateTarget(Size, RenderTextureFormat.ARGB32);
                scene.Camera.targetTexture = target;
            }
            catch { Dispose(); throw; }
        }

        internal Texture2D Get(ChefGridEntry entry)
        {
            Texture2D value;
            if (!images.TryGetValue(entry, out value)) return null;
            recent.Remove(entry);
            recent.AddLast(entry);
            return value;
        }

        void Store(ChefGridEntry entry, Texture2D texture)
        {
            texture.filterMode = FilterMode.Bilinear;
            // Portrait UVs cover the full image. Do not wrap the bottom clothing
            // pixels into the transparent top edge during bilinear filtering.
            texture.wrapMode = TextureWrapMode.Clamp;
            thumbnailCache.Available(entry);
            images[entry] = texture;
            recent.AddLast(entry);
            while (images.Count > capacity)
            {
                var oldest = recent.First.Value;
                recent.RemoveFirst();
                Object.Destroy(images[oldest]);
                images.Remove(oldest);
            }
        }

        string CachePath(ChefGridEntry entry)
        {
            string path;
            if (paths.TryGetValue(entry, out path)) return path;
            var key = new StringBuilder("portrait-v9-nohat|" + entry.Avatar.HeadName + "|" +
                scene.Actor.transform.localEulerAngles.y + "|" + colour.name);
            var game = new FileInfo(typeof(ChefAvatarData).Assembly.Location);
            key.Append('|').Append(game.Length).Append('|').Append(game.LastWriteTimeUtc.Ticks);
            var custom = entry.Avatar as DIYChefAvatarData;
            if (custom != null) AppendFiles(key, custom.Resources.Path);
            // This is a disposable visual cache. Resource loading still uses full content checksums.
            byte[] bytes = Encoding.UTF8.GetBytes(key.ToString());
            byte[] hash;
            using (var sha = new System.Security.Cryptography.SHA256Managed()) hash = sha.ComputeHash(bytes);
            path = Path.Combine(Path.Combine(Paths.CachePath, "DIYChefFastInit/portraits"),
                BitConverter.ToString(hash).Replace("-", "") + ".png");
            paths[entry] = path;
            return path;
        }

        static void AppendFiles(StringBuilder key, string directory)
        {
            if (!Directory.Exists(directory)) return;
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string path in files)
            {
                var file = new FileInfo(path);
                key.Append('|').Append(path).Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks);
            }
        }

        internal void Tick(List<ChefGridEntry> wanted)
        {
            capacity = Math.Max(96, wanted.Count);
            // Cache IO does not need the studio or the original menu's camera animation.
            // Fill several cells per frame even when a real player's model is still loading.
            var warmup = Stopwatch.StartNew();
            using (AssetHash.BeginBatch())
            {
                thumbnailCache.Warm(wanted, ThumbnailReady, LoadThumbnail, () => warmup.Elapsed.TotalMilliseconds < 6);
            }
            // User-facing previews take priority over background thumbnail work.
            if (ChefLoading.HasPendingExcept(scene.Replacer))
            {
                if (pending != null) { ChefLoading.Cancel(scene.Replacer); pending = null; }
                return;
            }
            if (pending != null && !wanted.Contains(pending))
            {
                ChefLoading.Cancel(scene.Replacer);
                pending = null;
            }
            if (pending != null)
            {
                var custom = pending.Avatar as DIYChefAvatarData;
                if (custom != null && custom.Resources.Failed(ChefMeshReplacer.ChefModelType.FrontEnd))
                {
                    Fail(pending, "custom model could not be loaded");
                    ChefLoading.Cancel(scene.Replacer);
                    pending = null;
                    return;
                }
                var data = scene.Replacer.GetChefData();
                if (data != null && data.Character == pending.Avatar && scene.View.ChefModel != null)
                {
                    scene.SilenceModel();
                    // Allow one update for skinned bounds after the asynchronous setter completes.
                    if (readyFrame == 0) { readyFrame = Time.frameCount + 1; return; }
                    if (Time.frameCount < readyFrame) return;
                    try
                    {
                        var texture = Capture(pending.Avatar.HeadName);
                        Store(pending, texture);
                        try { ChefGridPortraitCache.Save(CachePath(pending), texture); }
                        catch (Exception ex) { DIYChefPlugin.Warn("Portrait cache write: " + ex.Message); }
                    }
                    catch (Exception ex) { Fail(pending, ex.Message); }
                    pending = null;
                }
                else if (Time.realtimeSinceStartup - started > 30)
                {
                    Fail(pending, "model preparation timed out");
                    ChefLoading.Cancel(scene.Replacer);
                    pending = null;
                }
                return;
            }
            foreach (var entry in wanted)
            {
                if (images.ContainsKey(entry) || failed.Contains(entry)) continue;
                // Entries not reached within this frame's IO budget may already be cached.
                // Probe them next frame before spending time constructing a model.
                if (!thumbnailCache.Missing(entry)) continue;
                Begin(entry);
                return;
            }
            // Stop drawing the studio between requests; at most one extra model remains pinned.
            if (scene.Replacer.ChefModel != null && scene.Replacer.ChefModel.activeSelf) scene.Replacer.ChefModel.SetActive(false);
        }

        bool ThumbnailReady(ChefGridEntry entry) { return images.ContainsKey(entry) || failed.Contains(entry); }
        bool LoadThumbnail(ChefGridEntry entry) { return TryLoad(entry); }

        void Fail(ChefGridEntry entry, string reason)
        {
            failed.Add(entry);
            DIYChefPlugin.Warn("Portrait " + entry.Avatar.HeadName + ": " + reason);
        }

        void Begin(ChefGridEntry entry)
        {
            try
            {
                pending = entry;
                started = Time.realtimeSinceStartup;
                readyFrame = 0;
                scene.View.SetChefData(new GameSession.SelectedChefData(entry.Avatar, colour), true);
            }
            catch (Exception ex) { Fail(entry, ex.Message); pending = null; }
        }

        bool TryLoad(ChefGridEntry entry)
        {
            // Author previews may contain hats; regenerate the uniform hatless image.
            string supplied = null;
            Texture2D cached = null;
            try
            {
                cached = ChefGridPortraitCache.Load(CachePath(entry), supplied, Size, Downsample);
                if (cached == null) return false;
                Store(entry, cached);
                return true;
            }
            catch (Exception ex)
            {
                if (cached != null) Object.Destroy(cached);
                DIYChefPlugin.Warn("Portrait cache read: " + ex.Message);
                return false;
            }
        }

        Texture2D Downsample(Texture2D source)
        {
            var previous = RenderTexture.active;
            try { Graphics.Blit(source, target); return ReadTarget(target, Size); }
            finally { RenderTexture.active = previous; }
        }

        static Texture2D ReadTarget(RenderTexture source, int size)
        {
            Texture2D result = null;
            try
            {
                RenderTexture.active = source;
                result = new Texture2D(size, size, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                result.Apply(false, false);
                return result;
            }
            catch { if (result != null) Object.Destroy(result); throw; }
        }

        Texture2D Capture(string headName)
        {
            scene.View.ChefModel.SetActive(true);
            scene.SilenceModel();
            ChefGridPose.Prepare(scene.Replacer, scene.Replacer.GetChefData().Character);
            scene.View.SetChefHat(HatMeshVisibility.VisState.None);
            ChefLoading.Capture(scene.View);
            using (var snapshot = new ChefGridSnapshot())
            {
                var all = new ChefPortraitBounds();
                var head = new ChefPortraitBounds();
                var eyes = new ChefPortraitBounds();
                var upper = new ChefPortraitBounds();
                var anchors = FaceAnchors();
                float front = 0;
                var measured = new List<ChefPortraitBounds>();
                var depths = new List<float>();
                var renderers = scene.View.ChefModel.GetComponentsInChildren<Renderer>();
                foreach (var renderer in renderers)
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer is ParticleSystemRenderer) continue;
                    ChefGridVertexRole[] roles;
                    bool headPart = renderer.name == headName || renderer.name == "Head1" || renderer.name == "Head2";
                    bool fallbackPart = !renderer.name.StartsWith("Hat", StringComparison.OrdinalIgnoreCase) &&
                        !renderer.name.StartsWith("Knife", StringComparison.OrdinalIgnoreCase);
                    float partFront = 0;
                    measured.Add(Measure(snapshot.Freeze(renderer, out roles), roles, headPart,
                        ChefGridPose.IsOpenEye(renderer.name), fallbackPart, anchors, ref head, ref eyes, ref upper, ref partFront));
                    depths.Add(partFront);
                }
                if (!head.Valid) head = upper;
                for (int i = 0; i < measured.Count; i++)
                    if (ChefGridFraming.ContributesToFraming(measured[i], head))
                    {
                        all.Include(measured[i]);
                        front = Mathf.Max(front, depths[i]);
                    }
                var framing = ChefGridFraming.Calculate(all, head, eyes, anchors, true);
                scene.Camera.transform.localPosition = new Vector3(framing.X, framing.Y, front + 8);
                scene.Camera.transform.localRotation = Quaternion.Euler(0, 180, 0);
                scene.Camera.orthographicSize = framing.HalfSize;
                return RenderTransparent();
            }
        }

        Texture2D RenderTransparent()
        {
            var camera = scene.Camera;
            var previous = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            var previousBackground = camera.backgroundColor;
            Texture2D result = null, white = null;
            try
            {
                camera.targetTexture = target;
                camera.backgroundColor = Color.black;
                camera.Render();
                result = ReadTarget(target, Size);
                camera.backgroundColor = Color.white;
                camera.Render();
                white = ReadTarget(target, Size);
                byte[] pixels = result.GetRawTextureData();
                ChefGridTransparency.Combine(pixels, white.GetRawTextureData());
                result.LoadRawTextureData(pixels);
                result.Apply(false, false);
                return result;
            }
            catch { if (result != null) Object.Destroy(result); throw; }
            finally
            {
                if (white != null) Object.Destroy(white);
                camera.targetTexture = previousTarget;
                camera.backgroundColor = previousBackground;
                RenderTexture.active = previous;
            }
        }

        ChefPortraitAnchors FaceAnchors()
        {
            var skeleton = scene.View.ChefModel.transform.Find("Skeleton");
            var head = skeleton == null ? null : skeleton.FindChildRecursive("Head");
            var eyes = head == null ? null : head.FindChildRecursive("Eyes");
            if (head == null || eyes == null) return new ChefPortraitAnchors();
            var neck = scene.Stage.transform.InverseTransformPoint(head.position);
            var eye = scene.Stage.transform.InverseTransformPoint(eyes.position);
            return new ChefPortraitAnchors { Valid = Math.Abs(eye.y - neck.y) > 0.0001f,
                CenterX = eye.x, NeckY = neck.y, EyeY = eye.y };
        }

        ChefPortraitBounds Measure(Renderer renderer, ChefGridVertexRole[] roles, bool headPart, bool eyePart,
            bool fallbackPart, ChefPortraitAnchors anchors, ref ChefPortraitBounds head, ref ChefPortraitBounds eyes,
            ref ChefPortraitBounds upper, ref float front)
        {
            Mesh mesh = null;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable) mesh = filter.sharedMesh;
            var bounds = new ChefPortraitBounds();
            if (mesh != null)
            {
                bakedVertices.Clear();
                mesh.GetVertices(bakedVertices);
                var matrix = scene.Stage.transform.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int i = 0; i < bakedVertices.Count; i++)
                {
                    var point = matrix.MultiplyPoint3x4(bakedVertices[i]);
                    bounds.Include(point.x, point.y);
                    var role = roles != null && i < roles.Length ? roles[i] : ChefGridVertexRole.None;
                    if (headPart && (roles == null || (role & ChefGridVertexRole.Head) != 0))
                        ChefGridFraming.IncludeHead(ref head, point.x, point.y, anchors);
                    if (eyePart || (role & ChefGridVertexRole.Eyes) != 0) eyes.Include(point.x, point.y);
                    if (fallbackPart) ChefGridFraming.IncludeHead(ref upper, point.x, point.y, anchors);
                    front = Mathf.Max(front, point.z);
                }
            }
            else
            {
                var min = scene.Stage.transform.InverseTransformPoint(renderer.bounds.min);
                var max = scene.Stage.transform.InverseTransformPoint(renderer.bounds.max);
                bounds.Include(min.x, min.y); bounds.Include(max.x, max.y);
                float clippedMinY = anchors.Valid ? Math.Max(min.y, anchors.ChinY) : min.y;
                if (max.y >= clippedMinY)
                {
                    if (headPart) { head.Include(min.x, clippedMinY); head.Include(max.x, max.y); }
                    if (fallbackPart) { upper.Include(min.x, clippedMinY); upper.Include(max.x, max.y); }
                }
                if (eyePart) eyes.Include(bounds);
                front = Mathf.Max(front, max.z);
            }
            return bounds;
        }

        public void Dispose()
        {
            if (scene != null) scene.Dispose();
            foreach (var texture in images.Values) Object.Destroy(texture);
            images.Clear();
            recent.Clear();
        }
    }
}
