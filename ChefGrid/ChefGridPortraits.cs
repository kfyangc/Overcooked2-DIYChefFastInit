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
        const int Capacity = 96;
        readonly Dictionary<ChefGridEntry, Texture2D> images = new Dictionary<ChefGridEntry, Texture2D>();
        readonly LinkedList<ChefGridEntry> recent = new LinkedList<ChefGridEntry>();
        readonly HashSet<ChefGridEntry> failed = new HashSet<ChefGridEntry>();
        readonly Dictionary<ChefGridEntry, string> paths = new Dictionary<ChefGridEntry, string>();
        readonly GameObject stage;
        readonly FrontendChef view;
        readonly ChefMeshReplacer replacer;
        readonly Camera camera;
        readonly RenderTexture target;
        readonly ChefColourData colour;
        ChefGridEntry pending;
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
                camera.backgroundColor = new Color(0.18f, 0.33f, 0.43f, 1);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 30;
                camera.cullingMask = (1 << actor.layer) | (1 << HatData.hatLayer);
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
                target.Create();
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

        void Store(ChefGridEntry entry, Texture2D texture)
        {
            texture.filterMode = FilterMode.Bilinear;
            images[entry] = texture;
            recent.AddLast(entry);
            while (images.Count > Capacity)
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
            var key = new StringBuilder("portrait-v1|" + entry.Avatar.HeadName + "|" + entry.Hat + "|" +
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
            // User-facing previews take priority over background thumbnail work.
            if (ChefLoading.HasPendingExcept(replacer))
            {
                if (pending != null) { ChefLoading.Cancel(replacer); pending = null; }
                return;
            }
            if (pending != null && !wanted.Contains(pending))
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
                        var texture = Capture();
                        Store(pending, texture);
                        try
                        {
                            string path = CachePath(pending);
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                            File.WriteAllBytes(path, texture.EncodeToPNG());
                        }
                        catch (Exception ex) { DIYChefPlugin.Warn("Portrait cache write: " + ex.Message); }
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
            foreach (var entry in wanted)
            {
                if (images.ContainsKey(entry) || failed.Contains(entry)) continue;
                Texture2D cached = null;
                try
                {
                    var custom = entry.Avatar as DIYChefAvatarData;
                    string supplied = custom == null ? null : Path.Combine(custom.Resources.Path, "preview.png");
                    string path = supplied != null && File.Exists(supplied) ? supplied : CachePath(entry);
                    if (File.Exists(path))
                    {
                        cached = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (cached.LoadImage(File.ReadAllBytes(path)) && cached.width <= 512 && cached.height <= 512)
                        {
                            cached.Apply(false, true);
                            Store(entry, cached);
                            return;
                        }
                        Object.Destroy(cached);
                        cached = null;
                    }
                }
                catch (Exception ex)
                {
                    if (cached != null) Object.Destroy(cached);
                    DIYChefPlugin.Warn("Portrait cache read: " + ex.Message);
                }
                try
                {
                    pending = entry;
                    started = Time.realtimeSinceStartup;
                    readyFrame = 0;
                    view.SetChefData(new GameSession.SelectedChefData(entry.Avatar, colour), true);
                }
                catch (Exception ex) { Fail(entry, ex.Message); pending = null; }
                return;
            }
            // Stop drawing the studio between requests; at most one extra model remains pinned.
            if (replacer.ChefModel != null && replacer.ChefModel.activeSelf) replacer.ChefModel.SetActive(false);
        }

        void Fail(ChefGridEntry entry, string reason)
        {
            failed.Add(entry);
            DIYChefPlugin.Warn("Portrait " + entry.Avatar.HeadName + ": " + reason);
        }

        Texture2D Capture()
        {
            view.ChefModel.SetActive(true);
            view.SetChefHat(HatMeshVisibility.VisState.Fancy);
            ChefLoading.Capture(view);
            SilenceModel();
            foreach (var animator in view.ChefModel.GetComponentsInChildren<Animator>(true))
            {
                animator.Update(0);
                animator.enabled = false;
            }
            var bounds = new Bounds();
            bool found = false;
            foreach (var renderer in view.ChefModel.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer is ParticleSystemRenderer) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found) throw new InvalidOperationException("model has no visible renderers");
            float height = Mathf.Max(0.2f, bounds.size.y);
            Vector3 center = bounds.center + Vector3.up * height * 0.13f;
            camera.transform.position = center + new Vector3(0, 0, 8);
            camera.transform.LookAt(center);
            camera.orthographicSize = Mathf.Max(height * 0.43f, bounds.extents.x * 1.1f);
            var previous = RenderTexture.active;
            Texture2D result = null;
            try
            {
                camera.Render();
                RenderTexture.active = target;
                result = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                result.Apply(false, false);
                return result;
            }
            catch { if (result != null) Object.Destroy(result); throw; }
            finally { RenderTexture.active = previous; }
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
            if (stage != null) { stage.SetActive(false); Object.Destroy(stage); }
            foreach (var texture in images.Values) Object.Destroy(texture);
            images.Clear();
            recent.Clear();
        }
    }
}
