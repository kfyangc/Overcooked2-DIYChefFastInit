using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    // Identifies render-only instances to optional integrations, without naming real player chefs.
    public sealed class ChefGridPortraitActor : MonoBehaviour { }

    // One isolated, deferred FrontendChef renders small portraits. No roster-sized set of 3D models.
    internal sealed class ChefGridPortraits : IDisposable
    {
        const int Size = 128;
        const int DetailSize = 512;
        int capacity = 96;
        readonly Dictionary<ChefGridEntry, Texture2D> images = new Dictionary<ChefGridEntry, Texture2D>();
        readonly LinkedList<ChefGridEntry> recent = new LinkedList<ChefGridEntry>();
        readonly HashSet<ChefGridEntry> failed = new HashSet<ChefGridEntry>();
        readonly Dictionary<ChefGridEntry, string> paths = new Dictionary<ChefGridEntry, string>();
        readonly GameObject stage;
        readonly FrontendChef view;
        readonly ChefMeshReplacer replacer;
        readonly Camera camera;
        readonly RenderTexture target;
        readonly RenderTexture detailTarget;
        readonly List<Vector3> bakedVertices = new List<Vector3>();
        readonly ChefColourData colour;
        readonly HashSet<ChefGridEntry> failedDetails = new HashSet<ChefGridEntry>();
        Texture2D detailImage;
        ChefGridEntry detailEntry;
        ChefGridEntry pending;
        int pendingSize;
        float started;
        int readyFrame;

        internal ChefGridPortraits(FrontendChefCustomisation source, float yaw)
        {
            colour = source.GetCurrentSelection().Colour;
            stage = new GameObject("DIYChef portrait studio");
            stage.transform.position = new Vector3(10000, -10000, 10000);
            try
            {
                var sourceReplacer = (ChefMeshReplacer)AccessTools.Field(typeof(FrontendChefCustomisation), "m_chef").GetValue(source);
                var templateField = AccessTools.Field(typeof(ChefMeshReplacer), "m_chefTemplatePrefab");
                var template = (GameObject)templateField.GetValue(sourceReplacer);
                var actor = new GameObject("Portrait chef");
                actor.transform.SetParent(stage.transform, false);
                actor.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                actor.AddComponent<ChefGridPortraitActor>();
                var placeholder = new GameObject("Chef");
                placeholder.transform.SetParent(actor.transform, false);
                replacer = actor.AddComponent<ChefMeshReplacer>();
                templateField.SetValue(replacer, template);
                AccessTools.Field(typeof(ChefMeshReplacer), "m_modelType").SetValue(replacer, ChefMeshReplacer.ChefModelType.FrontEnd);
                var sourceHat = source.GetComponent<HatMeshVisibility>();
                if (sourceHat != null)
                {
                    var hat = actor.AddComponent<HatMeshVisibility>();
                    hat.m_meshes = sourceHat.m_meshes;
                    hat.m_stateFlags = sourceHat.m_stateFlags;
                }
                view = actor.AddComponent<FrontendChef>();
                var sourceView = source.GetComponent<FrontendChef>();
                if (sourceView != null)
                    foreach (var name in new[] { "m_StandardSkinShader", "m_StandardClothesShader", "m_UISkinShader", "m_UIClothesShader" })
                    {
                        var field = AccessTools.Field(typeof(FrontendChef), name);
                        field.SetValue(view, field.GetValue(sourceView));
                    }
                view.SetShaderMode(FrontendChef.ShaderMode.eUI);
                view.SetUIChefAmbientLighting(new Color(0.8f, 0.8f, 0.8f, 1));
                var cameraObject = new GameObject("Portrait camera");
                cameraObject.transform.SetParent(stage.transform, false);
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.useOcclusionCulling = false;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 30;
                camera.cullingMask = (1 << actor.layer) | (1 << HatData.hatLayer);
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
                target.Create();
                detailTarget = new RenderTexture(DetailSize, DetailSize, 24, RenderTextureFormat.ARGB32);
                detailTarget.Create();
                camera.targetTexture = target;
                var lamp = new GameObject("Portrait light");
                lamp.transform.SetParent(stage.transform, false);
                lamp.transform.localPosition = new Vector3(-2, 3, 4);
                var light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 12;
                light.intensity = 2;
                light.shadows = LightShadows.None;
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

        internal Texture2D GetDetail(ChefGridEntry entry)
        {
            return detailEntry == entry && detailImage != null ? detailImage : Get(entry);
        }

        void Store(ChefGridEntry entry, Texture2D texture, int size)
        {
            texture.filterMode = FilterMode.Bilinear;
            if (size == DetailSize)
            {
                if (detailImage != null) Object.Destroy(detailImage);
                detailEntry = entry;
                detailImage = texture;
                return;
            }
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

        string CachePath(ChefGridEntry entry, int size)
        {
            string path;
            if (paths.TryGetValue(entry, out path)) return size == Size ? path : Path.ChangeExtension(path, ".512.png");
            var key = new StringBuilder("portrait-v7|" + entry.Avatar.HeadName + "|" + entry.Hat + "|" +
                stage.transform.GetChild(0).localEulerAngles.y + "|" + colour.name);
            var game = new FileInfo(typeof(ChefAvatarData).Assembly.Location);
            key.Append('|').Append(game.Length).Append('|').Append(game.LastWriteTimeUtc.Ticks);
            var custom = entry.Avatar as DIYChefAvatarData;
            if (custom != null) AppendFiles(key, custom.Resources.Path);
            var hat = DIYChefCustomisation.diyHats.Find(x => x.HatName == "Hat_" + entry.Hat);
            if (hat != null) AppendFiles(key, hat.Resources.Path);
            // This is a disposable visual cache. Resource loading still uses full content checksums.
            byte[] bytes = Encoding.UTF8.GetBytes(key.ToString());
            byte[] hash;
            using (var sha = new System.Security.Cryptography.SHA256Managed()) hash = sha.ComputeHash(bytes);
            path = Path.Combine(Path.Combine(Paths.CachePath, "DIYChefFastInit/portraits"),
                BitConverter.ToString(hash).Replace("-", "") + ".png");
            paths[entry] = path;
            return size == Size ? path : Path.ChangeExtension(path, ".512.png");
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
            ChefGridEntry selected = wanted.Count == 0 ? null : wanted[0];
            // User-facing previews take priority over background thumbnail work.
            if (ChefLoading.HasPendingExcept(replacer))
            {
                if (pending != null) { ChefLoading.Cancel(replacer); pending = null; }
                return;
            }
            if (pending != null && (!wanted.Contains(pending) ||
                (pendingSize == DetailSize && pending != selected)))
            {
                ChefLoading.Cancel(replacer);
                pending = null;
            }
            if (pending != null)
            {
                var custom = pending.Avatar as DIYChefAvatarData;
                if (custom != null && custom.Resources.Failed(ChefMeshReplacer.ChefModelType.FrontEnd))
                {
                    Fail(pending, "custom model could not be loaded");
                    ChefLoading.Cancel(replacer);
                    pending = null;
                    return;
                }
                var data = replacer.GetChefData();
                if (data != null && data.Character == pending.Avatar && view.ChefModel != null)
                {
                    SilenceModel();
                    // Allow one update for skinned bounds after the asynchronous setter completes.
                    if (readyFrame == 0) { readyFrame = Time.frameCount + 1; return; }
                    if (Time.frameCount < readyFrame) return;
                    try
                    {
                        var texture = Capture(pendingSize, pending.Avatar.HeadName);
                        Store(pending, texture, pendingSize);
                        Save(pending, texture, pendingSize);
                    }
                    catch (Exception ex) { Fail(pending, ex.Message); }
                    pending = null;
                }
                else if (Time.realtimeSinceStartup - started > 30)
                {
                    Fail(pending, "model preparation timed out");
                    ChefLoading.Cancel(replacer);
                    pending = null;
                }
                return;
            }
            // A selected chef gets its own high-resolution render, never an enlarged thumbnail.
            if (selected != null && detailEntry != selected && !failed.Contains(selected) && !failedDetails.Contains(selected))
            {
                if (!TryLoad(selected, DetailSize)) Begin(selected, DetailSize);
                return;
            }
            foreach (var entry in wanted)
            {
                if (images.ContainsKey(entry) || failed.Contains(entry)) continue;
                if (!TryLoad(entry, Size)) Begin(entry, Size);
                return;
            }
            // Stop drawing the studio between requests; at most one extra model remains pinned.
            if (replacer.ChefModel != null && replacer.ChefModel.activeSelf) replacer.ChefModel.SetActive(false);
        }

        void Fail(ChefGridEntry entry, string reason)
        {
            if (pendingSize == DetailSize) failedDetails.Add(entry);
            else failed.Add(entry);
            DIYChefPlugin.Warn("Portrait " + entry.Avatar.HeadName + ": " + reason);
        }

        void Begin(ChefGridEntry entry, int size)
        {
            try
            {
                pending = entry;
                pendingSize = size;
                started = Time.realtimeSinceStartup;
                readyFrame = 0;
                view.SetChefData(new GameSession.SelectedChefData(entry.Avatar, colour), true);
            }
            catch (Exception ex) { Fail(entry, ex.Message); pending = null; }
        }

        bool TryLoad(ChefGridEntry entry, int size)
        {
            var custom = entry.Avatar as DIYChefAvatarData;
            string supplied = custom == null ? null : Path.Combine(custom.Resources.Path, "preview.png");
            if (supplied != null && LoadFile(entry, supplied, size)) return true;
            try { return LoadFile(entry, CachePath(entry, size), size); }
            catch (Exception ex) { DIYChefPlugin.Warn("Portrait cache read: " + ex.Message); return false; }
        }

        bool LoadFile(ChefGridEntry entry, string path, int size)
        {
            Texture2D cached = null;
            try
            {
                if (!File.Exists(path)) return false;
                cached = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                // A small supplied preview can serve the grid; the detail view needs actual pixels.
                if (!cached.LoadImage(File.ReadAllBytes(path)) || cached.width < size || cached.height < size ||
                    cached.width > DetailSize || cached.height > DetailSize)
                {
                    Object.Destroy(cached);
                    return false;
                }
                if (size == Size && (cached.width != Size || cached.height != Size))
                {
                    var original = cached;
                    cached = Downsample(original);
                    Object.Destroy(original);
                }
                cached.Apply(false, true);
                Store(entry, cached, size);
                return true;
            }
            catch (Exception ex)
            {
                if (cached != null) Object.Destroy(cached);
                DIYChefPlugin.Warn("Portrait cache read: " + ex.Message);
                return false;
            }
        }

        void Save(ChefGridEntry entry, Texture2D texture, int size)
        {
            try
            {
                string path = CachePath(entry, size);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            catch (Exception ex) { DIYChefPlugin.Warn("Portrait cache write: " + ex.Message); }
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

        Texture2D Capture(int size, string headName)
        {
            view.ChefModel.SetActive(true);
            SilenceModel();
            ChefGridPose.Prepare(replacer, replacer.GetChefData().Character);
            view.SetChefHat(HatMeshVisibility.VisState.Fancy);
            ChefLoading.Capture(view);
            using (var snapshot = new ChefGridSnapshot())
            {
                var all = new ChefPortraitBounds();
                var head = new ChefPortraitBounds();
                var eyes = new ChefPortraitBounds();
                var upper = new ChefPortraitBounds();
                var anchors = FaceAnchors();
                float front = 0;
                var renderers = view.ChefModel.GetComponentsInChildren<Renderer>();
                foreach (var renderer in renderers)
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer is ParticleSystemRenderer) continue;
                    ChefGridVertexRole[] roles;
                    bool headPart = renderer.name == headName || renderer.name == "Head1" || renderer.name == "Head2";
                    bool fallbackPart = !renderer.name.StartsWith("Hat", StringComparison.OrdinalIgnoreCase) &&
                        !renderer.name.StartsWith("Knife", StringComparison.OrdinalIgnoreCase);
                    var part = Measure(snapshot.Freeze(renderer, out roles), roles, headPart,
                        ChefGridPose.IsOpenEye(renderer.name), fallbackPart, anchors, ref head, ref eyes, ref upper, ref front);
                    all.Include(part);
                }
                if (!head.Valid) head = upper;
                var framing = ChefGridFraming.Calculate(all, head, eyes, anchors, size == Size);
                camera.transform.localPosition = new Vector3(framing.X, framing.Y, front + 8);
                camera.transform.localRotation = Quaternion.Euler(0, 180, 0);
                camera.orthographicSize = framing.HalfSize;
                return RenderTransparent(size);
            }
        }

        Texture2D RenderTransparent(int size)
        {
            var previous = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            var previousBackground = camera.backgroundColor;
            Texture2D result = null, white = null;
            try
            {
                var destination = size == DetailSize ? detailTarget : target;
                camera.targetTexture = destination;
                camera.backgroundColor = Color.black;
                camera.Render();
                result = ReadTarget(destination, size);
                camera.backgroundColor = Color.white;
                camera.Render();
                white = ReadTarget(destination, size);
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
            var skeleton = view.ChefModel.transform.Find("Skeleton");
            var head = skeleton == null ? null : skeleton.FindChildRecursive("Head");
            var eyes = head == null ? null : head.FindChildRecursive("Eyes");
            if (head == null || eyes == null) return new ChefPortraitAnchors();
            var neck = stage.transform.InverseTransformPoint(head.position);
            var eye = stage.transform.InverseTransformPoint(eyes.position);
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
                var matrix = stage.transform.worldToLocalMatrix * renderer.localToWorldMatrix;
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
                var min = stage.transform.InverseTransformPoint(renderer.bounds.min);
                var max = stage.transform.InverseTransformPoint(renderer.bounds.max);
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

        void SilenceModel()
        {
            foreach (var audio in view.ChefModel.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
            foreach (var rumble in view.ChefModel.GetComponentsInChildren<AnimatorRumbleComponent>(true)) rumble.enabled = false;
            foreach (var audio in view.ChefModel.GetComponentsInChildren<AnimatorAudioComponent>(true)) audio.enabled = false;
        }

        public void Dispose()
        {
            if (replacer != null) ChefLoading.Cancel(replacer);
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.Destroy(target); }
            if (detailTarget != null) { detailTarget.Release(); Object.Destroy(detailTarget); }
            if (detailImage != null) { Object.Destroy(detailImage); detailImage = null; }
            if (stage != null) { stage.SetActive(false); Object.Destroy(stage); }
            foreach (var texture in images.Values) Object.Destroy(texture);
            images.Clear();
            recent.Clear();
        }
    }
}
