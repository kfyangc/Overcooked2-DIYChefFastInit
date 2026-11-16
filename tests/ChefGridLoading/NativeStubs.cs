using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(); } }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    public struct Quaternion
    {
        public float x, y, z;
        public static Quaternion Euler(float x, float y, float z) { return new Quaternion { x = x, y = y, z = z }; }
    }
    public struct Rect
    {
        public float width, height;
        public Vector2 center;
    }
    public sealed class RectTransform
    {
        public Rect rect = new Rect { width = 26, height = 26 };
        public Quaternion localRotation;
    }
    public sealed class Sprite { public string name; }
    public static class Resources
    {
        public static Sprite[] Sprites = new Sprite[0];
        public static T[] FindObjectsOfTypeAll<T>() { return (T[])(object)Sprites; }
    }
    public static class Time { public static float unscaledTime; }
    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Cos(float value) { return (float)Math.Cos(value); }
        public static float Sin(float value) { return (float)Math.Sin(value); }
    }
}

namespace UnityEngine.UI
{
    using UnityEngine;
    public class Image
    {
        public Sprite sprite;
        public Color color = new Color(1, 1, 1, 1);
        public readonly RectTransform rectTransform = new RectTransform();
        public int NativeBuilds;
        protected virtual void OnPopulateMesh(VertexHelper mesh) { NativeBuilds++; mesh.Clear(); }
        public void Populate(VertexHelper mesh) { OnPopulateMesh(mesh); }
        public void Tick() { GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(this, null); }
    }
    public sealed class VertexHelper
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Color> Colours = new List<Color>();
        public readonly List<int> Indices = new List<int>();
        public int currentVertCount { get { return Positions.Count; } }
        public void Clear() { Positions.Clear(); Colours.Clear(); Indices.Clear(); }
        public void AddVert(Vector3 position, Color color, Vector2 uv) { Positions.Add(position); Colours.Add(color); }
        public void AddTriangle(int a, int b, int c) { Indices.Add(a); Indices.Add(b); Indices.Add(c); }
    }
}
