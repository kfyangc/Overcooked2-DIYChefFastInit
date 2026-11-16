using UnityEngine;

namespace OC2DIYChef
{
    // Keep the visible face heading fixed while the native selector animates.
    internal sealed class ChefGridHeading
    {
        readonly Transform actor;
        GameObject boundModel, boundPrefab;
        Transform facing;
        Quaternion neutral;
        internal ChefGridHeading(Transform actor) { this.actor = actor; }

        internal void Apply(GameObject model, GameObject prefab)
        {
            if (boundModel != model || boundPrefab != prefab)
            {
                boundModel = model; boundPrefab = prefab;
                facing = null;
                var actualSkeleton = model == null ? null : model.transform.Find("Skeleton");
                var neutralSkeleton = prefab == null ? null : prefab.transform.Find("Skeleton");
                if (actualSkeleton != null && neutralSkeleton != null)
                    foreach (string bone in new[] { "Head", "Jnt_Body" })
                    {
                        var actual = actualSkeleton.FindChildRecursive(bone);
                        var rest = neutralSkeleton.FindChildRecursive(bone);
                        if (actual == null || rest == null) continue;
                        facing = actual;
                        neutral = Quaternion.Inverse(prefab.transform.rotation) * rest.rotation;
                        break;
                    }
            }
            float animatedYaw = 0;
            if (facing != null)
            {
                // Remove our previous correction before measuring this frame's
                // animated heading, so it cannot feed back or accumulate.
                var relative = Quaternion.Inverse(actor.rotation) * facing.rotation;
                var forward = (relative * Quaternion.Inverse(neutral)) * Vector3.forward;
                if (forward.x * forward.x + forward.z * forward.z > 0.000001f)
                    animatedYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            }
            actor.localRotation = Quaternion.Euler(0, ChefGridPreviewScene.FixedYaw - animatedYaw, 0);
        }
    }
}
