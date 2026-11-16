using System;
using OC2DIYChef;
using UnityEngine;

static class ChefGridHeadingRegression
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static Transform Rig(GameObject root, Quaternion rest, string bone = "Head")
    {
        var skeleton = new GameObject("Skeleton"); skeleton.transform.SetParent(root.transform, false);
        var body = new GameObject(bone); body.transform.SetParent(skeleton.transform, false);
        body.transform.localRotation = rest;
        return body.transform;
    }
    static float Yaw(Quaternion value)
    {
        var forward = value * Vector3.forward;
        return (float)(Math.Atan2(forward.x, forward.z) * 180 / Math.PI);
    }
    static bool Fixed(Transform body, Quaternion rest)
    { return Math.Abs(Yaw(body.rotation * Quaternion.Inverse(rest)) + 21.6362f) < 0.001f; }

    internal static int Run()
    {
        var actor = new GameObject("Actor");
        var model = new GameObject("Animated chef"); model.transform.SetParent(actor.transform, false);
        var prefab = new GameObject("Neutral prefab");
        var rest = Quaternion.identity;
        Rig(prefab, rest);
        var body = Rig(model, Quaternion.Euler(0, 21.6362f, 0));
        var heading = new ChefGridHeading(actor.transform);
        heading.Apply(model, prefab);
        Check(Fixed(body, rest), "the final face heading must remain -21.64 when animation cancels the outer angle to zero");
        Check(body.localRotation.Equals(Quaternion.Euler(0, 21.6362f, 0)), "heading correction leaves native bone animation intact");
        heading.Apply(model, prefab);
        Check(Fixed(body, rest), "repeated rendering cannot accumulate angle compensation");
        foreach (float yaw in new[] { -179f, -90f, 0, 45f, 179f })
        {
            body.localRotation = Quaternion.Euler(8, yaw, 3);
            heading.Apply(model, prefab);
            Check(Fixed(body, rest), "native animation turns and angle wrapping retain the requested final heading");
        }
        model.transform.localRotation = Quaternion.Euler(0, 40, 0);
        heading.Apply(model, prefab);
        Check(Fixed(body, rest), "animated model-root rotation is included in heading compensation");
        Check(model.transform.localScale.x == 1, "heading correction never changes model scale");

        var replacement = new GameObject("Other chef"); replacement.transform.SetParent(actor.transform, false);
        var otherPrefab = new GameObject("Other neutral prefab");
        rest = Quaternion.Euler(20, -35, 12); Rig(otherPrefab, rest);
        body = Rig(replacement, Quaternion.Euler(-5, 62, 4) * rest);
        heading.Apply(replacement, otherPrefab);
        Check(Fixed(body, rest), "switching chefs uses the new rig's neutral bone orientation");
        var neutralModel = new GameObject("Neutral nested rig");
        var neutralHead = Rig(neutralModel, Quaternion.identity);
        var nestedModel = new GameObject("Animated nested rig"); nestedModel.transform.SetParent(actor.transform, false);
        var skeleton = new GameObject("Skeleton"); skeleton.transform.SetParent(nestedModel.transform, false);
        var torso = new GameObject("Jnt_Body"); torso.transform.SetParent(skeleton.transform, false);
        torso.transform.localRotation = Quaternion.Euler(0, 5, 0);
        var head = new GameObject("Head"); head.transform.SetParent(torso.transform, false);
        head.transform.localRotation = Quaternion.Euler(0, 16.6362f, 0);
        heading.Apply(nestedModel, neutralModel);
        Check(Fixed(head.transform, neutralHead.rotation), "combined torso and head turns are compensated using the final face direction");
        Check(torso.transform.localRotation.Equals(Quaternion.Euler(0, 5, 0)) &&
            head.transform.localRotation.Equals(Quaternion.Euler(0, 16.6362f, 0)),
            "native torso and head bone tracks are preserved instead of being overwritten");
        // Decoded from the native FE_VanSelectIdle_01 clip at t=0. The full
        // animated Head chain adds 19.0611° relative to its neutral prefab pose.
        var nativePrefab = new GameObject("Native neutral rig");
        rest = new Quaternion { x = 0.70710683f, y = -0.70710683f, z = 0, w = 0 };
        Rig(nativePrefab, rest);
        var nativeModel = new GameObject("Native selecting rig"); nativeModel.transform.SetParent(actor.transform, false);
        var nativePose = new Quaternion { x = -0.71238036f, y = -0.64319354f, z = 0.2708799f, w = -0.07375908f };
        var nativeHead = Rig(nativeModel, nativePose);
        actor.transform.localRotation = Quaternion.Euler(0, -21.6362f, 0);
        Check(Math.Abs(Yaw(nativeHead.rotation * Quaternion.Inverse(rest)) - (-2.575052f)) < 0.001f,
            "the recorded native selection pose reproduces the almost-frontal angle reported by the user");
        heading.Apply(nativeModel, nativePrefab);
        Check(Fixed(nativeHead, rest) && nativeHead.localRotation.Equals(nativePose),
            "the real native selection pose is displayed at -21.64 without editing its bone rotation");
        var bodyPrefab = new GameObject("Neutral body-only rig"); Rig(bodyPrefab, Quaternion.identity, "Jnt_Body");
        var bodyModel = new GameObject("Animated body-only rig"); bodyModel.transform.SetParent(actor.transform, false);
        var bodyOnly = Rig(bodyModel, Quaternion.Euler(0, 25, 0), "Jnt_Body");
        heading.Apply(bodyModel, bodyPrefab);
        Check(Fixed(bodyOnly, Quaternion.identity), "rigs without Head use the standard torso heading as a fallback");
        var custom = new GameObject("Chef without the standard rig"); custom.transform.SetParent(actor.transform, false);
        heading.Apply(custom, null);
        Check(Math.Abs(Yaw(actor.transform.rotation) + 21.6362f) < 0.001f, "unrecognized rigs retain the fixed outer-angle fallback");
        return checks;
    }
}
