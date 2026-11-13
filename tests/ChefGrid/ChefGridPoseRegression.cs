using System;
using System.Collections.Generic;
using OC2DIYChef;
using UnityEngine;

static class ChefGridPoseRegression
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    static GameObject Model(bool animated)
    {
        var root = new GameObject("Chef");
        root.Add(new Animator());
        var headBone = root.Child("Skeleton").Child("Head");
        headBone.Child("Eyes").transform.localScale = animated ? 0.01f : 1;
        headBone.Child("Eyes2_Blinks").transform.localScale = animated ? 0.8f : 1;
        var head = root.Child("Mesh").Child("Chef_Test");
        head.activeSelf = false;
        head.Add(new SkinnedMeshRenderer { sharedMesh = new Mesh { blendShapeCount = 1 },
            ShapeWeight = animated ? 100 : 0 });
        var eye = head.Child("Eyes");
        eye.activeSelf = false; // An imported part inherited an inactive template head flag.
        eye.transform.localPosition = animated ? 99 : 2;
        eye.transform.localRotation = animated ? 99 : 3;
        eye.transform.localScale = animated ? 0 : 1;
        eye.Add(new SkinnedMeshRenderer { enabled = false, sharedMesh = new Mesh() });
        head.Child("Eyes2_Blinks").Add(new SkinnedMeshRenderer { sharedMesh = new Mesh() });
        return root;
    }

    internal static int Run()
    {
        var prefab = Model(false);
        prefab.activeSelf = false;
        var model = Model(true);
        model.transform.localPosition = 10000;
        // Runtime children can precede prefab children and must survive path matching.
        model.Child("Runtime hat");
        model.transform.Children.Reverse();
        var avatar = new ChefAvatarData { HeadName = "Chef_Test", FrontendModelPrefab = prefab };
        var replacer = new ChefMeshReplacer { ChefModel = model };
        ChefGridPose.Prepare(replacer, avatar);
        Check(!model.GetComponent<Animator>().enabled, "sampling cannot change a portrait after its static pose is restored");
        Check(model.transform.localPosition == 10000 && model.activeSelf, "the isolated studio's world placement remains intact");
        var skeleton = model.transform.Find("Skeleton/Head");
        Check(skeleton.Find("Eyes").localScale == 1, "the open-eye bone is restored from a partial blink");
        Check(skeleton.Find("Eyes2_Blinks").localScale == 1,
            "eyelid geometry is filtered without collapsing a skinning bone");
        var head = model.transform.Find("Mesh/Chef_Test");
        Check(head.gameObject.activeSelf && head.GetComponent<SkinnedMeshRenderer>().enabled,
            "the selected head is visible even when its serialized flag is inactive");
        Check(head.GetComponent<SkinnedMeshRenderer>().ShapeWeight == 0, "animated blend shapes reset to the prefab's static values");
        var eyes = head.Find("Eyes");
        Check(eyes.gameObject.activeSelf && eyes.GetComponent<SkinnedMeshRenderer>().enabled,
            "an independent open-eye object is activated as well as its renderer");
        Check(eyes.localPosition == 2 && eyes.localRotation == 3 && eyes.localScale == 1,
            "custom eye renderer transforms are restored together with skeleton transforms");
        Check(!head.Find("Eyes2_Blinks").GetComponent<SkinnedMeshRenderer>().enabled,
            "independent closed-eye renderers stay hidden");
        Check(model.transform.Find("Runtime hat") != null, "runtime attachments absent from the prefab are retained");
        Check(!prefab.activeSelf && !prefab.transform.Find("Mesh/Chef_Test/Eyes").gameObject.activeSelf,
            "preparing a snapshot does not modify the shared source prefab");
        return checks;
    }
}

// Hierarchy/state stand-ins exercise the production pose traversal, without calling Unity.
namespace UnityEngine
{
    class Component
    {
        internal GameObject gameObject;
        internal string name { get { return gameObject.name; } }
        internal T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    sealed class Transform : Component
    {
        internal readonly List<Transform> Children = new List<Transform>();
        internal float localPosition, localRotation, localScale = 1;
        internal int childCount { get { return Children.Count; } }
        internal Transform GetChild(int i) { return Children[i]; }
        internal Transform Find(string path)
        {
            int slash = path.IndexOf('/');
            string first = slash < 0 ? path : path.Substring(0, slash);
            var match = Children.Find(x => x.name == first);
            return slash < 0 || match == null ? match : match.Find(path.Substring(slash + 1));
        }
    }
    sealed class GameObject
    {
        internal readonly string name;
        internal readonly Transform transform;
        internal bool activeSelf = true;
        readonly List<Component> components = new List<Component>();
        internal GameObject(string value) { name = value; transform = Add(new Transform()); }
        internal GameObject Child(string value)
        {
            var child = new GameObject(value);
            transform.Children.Add(child.transform);
            return child;
        }
        internal T Add<T>(T value) where T : Component { value.gameObject = this; components.Add(value); return value; }
        internal T GetComponent<T>() where T : Component { return components.Find(x => x is T) as T; }
        internal T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>();
            foreach (var component in components) if (component is T) result.Add((T)component);
            foreach (var child in transform.Children)
                if (includeInactive || child.gameObject.activeSelf) result.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive));
            return result.ToArray();
        }
        internal void SetActive(bool value) { activeSelf = value; }
    }
    sealed class Animator : Component { internal bool enabled = true; }
    sealed class Mesh { internal int blendShapeCount; }
    sealed class SkinnedMeshRenderer : Component
    {
        internal bool enabled = true;
        internal Mesh sharedMesh;
        internal float ShapeWeight;
        internal float GetBlendShapeWeight(int index) { return ShapeWeight; }
        internal void SetBlendShapeWeight(int index, float value) { ShapeWeight = value; }
    }
}

sealed class ChefMeshReplacer
{
    internal GameObject ChefModel;
    void SetHeadVisibility(string headName) { ChefModel.transform.Find("Mesh/" + headName).gameObject.SetActive(true); }
}
