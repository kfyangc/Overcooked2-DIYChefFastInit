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
        public GameObject(string name) { this.name = name; Created.Add(this); }
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
        public Transform parent;
        public Vector3 position, localPosition;
        public void SetParent(Transform value, bool worldPositionStays) { parent = value; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
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
    public object m_StandardSkinShader, m_StandardClothesShader, m_UISkinShader, m_UIClothesShader;
    public ShaderMode Mode;
    public UnityEngine.Color Ambient;
    public UnityEngine.GameObject ChefModel;
    public void SetShaderMode(ShaderMode mode) { Mode = mode; }
    public void SetUIChefAmbientLighting(UnityEngine.Color ambient) { Ambient = ambient; }
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
