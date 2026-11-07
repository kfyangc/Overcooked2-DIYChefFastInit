using BepInEx;
using BepInEx.Bootstrap;
using System;
using System.Reflection;
using UnityEngine;

namespace OC2DIYChef
{
    // Optional runtime bridge: do not reference or load OC2ModelTest.dll ourselves.
    internal static class ModelTestCompatibility
    {
        const string PluginIdFragment = "modeltest";
        const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static BaseUnityPlugin plugin;
        static FieldInfo resourcesLoaded, particleReplacer;
        static MethodInfo execute;
        static bool initialized, disabled, pending;
        static int requestedFrame;

        // Start runs after the chainloader has finished creating plugins. Looking up
        // ModelTest in Awake would depend on which plugin the chainloader loads first.
        internal static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                string pluginId = null;
                foreach (var entry in Chainloader.PluginInfos)
                    if (entry.Key.IndexOf(PluginIdFragment, StringComparison.OrdinalIgnoreCase) >= 0 && entry.Value.Instance != null)
                    {
                        plugin = entry.Value.Instance;
                        pluginId = entry.Key;
                        break;
                    }
                if (plugin == null)
                {
                    pending = false;
                    return;
                }
                var type = plugin.GetType();
                resourcesLoaded = type.GetField("resourcesLoaded", InstanceMembers);
                if (resourcesLoaded == null || resourcesLoaded.FieldType != typeof(bool))
                    throw new MissingFieldException(type.FullName, "bool resourcesLoaded");
                particleReplacer = type.GetField("particleReplacer", InstanceMembers);
                if (particleReplacer == null)
                    throw new MissingFieldException(type.FullName, "particleReplacer");
                execute = particleReplacer.FieldType.GetMethod("Execute", InstanceMembers, null, Type.EmptyTypes, null);
                if (execute == null || execute.ReturnType != typeof(void) || execute.ContainsGenericParameters)
                    throw new MissingMethodException(particleReplacer.FieldType.FullName, "void Execute()");
                DIYChefPlugin.Log("ModelTest particle compatibility enabled: " + pluginId);
            }
            catch (Exception ex) { Disable(ex); }
        }

        internal static void ModelReplaced(GameObject previous, GameObject current)
        {
            if (disabled || (initialized && plugin == null) || current == null || ReferenceEquals(previous, current)) return;
            if (!pending) requestedFrame = Time.frameCount;
            pending = true;
        }

        internal static void LateUpdate()
        {
            if (!initialized || disabled || !pending || Time.frameCount <= requestedFrame) return;
            if (plugin == null)
            {
                pending = false;
                return;
            }
            try
            {
                // Keep the request while ModelTest loads its bundles; a fixed delay
                // would miss slow cold starts. No scene scan happens until it is ready.
                if (!(bool)resourcesLoaded.GetValue(plugin)) return;
                var particles = particleReplacer.GetValue(plugin);
                if (particles == null)
                    throw new InvalidOperationException("particleReplacer is null after resourcesLoaded=true");
                // Clear first so any new replacement during Execute remains pending.
                pending = false;
                execute.Invoke(particles, null);
            }
            catch (Exception ex) { Disable(ex); }
        }

        static void Disable(Exception error)
        {
            if (disabled) return;
            disabled = true;
            pending = false;
            plugin = null;
            if (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            DIYChefPlugin.Warn("ModelTest particle compatibility disabled; chef loading continues. " +
                error.GetType().Name + ": " + error.Message);
        }

        internal static void Shutdown()
        {
            plugin = null;
            resourcesLoaded = particleReplacer = null;
            execute = null;
            initialized = disabled = pending = false;
        }
    }
}
