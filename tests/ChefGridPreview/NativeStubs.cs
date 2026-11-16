using System;
using System.Collections.Generic;
using System.Reflection;

namespace HarmonyLib
{
    static class AccessTools
    {
        internal static FieldInfo Field(Type type, string name)
        { return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public int DestroyCalls;
        public HideFlags hideFlags;
        public static void Destroy(Object value) { if (value != null) value.DestroyCalls++; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>() where T : Component { return gameObject.GetComponentsInChildren<T>(false); }
    }
    public class MonoBehaviour : Component { }
    public sealed class GameObject : Object
    {
        public static readonly List<GameObject> Created = new List<GameObject>();
        readonly List<Component> components = new List<Component>();
        public readonly Transform transform = new Transform();
        public readonly string name;
        public int layer;
        public bool activeSelf = true;
        public bool activeInHierarchy { get { return activeSelf; } }
        public GameObject(string name) { this.name = name; transform.name = name; Created.Add(this); }
        public void SetActive(bool active) { activeSelf = active; }
        public T GetComponent<T>() where T : Component
        { foreach (var item in components) if (item is T) return (T)item; return null; }
        public T AddComponent<T>() where T : Component, new()
        { var item = new T { gameObject = this }; components.Add(item); return item; }
        public T[] GetComponentsInChildren<T>(bool inactive = false) where T : Component
        { return components.FindAll(item => item is T).ConvertAll(item => (T)item).ToArray(); }
    }
    public sealed class Transform
    {
        public string name;
        readonly List<Transform> children = new List<Transform>();
        public Transform parent;
        public Vector3 position, localPosition;
        public Vector3 localScale = new Vector3(1, 1, 1);
        public Quaternion localRotation = Quaternion.identity;
        public Quaternion rotation { get { return parent == null ? localRotation : parent.rotation * localRotation; } }
        public void SetParent(Transform value, bool worldPositionStays)
        {
            if (parent != null) parent.children.Remove(this);
            parent = value;
            if (value != null) value.children.Add(this);
        }
        public Transform Find(string name) { return children.Find(child => child.name == name); }
        public Transform FindChildRecursive(string name)
        {
            foreach (var child in children)
            {
                if (child.name == name) return child;
                var result = child.FindChildRecursive(name);
                if (result != null) return result;
            }
            return null;
        }
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity { get { return new Quaternion { w = 1 }; } }
        public static Quaternion Euler(float x, float y, float z)
        {
            double half = Math.PI / 360;
            return new Quaternion { y = (float)Math.Sin(y * half), w = (float)Math.Cos(y * half) } *
                new Quaternion { x = (float)Math.Sin(x * half), w = (float)Math.Cos(x * half) } *
                new Quaternion { z = (float)Math.Sin(z * half), w = (float)Math.Cos(z * half) };
        }
        public static Quaternion Inverse(Quaternion value)
        {
            float norm = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return new Quaternion { x = -value.x / norm, y = -value.y / norm, z = -value.z / norm, w = value.w / norm };
        }
        public static Quaternion operator *(Quaternion a, Quaternion b)
        {
            return new Quaternion {
                x = a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                y = a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                z = a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
                w = a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z };
        }
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var p = q * new Quaternion { x = v.x, y = v.y, z = v.z } * Inverse(q);
            return new Vector3(p.x, p.y, p.z);
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
    }
    public static class Mathf
    {
        public const float Rad2Deg = (float)(180 / Math.PI);
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
    }
    public enum CameraClearFlags { SolidColor }
    public sealed class Camera : Component
    {
        public bool enabled, orthographic, useOcclusionCulling;
        public float nearClipPlane, farClipPlane;
        public int cullingMask;
        public CameraClearFlags clearFlags;
        public Color backgroundColor;
        public RenderTexture targetTexture;
        public Rendering.CommandBuffer Commands;
        public void AddCommandBuffer(Rendering.CameraEvent when, Rendering.CommandBuffer commands) { Commands = commands; }
        public void RemoveCommandBuffer(Rendering.CameraEvent when, Rendering.CommandBuffer commands) { if (Commands == commands) Commands = null; }
    }
    public enum HideFlags { HideAndDontSave }
    public sealed class Shader : Object
    {
        public static bool Available = true;
        public bool isSupported = true;
        public static Shader Find(string name) { return Available ? new Shader() : null; }
    }
    public sealed class Material : Object
    {
        public static Material Last;
        public int Mask;
        public Material(Shader shader) { Last = this; }
        public void SetInt(string name, int value) { Mask = value; }
        public void SetColor(string name, Color value) { }
        public void SetTexture(string name, Texture2D value) { }
    }
    public sealed class Texture2D : Object { public static readonly Texture2D whiteTexture = new Texture2D(); }
    public class Renderer : Component { public bool enabled = true; public Material[] sharedMaterials = new Material[2]; }
    public sealed class ParticleSystemRenderer : Renderer { }
    public enum RenderTextureFormat { ARGB32, RGB111110Float }
    public sealed class RenderTexture : Object
    {
        public static bool FailCreate;
        public static RenderTexture Last;
        public readonly int width;
        public readonly RenderTextureFormat format;
        public int Releases;
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format)
        { this.width = width; this.format = format; Last = this; }
        public void Create() { if (FailCreate) throw new InvalidOperationException("GPU allocation failed"); }
        public void Release() { Releases++; }
    }
    public enum LightType { Point }
    public enum LightShadows { None }
    public sealed class Light : Component
    {
        public LightType type;
        public LightShadows shadows;
        public float range, intensity;
    }
    public sealed class AudioSource : Component { public bool enabled = true; }
    public sealed class Animator : Component
    {
        public bool HasAnimationSet = true;
        public int AnimationValue;
        public bool HasSelectingTrigger = true;
        public int LastTrigger, TriggerCalls;
        public static int StringToHash(string value) { return value.GetHashCode(); }
        public bool HasParameter(int hash)
        { return hash == StringToHash("TrigChooseChef") && HasSelectingTrigger; }
        public void SetTrigger(int hash)
        {
            if (!HasParameter(hash)) throw new InvalidOperationException("Unsupported animation trigger");
            LastTrigger = hash; TriggerCalls++;
        }
    }
}

namespace UnityEngine.Rendering
{
    public enum CameraEvent { AfterEverything }
    public enum ColorWriteMask { Alpha = 1 }
    public sealed class CommandBuffer
    {
        public string name;
        public int Draws, Releases;
        public readonly List<UnityEngine.Renderer> Renderers = new List<UnityEngine.Renderer>();
        public void Clear() { Draws = 0; Renderers.Clear(); }
        public void DrawRenderer(UnityEngine.Renderer renderer, UnityEngine.Material material, int submesh, int pass)
        { Draws++; Renderers.Add(renderer); }
        public void Release() { Releases++; }
    }
}

public sealed class FrontendChefCustomisation : UnityEngine.MonoBehaviour
{
    public ChefMeshReplacer m_chef;
}
public sealed class ChefMeshReplacer : UnityEngine.MonoBehaviour
{
    public enum ChefModelType { FrontEnd }
    public UnityEngine.GameObject m_chefTemplatePrefab;
    public ChefModelType m_modelType;
}
public sealed class HatMeshVisibility : UnityEngine.MonoBehaviour
{
    public object m_meshes, m_stateFlags;
}
public sealed class FrontendChef : UnityEngine.MonoBehaviour
{
    public enum ShaderMode { eUI }
    public enum AnimationSet { First, Second, Third, Fourth }
    public UnityEngine.Animator m_ChefAnimator;
    public bool m_bHasAnimationSet;
    public object m_StandardSkinShader, m_StandardClothesShader, m_UISkinShader, m_UIClothesShader;
    public ShaderMode Mode;
    public UnityEngine.Color Ambient;
    public UnityEngine.GameObject ChefModel;
    public void SetShaderMode(ShaderMode mode) { Mode = mode; }
    public void SetUIChefAmbientLighting(UnityEngine.Color ambient) { Ambient = ambient; }
    // Native SetChefData caches the animator but only checks this parameter inside
    // its FrontendChefCustomisation branch. Isolated preview actors lack that component.
    public void AttachModel(UnityEngine.GameObject model)
    { ChefModel = model; m_ChefAnimator = model.GetComponent<UnityEngine.Animator>(); }
    public void SetAnimationSet(AnimationSet value)
    {
        // Mirrors the native cache guard in SetCorrectAnimations, rather than
        // automatically repairing the missing parameter observation in the adapter.
        if (m_ChefAnimator == null && ChefModel != null)
        {
            m_ChefAnimator = ChefModel.GetComponent<UnityEngine.Animator>();
            m_bHasAnimationSet = m_ChefAnimator != null && m_ChefAnimator.HasAnimationSet;
        }
        if (m_ChefAnimator != null && m_bHasAnimationSet) m_ChefAnimator.AnimationValue = (int)value + 1;
    }
}
public sealed class AnimatorAudioComponent : UnityEngine.Component { public bool enabled = true; }
public sealed class AnimatorRumbleComponent : UnityEngine.Component { public bool enabled = true; }

namespace OC2DIYChef
{
    static class HatData { internal const int hatLayer = 8; }
    static class ChefLoading
    {
        internal static readonly List<ChefMeshReplacer> Cancelled = new List<ChefMeshReplacer>();
        internal static void Cancel(ChefMeshReplacer replacer) { Cancelled.Add(replacer); }
    }
}
