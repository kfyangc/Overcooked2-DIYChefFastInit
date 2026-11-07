using System;
using System.Collections.Generic;
using System.Reflection;
using OC2DIYChef;
using UnityEngine;

// The renderer adapter models Unity's documented distinction: sharedMaterials
// borrows references, whereas materials instantiates per-renderer materials.
static class ResourceUseRegression
{
    static int assertions;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    static Renderer AddRenderer(GameObject model, Material material)
    {
        var renderer = model.AddComponent<Renderer>();
        renderer.sharedMaterials = new[] { material, null };
        return renderer;
    }

    static void Destroy(GameObject model)
    {
        // Unity may destroy renderers before invoking ResourceUse.OnDestroy.
        model.ClearRenderers();
        typeof(ResourceUse).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(model.GetComponent<ResourceUse>(), null);
    }

    static void Main()
    {
        var sharedPollen = new Material("pollen_mat (Instance)");
        var prefab = new GameObject();
        var prefabRenderer = AddRenderer(prefab, sharedPollen);
        var restaurant = new GameObject();
        var restaurantParticles = AddRenderer(restaurant, sharedPollen);
        var kitchen = new GameObject();
        var kitchenParticles = AddRenderer(kitchen, sharedPollen);
        var restaurantResource = new ResourceCache();
        var kitchenResource = new ResourceCache();

        ResourceUse.Attach(restaurant, restaurantResource);
        var privateRestaurant = restaurantParticles.sharedMaterials[0];
        ResourceUse.Attach(kitchen, kitchenResource);
        var privateKitchen = kitchenParticles.sharedMaterials[0];
        Destroy(restaurant);
        Check(!sharedPollen.Destroyed, "destroying restaurant chef must not destroy ModelTest's shared pollen material");
        Check(!prefabRenderer.sharedMaterials[0].Destroyed, "particle prefab material survives scene transition");
        Check(!kitchenParticles.sharedMaterials[0].Destroyed, "already spawned kitchen particles survive restaurant destruction");
        Check(!ReferenceEquals(privateRestaurant, sharedPollen) && !ReferenceEquals(privateKitchen, sharedPollen),
            "each chef owns a private material rather than borrowing the prefab instance");
        Check(!ReferenceEquals(privateRestaurant, privateKitchen), "different chefs do not share owned materials");
        Check(privateRestaurant.Destroyed && !privateKitchen.Destroyed, "only outgoing chef's private copy is reclaimed");
        var futureKitchen = new GameObject();
        var futureParticles = AddRenderer(futureKitchen, prefabRenderer.sharedMaterials[0]);
        ResourceUse.Attach(futureKitchen, null);
        Check(!futureParticles.sharedMaterials[0].Destroyed, "future spawns still inherit a valid prefab material");
        Destroy(kitchen);
        Destroy(futureKitchen);
        Check(privateKitchen.Destroyed && !sharedPollen.Destroyed, "all chefs can be destroyed while prefab material remains valid");

        var model = new GameObject();
        var source = new Material("chef_material");
        ResourceCache.Owned.Add(source);
        var renderer = AddRenderer(model, source);
        var resource = new ResourceCache();
        Time.realtimeSinceStartup = 10;
        ResourceUse.Attach(model, resource);
        var owned = renderer.sharedMaterials[0];
        ResourceUse.Attach(model, resource);
        Check(resource.References == 1 && resource.LastUsed == 10, "repeated tracking pins a resource only once");
        Check(ReferenceEquals(owned, renderer.sharedMaterials[0]), "repeated capture reuses an already private material");

        var addedByMod = new GameObject();
        model.Children.Add(addedByMod);
        var external = new Material("flower_blue_mat");
        var externalRenderer = AddRenderer(addedByMod, external);
        model.GetComponent<ResourceUse>().CaptureMaterials();
        var ownedExternal = externalRenderer.sharedMaterials[0];
        Check(!ReferenceEquals(ownedExternal, external), "late-added effects get private copies without relying on a name suffix");

        var replacementSource = new Material("replacement (Instance)");
        renderer.sharedMaterials = new[] { replacementSource };
        model.GetComponent<ResourceUse>().CaptureMaterials();
        var ownedReplacement = renderer.sharedMaterials[0];
        var hatResource = new ResourceCache();
        ResourceUse.Attach(model, hatResource);
        Check(resource.References == 1 && hatResource.References == 1, "one model can pin multiple resources independently");
        Time.realtimeSinceStartup = 20;
        Destroy(model);
        Check(owned.Destroyed && ownedReplacement.Destroyed && ownedExternal.Destroyed,
            "material replacements and late effects are reclaimed even after renderers disappear");
        Check(owned.DestroyCalls == 1 && ownedReplacement.DestroyCalls == 1 && ownedExternal.DestroyCalls == 1,
            "repeated captures do not destroy private materials more than once");
        Check(!source.Destroyed && !external.Destroyed && !replacementSource.Destroyed,
            "cache-owned and external source materials are preserved");
        Check(resource.References == 0 && hatResource.References == 0 && resource.LastUsed == 20 && hatResource.LastUsed == 20,
            "destruction releases pins and updates last-used timestamps");
        ResourceUse.Attach(null, null);
        Console.WriteLine("PASS: " + assertions + " material ownership assertions; no Unity/game process used.");
    }
}

namespace OC2DIYChef
{
    sealed class ResourceCache
    {
        internal static readonly HashSet<Material> Owned = new HashSet<Material>();
        internal int References;
        internal float LastUsed;
        internal static bool OwnsMaterial(Material material) { return Owned.Contains(material); }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public int DestroyCalls;
        public static void Destroy(Object value) { value.Destroyed = true; value.DestroyCalls++; }
    }
    public class Material : Object
    {
        public string name;
        public Material(string materialName) { name = materialName; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        { return gameObject.GetComponentsInChildren<T>(includeInactive); }
    }
    public class MonoBehaviour : Component { }
    public sealed class GameObject : Object
    {
        readonly List<Component> components = new List<Component>();
        public readonly List<GameObject> Children = new List<GameObject>();
        public T GetComponent<T>() where T : Component
        {
            foreach (var component in components) if (component is T) return (T)component;
            return null;
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>();
            foreach (var component in components) if (component is T) result.Add((T)component);
            foreach (var child in Children) result.AddRange(child.GetComponentsInChildren<T>(includeInactive));
            return result.ToArray();
        }
        public void ClearRenderers()
        {
            components.RemoveAll(delegate(Component value) { return value is Renderer; });
            foreach (var child in Children) child.ClearRenderers();
        }
    }
    public sealed class Renderer : Component
    {
        Material[] shared = new Material[0];
        bool instanced;
        public Material[] sharedMaterials
        {
            get { return (Material[])shared.Clone(); }
            set { shared = (Material[])value.Clone(); instanced = false; }
        }
        public Material[] materials
        {
            get
            {
                if (!instanced)
                {
                    for (int i = 0; i < shared.Length; i++)
                        if (shared[i] != null) shared[i] = new Material(shared[i].name + " (Instance)");
                    instanced = true;
                }
                return (Material[])shared.Clone();
            }
        }
    }
    public static class Time { public static float realtimeSinceStartup; }
}
