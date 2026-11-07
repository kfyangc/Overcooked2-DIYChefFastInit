using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
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
            // sharedMaterials can include another mod's prefab material, even when
            // its name already ends in "(Instance)". Ask Unity for private renderer
            // materials before taking responsibility for destroying them.
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.materials)
                    if (material != null && !ResourceCache.OwnsMaterial(material))
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
