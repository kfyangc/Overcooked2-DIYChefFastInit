using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace OC2DIYChef
{
    internal static class ChefLoading
    {
        sealed class Request
        {
            internal FrontendChef View;
            internal ChefMeshReplacer Replacer;
            internal GameSession.SelectedChefData Data;
            internal bool Force;
            internal ChefMeshReplacer.ChefModelType Type;
            internal Stopwatch Timer = Stopwatch.StartNew();
        }

        static readonly Dictionary<int, Request> pending = new Dictionary<int, Request>();
        static readonly FieldInfo modelType = AccessTools.Field(typeof(ChefMeshReplacer), "m_modelType");
        static ConfigEntry<int> budget, count;
        static ConfigEntry<bool> diagnostics;
        static IEnumerator work;
        static ResourceCache active;
        static ChefMeshReplacer.ChefModelType activeType;
        static readonly List<float> frames = new List<float>();
        static float nextReport;
        internal static bool Applying;
        internal static bool Diagnostics { get { return diagnostics != null && diagnostics.Value; } }

        internal static void Configure(ConfigFile config)
        {
            budget = config.Bind("Loading", "CacheBudgetMiB", IntPtr.Size == 4 ? 64 : 128,
                "Estimated plugin resource budget in MiB; active instances may exceed it.");
            count = config.Bind("Loading", "MaxCachedChefs", IntPtr.Size == 4 ? 3 : 6,
                "Target number of resident chefs. Active and pending chefs remain pinned.");
            diagnostics = config.Bind("Loading", "Diagnostics", false,
                "Log frame P95/max, process memory and first camera-render latency.");
            AssetDiskCache.Enabled = config.Bind("Loading", "DiskCacheEnabled", true,
                "Cache parsed OBJ geometry and decoded texture pixels on disk; changed source files rebuild automatically.").Value;
            AssetDiskCache.Root = Path.Combine(Paths.CachePath, "DIYChefFastInit");
            AssetDiskCache.Log = DIYChefPlugin.Log;
        }

        internal static ChefMeshReplacer.ChefModelType Type(ChefMeshReplacer replacer)
        {
            return (ChefMeshReplacer.ChefModelType)modelType.GetValue(replacer);
        }

        static HatData Hat(GameSession.SelectedChefData data)
        {
            if (data == null || DIYChefCustomisation.preferredChefs == null) return null;
            var pair = DIYChefCustomisation.preferredChefs.FirstOrDefault(x => x.Key == data.Character.HeadName);
            return DIYChefCustomisation.diyHats.Find(x => x.HatName == "Hat_" + pair.Value);
        }

        static ResourceCache Needed(Request request)
        {
            var chef = request.Data.Character as DIYChefAvatarData;
            if (chef != null && !chef.Resources.Ready(request.Type) && !chef.Resources.Failed(request.Type))
                return chef.Resources;
            var hat = Hat(request.Data);
            if (hat != null && hat.prefab == null && !hat.Resources.Failed(request.Type)) return hat.Resources;
            return null;
        }

        static bool Wants(ResourceCache resource)
        {
            foreach (var request in pending.Values)
            {
                var chef = request.Data.Character as DIYChefAvatarData;
                var hat = Hat(request.Data);
                if ((chef != null && chef.Resources == resource) ||
                    (hat != null && hat.Resources == resource)) return true;
            }
            return false;
        }

        static GameSession.SelectedChefData Resolve(GameSession.SelectedChefData data, ChefMeshReplacer.ChefModelType type)
        {
            var chef = data.Character as DIYChefAvatarData;
            return chef != null && !chef.Resources.Ready(type) ?
                new GameSession.SelectedChefData(chef.template, data.Colour) : data;
        }

        internal static bool Submit(FrontendChef view, GameSession.SelectedChefData data, bool force)
        {
            if (data == null || data.Character == null) return true;
            var replacer = view.GetComponent<ChefMeshReplacer>();
            if (replacer == null) return true; // Original Awake will initialize it.
            int id = replacer.GetInstanceID();
            Request previous;
            pending.TryGetValue(id, out previous);
            var request = new Request { View = view, Replacer = replacer, Data = data,
                Force = force, Type = Type(replacer) };
            // Repeated lobby refreshes must not reset input-to-display timing.
            if (previous != null && previous.Data.Character == data.Character && previous.Data.Colour == data.Colour)
                request.Timer = previous.Timer;
            pending.Remove(id); // Includes switching back to a stock chef.
            if (Needed(request) == null && Resolve(data, request.Type) == data) return true;
            pending[id] = request;
            return false; // No original setter state is changed; old model keeps animating.
        }

        internal static void Cancel(ChefMeshReplacer replacer)
        {
            if (replacer == null) return;
            pending.Remove(replacer.GetInstanceID());
            if (active != null && !Wants(active)) CancelWork();
        }

        internal static void EnsureImmediate(ref GameSession.SelectedChefData data, ChefMeshReplacer replacer)
        {
            if (data == null) return;
            var request = new Request { Data = data, Replacer = replacer, Type = Type(replacer) };
            var resource = Needed(request);
            if (resource == null)
            {
                data = Resolve(data, request.Type);
                return;
            }
            var timer = Stopwatch.StartNew();
            // InGame's caller configures attachment/component caches immediately after this method.
            // Complete required resources here; never take this path for deferred frontend input.
            CancelWork();
            while (ResourceCache.Worker != null && !ResourceCache.Worker.Completed) Thread.Sleep(1);
            while ((resource = Needed(request)) != null)
            {
                StartWork(resource, request.Type);
                while (work != null)
                {
                    Advance();
                    if (work != null && ResourceCache.Worker != null && !ResourceCache.Worker.Completed)
                        Thread.Sleep(1);
                }
            }
            data = Resolve(data, request.Type);
            DIYChefPlugin.Log("InGame synchronous compatibility waitMs=" + timer.Elapsed.TotalMilliseconds.ToString("F2"));
        }

        static void StartWork(ResourceCache resource, ChefMeshReplacer.ChefModelType type)
        {
            active = resource; activeType = type;
            work = resource.Prepare(type);
        }

        static void Advance()
        {
            try
            {
                if (work.MoveNext()) return;
                (work as IDisposable)?.Dispose();
                work = null; active = null;
            }
            catch (Exception ex)
            {
                if (!active.Imported) active.ImportFailed = true;
                else active.FailedModels.Add(activeType);
                DIYChefPlugin.Log("Loading failed chef=" + active.Name + " resource=" + active.Path +
                    " model=" + activeType + " stage=prepare: " + ex);
                CancelWork();
            }
        }

        static void CancelWork()
        {
            if (work != null) (work as IDisposable)?.Dispose();
            work = null; active = null;
        }

        internal static void Update()
        {
            foreach (var item in pending.ToArray())
                if (item.Value.View == null || item.Value.Replacer == null ||
                    !item.Value.View.isActiveAndEnabled)
                    pending.Remove(item.Key);

            if (active != null && !Wants(active)) CancelWork();
            if (work == null)
            {
                foreach (var request in pending.Values)
                {
                    var needed = Needed(request);
                    if (needed == null) continue;
                    StartWork(needed, request.Type);
                    break;
                }
            }
            var slice = Stopwatch.StartNew();
            // No fixed "one operation per frame" latency; consume up to 3 ms of ready work.
            while (work != null && slice.Elapsed.TotalMilliseconds < 3)
            {
                Advance();
                if (ResourceCache.Worker != null && !ResourceCache.Worker.Completed) break;
            }
            foreach (var item in pending.ToArray())
            {
                var request = item.Value;
                if (Needed(request) != null) continue;
                pending.Remove(item.Key);
                Applying = true;
                try
                {
                    var applyTimer = Stopwatch.StartNew();
                    request.View.SetChefData(Resolve(request.Data, request.Type), request.Force);
                    request.View.SetChefHat((HatMeshVisibility.VisState)AccessTools.Field(typeof(FrontendChef), "m_HatState").GetValue(request.View));
                    Capture(request.View);
                    if (Diagnostics) DIYChefPlugin.Log("ApplyCost " + request.Data.Character.HeadName +
                        " elapsedMs=" + applyTimer.Elapsed.TotalMilliseconds.ToString("F2"));
                    Track(request.Replacer, request.Data);
                    if (Diagnostics) ChefDrawProbe.Observe(request.Replacer.ChefModel, request.Data.Character.HeadName, request.Timer);
                    DIYChefPlugin.Log("Apply " + request.Data.Character.HeadName + "/" + request.Type +
                        " inputToApplyMs=" + request.Timer.Elapsed.TotalMilliseconds.ToString("F2"));
                }
                catch (Exception ex) { DIYChefPlugin.Log("Apply failed " + request.Data.Character.HeadName + ": " + ex); }
                finally { Applying = false; }
            }
            Trim();
            Sample();
        }

        internal static void Track(ChefMeshReplacer replacer, GameSession.SelectedChefData data)
        {
            if (replacer == null || replacer.ChefModel == null) return;
            var chef = data == null ? null : data.Character as DIYChefAvatarData;
            if (chef != null && chef.GetPrefab(Type(replacer)) != null)
                ResourceUse.Attach(replacer.ChefModel, chef.Resources);
            else ResourceUse.Attach(replacer.ChefModel, null);
        }

        internal static void Capture(FrontendChef view)
        {
            if (view.ChefModel == null) return;
            var use = view.ChefModel.GetComponent<ResourceUse>();
            if (use != null) use.CaptureMaterials();
        }

        static void Trim()
        {
            long total = ResourceCache.All.Sum(x => x.Bytes);
            int chefs = ResourceCache.All.Count(x => !x.IsHat && x.Bytes != 0);
            long target = Math.Max(1, budget.Value) * 1024L * 1024L;
            int limit = Math.Max(1, count.Value);
            foreach (var resource in ResourceCache.All.OrderBy(x => x.LastUsed))
            {
                if (total <= target && chefs <= limit) break;
                if (resource.Bytes == 0 || resource.References != 0 || resource == active || Wants(resource)) continue;
                DIYChefPlugin.Log("Evict " + resource.Name + " bytes=" + resource.Bytes + " reason=cache-budget");
                total -= resource.Bytes;
                if (!resource.IsHat) chefs--;
                resource.Evict();
            }
        }

        static void Sample()
        {
            if (!Diagnostics) return;
            frames.Add(Time.unscaledDeltaTime * 1000);
            if (Time.realtimeSinceStartup < nextReport) return;
            nextReport = Time.realtimeSinceStartup + 5;
            frames.Sort();
            try
            {
                DIYChefPlugin.Log("Sample frames=" + frames.Count +
                        " p95Ms=" + frames[(frames.Count - 1) * 95 / 100].ToString("F2") +
                        " maxMs=" + frames[frames.Count - 1].ToString("F2") +
                        ProcessMemory.Read() +
                        " cacheBytes=" + ResourceCache.All.Sum(x => x.Bytes) +
                        " temporaryBytes=" + ((ResourceCache.Worker != null && ResourceCache.Worker.Data != null ? ResourceCache.Worker.Data.Bytes : 0) + AssetDiskCache.PendingWriteBytes) +
                        " pinned=" + ResourceCache.All.Count(x => x.References > 0) +
                        " budgetBlockedByPins=" + (ResourceCache.All.Sum(x => x.Bytes) > Math.Max(1, budget.Value) * 1024L * 1024L) +
                        " pending=" + pending.Count + " active=" + (active == null ? "-" : active.Name));
            }
            catch (Exception ex) { DIYChefPlugin.Log("Memory sample unavailable: " + ex.Message); }
            frames.Clear();
        }

        internal static void Shutdown()
        {
            pending.Clear(); CancelWork();
            ChefSelectionMenu.Clear();
            DIYChefCustomisation.Shutdown();
            foreach (var resource in ResourceCache.All) if (resource.References == 0) resource.Evict();
        }
    }

    // Callback records a frame rendered by a camera, rather than only a model assignment.
    public sealed class ChefDrawProbe : MonoBehaviour
    {
        sealed class Observation
        {
            internal Stopwatch Timer;
            internal string Chef;
            internal bool Reported;
        }
        Observation observation;
        Camera seenBy;
        internal static void Observe(GameObject model, string chef, Stopwatch timer)
        {
            if (model == null) return;
            var observation = new Observation { Timer = timer, Chef = chef };
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var probe = renderer.GetComponent<ChefDrawProbe>() ?? renderer.gameObject.AddComponent<ChefDrawProbe>();
                probe.observation = observation;
            }
        }
        void OnEnable() { Camera.onPostRender += AfterCamera; }
        void OnDisable() { Camera.onPostRender -= AfterCamera; seenBy = null; }
        void OnWillRenderObject() { if (observation != null && !observation.Reported) seenBy = Camera.current; }
        void AfterCamera(Camera camera)
        {
            if (observation == null || observation.Reported || seenBy != camera) return;
            observation.Reported = true;
            DIYChefPlugin.Log("FirstCameraRender " + observation.Chef + " inputToRenderMs=" +
                observation.Timer.Elapsed.TotalMilliseconds.ToString("F2") + " frame=" + Time.frameCount);
            observation = null; seenBy = null;
        }
    }
}
