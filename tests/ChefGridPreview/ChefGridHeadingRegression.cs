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
        var wholePrefab = new GameObject("Neutral complete rig");
        var prefabSkeleton = new GameObject("Skeleton"); prefabSkeleton.transform.SetParent(wholePrefab.transform, false);
        var prefabTorso = new GameObject("Jnt_Body"); prefabTorso.transform.SetParent(prefabSkeleton.transform, false);
        var prefabHead = new GameObject("Head"); prefabHead.transform.SetParent(prefabTorso.transform, false);
        var wholeModel = new GameObject("Animated complete rig"); wholeModel.transform.SetParent(actor.transform, false);
        var modelSkeleton = new GameObject("Skeleton"); modelSkeleton.transform.SetParent(wholeModel.transform, false);
        var modelTorso = new GameObject("Jnt_Body"); modelTorso.transform.SetParent(modelSkeleton.transform, false);
        modelTorso.transform.localRotation = Quaternion.Euler(0, 4.9123f, 0);
        var modelHead = new GameObject("Head"); modelHead.transform.SetParent(modelTorso.transform, false);
        modelHead.transform.localRotation = Quaternion.Euler(0, 14.1488f, 0);
        heading.Apply(wholeModel, wholePrefab);
        Check(Fixed(modelTorso.transform, Quaternion.identity),
            "the whole chef heading follows the torso rather than over-rotating the actor to cancel its head gaze");
        Check(Fixed(modelHead.transform, Quaternion.identity),
            "the head must follow the fixed torso heading instead of retaining an independent selecting-screen turn");
        float[] torsoYaw = { 4.9123f, 2.7550f, -16.3278f, -2.4484f };
        float[] headYaw = { 19.0611f, 13.5f, -44f, -12f };
        for (int player = 0; player < 4; player++)
        {
            modelTorso.transform.localRotation = Quaternion.Euler(0, torsoYaw[player], 0);
            var gaze = Quaternion.Euler(0, headYaw[player] - torsoYaw[player], 0);
            modelHead.transform.localRotation = gaze;
            heading.Apply(wholeModel, wholePrefab);
            Check(Fixed(modelTorso.transform, Quaternion.identity) && Fixed(modelHead.transform, Quaternion.identity),
                "P1-P4 selecting animations align each head to its torso despite their different head turns");
            var correctedHead = modelHead.transform.localRotation;
            heading.Apply(wholeModel, wholePrefab);
            Check(Fixed(modelHead.transform, Quaternion.identity) && SameRotation(modelHead.transform.localRotation, correctedHead),
                "repeated rendering does not accumulate head correction when the animator has not changed its pose");
        }
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
        // Replay the original native body and head quaternion chains together.
        prefabTorso.transform.localRotation = rest;
        var nativeBodyPose = new Quaternion { x = -0.7055433f, y = -0.6907647f, z = 0.1364273f, w = -0.080252f };
        modelTorso.transform.localRotation = nativeBodyPose;
        var nativeGaze = Quaternion.Inverse(nativeBodyPose) * nativePose;
        modelHead.transform.localRotation = nativeGaze;
        float pitchBefore = (modelHead.transform.rotation * Quaternion.Inverse(rest) * Vector3.forward).y;
        float rollBefore = (modelHead.transform.rotation * Quaternion.Inverse(rest) * new Vector3(0, 1, 0)).y;
        var blink = new GameObject("Eyes2_Blinks"); blink.transform.SetParent(modelHead.transform, false);
        blink.transform.localScale = new Vector3(0, 0, 0);
        blink.transform.localRotation = Quaternion.Euler(3, 7, 9);
        heading.Apply(wholeModel, wholePrefab);
        Check(Fixed(modelTorso.transform, rest) && Math.Abs(Yaw(actor.transform.rotation) - (-26.548508f)) < 0.001f,
            "the native selection fixture compensates only the 4.9123-degree body turn instead of its 19-degree head gaze");
        Check(Fixed(modelHead.transform, rest), "the actual native head quaternion follows the torso heading");
        Check(Math.Abs((modelHead.transform.rotation * Quaternion.Inverse(rest) * Vector3.forward).y - pitchBefore) < 0.00001f &&
            Math.Abs((modelHead.transform.rotation * Quaternion.Inverse(rest) * new Vector3(0, 1, 0)).y - rollBefore) < 0.00001f,
            "native head pitch and roll survive the horizontal correction even with rotated bone axes");
        Check(blink.transform.localScale.x == 0 && blink.transform.localScale.y == 0 && blink.transform.localScale.z == 0 &&
            SameRotation(blink.transform.localRotation, Quaternion.Euler(3, 7, 9)),
            "head alignment leaves the selecting animation's eye visibility and child-bone rotations intact");
        Check(SameRotation(prefabTorso.transform.localRotation, rest) && SameRotation(prefabHead.transform.localRotation, Quaternion.identity),
            "only the isolated preview is adjusted, never the shared neutral prefab");
        // Head has its own neutral axes and an animated neck between it and
        // the torso. Replay the animator writing a new pose before each render.
        var neutralHeadAxes = Quaternion.Euler(12, 30, -7);
        prefabHead.transform.localRotation = neutralHeadAxes;
        var neck = new GameObject("Neck"); neck.transform.SetParent(modelTorso.transform, false);
        neck.transform.localRotation = Quaternion.Euler(2, 8, -4);
        modelHead.transform.SetParent(neck.transform, false);
        heading = new ChefGridHeading(actor.transform);
        foreach (float yaw in new[] { -179f, -44, 0, 19, 179 })
        {
            modelTorso.transform.localRotation = nativeBodyPose;
            var pose = Quaternion.Euler(15, yaw, -10) * rest * neutralHeadAxes;
            modelHead.transform.localRotation = Quaternion.Inverse(neck.transform.rotation) * pose;
            heading.Apply(wholeModel, wholePrefab);
            Check(Fixed(modelTorso.transform, rest) && Fixed(modelHead.transform, rest * neutralHeadAxes),
                "animated necks and different head rest axes align to the torso across wrapped yaw angles");
            Check(SameRotation(neck.transform.localRotation, Quaternion.Euler(2, 8, -4)),
                "head alignment does not overwrite the intermediate neck animation");
        }
        var custom = new GameObject("Chef without the standard rig"); custom.transform.SetParent(actor.transform, false);
        heading.Apply(custom, null);
        Check(Math.Abs(Yaw(actor.transform.rotation) + 21.6362f) < 0.001f, "unrecognized rigs retain the fixed outer-angle fallback");
        return checks;
    }

    static bool SameRotation(Quaternion a, Quaternion b)
    {
        float dot = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        return Math.Abs(Math.Abs(dot) - 1) < 0.00001f;
    }
}
