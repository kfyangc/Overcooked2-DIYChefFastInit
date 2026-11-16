using UnityEngine;

namespace OC2DIYChef
{
    // The torso owns the fixed heading; align only the head's horizontal gaze.
    internal sealed class ChefGridHeading
    {
        readonly Transform actor;
        GameObject boundModel, boundPrefab;
        Transform facing, head;
        Quaternion neutral, headNeutral;
        internal ChefGridHeading(Transform actor) { this.actor = actor; }

        internal void Apply(GameObject model, GameObject prefab)
        {
            if (boundModel != model || boundPrefab != prefab)
            {
                boundModel = model; boundPrefab = prefab;
                facing = head = null;
                var actualSkeleton = model == null ? null : model.transform.Find("Skeleton");
                var neutralSkeleton = prefab == null ? null : prefab.transform.Find("Skeleton");
                if (actualSkeleton != null && neutralSkeleton != null)
                    foreach (string bone in new[] { "Jnt_Body", "Head" })
                    {
                        var actual = actualSkeleton.FindChildRecursive(bone);
                        var rest = neutralSkeleton.FindChildRecursive(bone);
                        if (actual == null || rest == null) continue;
                        facing = actual;
                        neutral = Quaternion.Inverse(prefab.transform.rotation) * rest.rotation;
                        break;
                    }
                if (facing != null && facing.name == "Jnt_Body")
                {
                    var actualHead = actualSkeleton.FindChildRecursive("Head");
                    var restHead = neutralSkeleton.FindChildRecursive("Head");
                    if (actualHead != null && restHead != null)
                    {
                        head = actualHead;
                        headNeutral = Quaternion.Inverse(prefab.transform.rotation) * restHead.rotation;
                    }
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
            if (head != null)
            {
                var forward = (head.rotation * Quaternion.Inverse(headNeutral)) * Vector3.forward;
                if (forward.x * forward.x + forward.z * forward.z > 0.000001f)
                {
                    float headYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                    var correction = Quaternion.Euler(0, ChefGridPreviewScene.FixedYaw - headYaw, 0);
                    // Apply around scene up, not the bone's local Y (the native
                    // rig uses rotated bone axes). Keep pitch/roll and child eye
                    // animation intact, and leave the torso/actor unchanged.
                    head.localRotation = Quaternion.Inverse(head.parent.rotation) * correction * head.rotation;
                }
            }
        }
    }
}
