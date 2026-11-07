using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using OC2DIYChef;
using UnityEngine;

// Exercise the production bridge with plugin/Unity adapters, including incompatible
// future plugin shapes. No Unity objects or game processes are loaded by this test.
static class ModelTestCompatibilityRegression
{
    static int assertions;

    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        assertions++;
    }

    static void Reset(BaseUnityPlugin plugin = null, string pluginId = "OC2.ModelTest.1")
    {
        ModelTestCompatibility.Shutdown();
        Chainloader.PluginInfos.Clear();
        if (plugin != null) Chainloader.PluginInfos.Add(pluginId, new PluginInfo { Instance = plugin });
        DIYChefPlugin.Messages.Clear();
        DIYChefPlugin.Warnings.Clear();
        Time.frameCount = 100;
    }

    static void Tick(int frame)
    {
        Time.frameCount = frame;
        ModelTestCompatibility.LateUpdate();
    }

    static void Replace()
    {
        ModelTestCompatibility.ModelReplaced(null, new GameObject());
    }

    static void Main()
    {
        Reset();
        Replace();
        ModelTestCompatibility.Initialize();
        Tick(101);
        Replace();
        Tick(102);
        Check(DIYChefPlugin.Messages.Count == 0 && DIYChefPlugin.Warnings.Count == 0,
            "no ModelTest installed: optional bridge stays silent");

        foreach (string id in new[] { "OC2.ModelTest", "OC2.ModelTest.", "OC2.ModelTest.1", "OC2.ModelTest.2",
            "OC2.ModelTest.3", "OC2.ModelTest.10", "OC2.ModelTest.999999999999999999999999999999",
            "OC2.ModelTest.helper", "OC2.ModelTest.2.extra", "OC2.ModelTest.-1", "OC2.ModelTestBeta",
            "Other.OC2.ModelTest.2", "ModelTest", "xOC2.ModelTest", "modeltest", "org.ModelTEST.plugin" })
        {
            var candidate = new ModelTestPlugin(true);
            Reset(candidate, id);
            ModelTestCompatibility.Initialize();
            Replace();
            Tick(101);
            Check(candidate.Particles.Calls == 1 && DIYChefPlugin.Messages[0].Contains(id),
                id + ": discover plugin ID containing modeltest without case sensitivity");
        }
        foreach (string id in new[] { "OC2.ModelTes", "Other.Plugin", "model.test", "modeltes" })
        {
            var unrelated = new ModelTestPlugin(true);
            Reset(unrelated, id);
            ModelTestCompatibility.Initialize();
            Replace();
            Tick(101);
            Check(unrelated.Particles.Calls == 0 && DIYChefPlugin.Messages.Count == 0 && DIYChefPlugin.Warnings.Count == 0,
                id + ": ignore IDs without the modeltest substring");
        }
        Reset(new ModelTestPlugin(true), "Other.Plugin");
        var discovered = new ModelTestPlugin(true);
        Chainloader.PluginInfos.Add("OC2.ModelTest.3", new PluginInfo { Instance = discovered });
        ModelTestCompatibility.Initialize();
        Replace();
        Tick(101);
        Check(discovered.Particles.Calls == 1, "skip unrelated plugins and find a later ModelTest ID");

        var plugin = new ModelTestPlugin(true);
        Reset(plugin);
        Replace(); // A chef may be set by another plugin before FastInit.Start.
        Tick(101);
        Check(plugin.Particles.Calls == 0, "wait for chainloader discovery");
        ModelTestCompatibility.Initialize();
        ModelTestCompatibility.Initialize();
        Tick(101);
        Check(plugin.Particles.Calls == 1 && DIYChefPlugin.Messages.Count == 1,
            "request before initialization survives, bind only once");
        Tick(102);
        Check(plugin.Particles.Calls == 1, "idle frames do not rescan the scene");

        Reset(plugin = new ModelTestPlugin(true));
        ModelTestCompatibility.Initialize();
        var model = new GameObject();
        ModelTestCompatibility.ModelReplaced(model, model);
        ModelTestCompatibility.ModelReplaced(model, null);
        Tick(101);
        Check(plugin.Particles.Calls == 0, "same instance or missing model does not refresh");
        ModelTestCompatibility.ModelReplaced(model, new GameObject());
        Tick(101);
        Check(plugin.Particles.Calls == 0, "do not attach before frontend setters finish this frame");
        Replace();
        Tick(102);
        Check(plugin.Particles.Calls == 1, "multiple chefs/replacements coalesce to one next-frame scan");
        Replace();
        Tick(103);
        Check(plugin.Particles.Calls == 2, "switching back to a cached chef also requests a refresh");

        Reset(plugin = new ModelTestPlugin(false));
        plugin.SetParticles(null);
        ModelTestCompatibility.Initialize();
        Replace();
        Tick(101);
        Tick(500);
        Check(DIYChefPlugin.Warnings.Count == 0, "unfinished resources are not an interface failure");
        plugin.SetParticles(new Particles());
        plugin.resourcesLoaded = true;
        Tick(501);
        Tick(502);
        Check(plugin.Particles.Calls == 1, "slow resource loading preserves and executes the request once");

        Reset(plugin = new ModelTestPlugin(true));
        ModelTestCompatibility.Initialize();
        plugin.Particles.OnExecute = Replace;
        Replace();
        Tick(101);
        plugin.Particles.OnExecute = null;
        Tick(102);
        Check(plugin.Particles.Calls == 2, "a replacement during Execute is retained for the next frame");

        Reset(plugin = new ModelTestPlugin(true));
        ModelTestCompatibility.Initialize();
        plugin.Particles.Throw = true;
        Replace();
        Tick(101); // Must not propagate an exception into FastInit's update.
        Replace();
        Tick(102);
        ModelTestCompatibility.Initialize();
        Check(plugin.Particles.Calls == 1 && DIYChefPlugin.Warnings.Count == 1,
            "invocation failure disables only the bridge and warns once");
        Check(DIYChefPlugin.Warnings[0].Contains("test particle failure"),
            "report the underlying plugin error rather than a reflection wrapper");

        Reset(plugin = new ModelTestPlugin(true));
        plugin.SetParticles(null);
        ModelTestCompatibility.Initialize();
        Replace();
        Tick(101);
        Replace();
        Tick(102);
        Check(DIYChefPlugin.Warnings.Count == 1, "ready flag with a missing particle manager fails once");

        foreach (var incompatible in new BaseUnityPlugin[] {
            new MissingReady(), new WrongReadyType(), new MissingParticles(),
            new MissingExecute(), new ParameterExecute(), new ReturningExecute(), new StaticExecute(), new GenericExecute()
        })
        {
            Reset(incompatible, "OC2.ModelTest");
            Replace();
            ModelTestCompatibility.Initialize();
            ModelTestCompatibility.Initialize();
            Tick(101);
            Replace();
            Tick(102);
            Check(DIYChefPlugin.Warnings.Count == 1 && DIYChefPlugin.Messages.Count == 0,
                incompatible.GetType().Name + ": interface changes disable once without throwing");
        }

        Reset(plugin = new ModelTestPlugin(true));
        ModelTestCompatibility.Initialize();
        Replace();
        ModelTestCompatibility.Shutdown();
        Tick(101);
        Check(plugin.Particles.Calls == 0, "shutdown discards pending work and plugin references");
        ModelTestCompatibility.Initialize();
        Replace();
        Tick(102);
        Check(plugin.Particles.Calls == 1, "a new lifecycle can initialize cleanly");

        Console.WriteLine("PASS: " + assertions + " ModelTest compatibility assertions; no Unity/game process used.");
    }

    sealed class ModelTestPlugin : BaseUnityPlugin
    {
        internal bool resourcesLoaded;
        private Particles particleReplacer = new Particles();
        public Particles Particles { get { return particleReplacer; } }
        public ModelTestPlugin(bool ready) { resourcesLoaded = ready; }
        public void SetParticles(Particles value) { particleReplacer = value; }
    }

    public sealed class Particles
    {
        public int Calls;
        public bool Throw;
        public Action OnExecute;
        public void Execute()
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("test particle failure");
            if (OnExecute != null) OnExecute();
        }
    }

    sealed class MissingReady : BaseUnityPlugin { public Particles particleReplacer = new Particles(); }
    sealed class WrongReadyType : BaseUnityPlugin { public int resourcesLoaded = 1; public Particles particleReplacer = new Particles(); }
    sealed class MissingParticles : BaseUnityPlugin { public bool resourcesLoaded = true; }
    sealed class MissingExecute : BaseUnityPlugin { public bool resourcesLoaded = true; public object particleReplacer = new object(); }
    sealed class ParameterExecute : BaseUnityPlugin { public bool resourcesLoaded = true; public WithParameter particleReplacer = new WithParameter(); }
    sealed class ReturningExecute : BaseUnityPlugin { public bool resourcesLoaded = true; public WithReturn particleReplacer = new WithReturn(); }
    sealed class StaticExecute : BaseUnityPlugin { public bool resourcesLoaded = true; public WithStatic particleReplacer = new WithStatic(); }
    sealed class GenericExecute : BaseUnityPlugin { public bool resourcesLoaded = true; public WithGeneric particleReplacer = new WithGeneric(); }
    public sealed class WithParameter { public void Execute(int value) { } }
    public sealed class WithReturn { public int Execute() { return 0; } }
    public sealed class WithStatic { public static void Execute() { } }
    public sealed class WithGeneric { public void Execute<T>() { } }
}

namespace BepInEx
{
    class BaseUnityPlugin { }
    sealed class PluginInfo { public BaseUnityPlugin Instance; }
}

namespace BepInEx.Bootstrap
{
    static class Chainloader
    {
        public static readonly Dictionary<string, PluginInfo> PluginInfos = new Dictionary<string, PluginInfo>();
    }
}

namespace UnityEngine
{
    sealed class GameObject { }
    static class Time { public static int frameCount; }
}

namespace OC2DIYChef
{
    static class DIYChefPlugin
    {
        public static readonly List<string> Messages = new List<string>();
        public static readonly List<string> Warnings = new List<string>();
        public static void Log(string text) { Messages.Add(text); }
        public static void Warn(string text) { Warnings.Add(text); }
    }
}
