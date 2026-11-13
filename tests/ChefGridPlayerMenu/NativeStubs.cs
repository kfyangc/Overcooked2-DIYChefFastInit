using System;
using System.Collections.Generic;
using System.Reflection;
using OC2DIYChef;
using UnityEngine;
using UnityEngine.UI;

// Minimal menu tree and native event surface. Exercises the actual menu adapter,
// without loading Unity, a game scene, or another mod.
namespace UnityEngine
{
    public class Object
    {
        public static GameObject Instantiate(GameObject source, Transform parent)
        {
            var copy = new GameObject(source.name);
            copy.transform.SetParent(parent);
            copy.transform.localPosition = source.transform.localPosition;
            ((RectTransform)copy.transform).sizeDelta = ((RectTransform)source.transform).sizeDelta;
            foreach (var component in source.Components)
            {
                var button = component as T17Button;
                if (button != null)
                {
                    var clone = copy.AddComponent<T17Button>();
                    clone.onClick = button.onClick;
                    clone.OnButtonSelect = button.OnButtonSelect;
                    clone.OnButtonDeselect = button.OnButtonDeselect;
                    continue;
                }
                var text = component as Text;
                if (text != null)
                {
                    var clone = text is T17Text ? copy.AddComponent<T17Text>() : copy.AddComponent<Text>();
                    clone.text = text.text;
                }
            }
            foreach (var child in source.transform.Children) Instantiate(child.gameObject, copy.transform);
            return copy;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component { return gameObject.GetComponentsInChildren<T>(inactive); }
    }
    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled { get { return enabled && gameObject.activeInHierarchy; } }
        public void StopCoroutine(Coroutine coroutine) { coroutine.Stopped = true; }
    }
    public sealed class Coroutine { public bool Stopped; }
    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> Children = new List<Transform>();
        public Vector3 localPosition;
        public void SetParent(Transform value) { parent = value; value.Children.Add(this); }
    }
    public sealed class RectTransform : Transform
    {
        public Vector2 sizeDelta = new Vector2(200, 50);
        public Rect rect { get { return new Rect { height = sizeDelta.y }; } }
    }
    public struct Rect { public float height; }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
    }
    public static class Mathf { public static float Max(float a, float b) { return Math.Max(a, b); } public static float Abs(float a) { return Math.Abs(a); } }
    public sealed class GameObject : Object
    {
        public string name;
        public bool activeSelf = true;
        public bool activeInHierarchy { get { return activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); } }
        public readonly Transform transform;
        internal readonly List<Component> Components = new List<Component>();
        public GameObject(string name) { this.name = name; transform = new RectTransform { gameObject = this }; }
        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : Component, new()
        {
            var value = new T { gameObject = this }; Components.Add(value); return value;
        }
        public T GetComponent<T>() where T : Component
        {
            foreach (var item in Components) if (item is T) return (T)item;
            return null;
        }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component
        {
            var result = new List<T>();
            if (inactive || activeInHierarchy)
                foreach (var item in Components) if (item is T) result.Add((T)item);
            foreach (var child in transform.Children) result.AddRange(child.gameObject.GetComponentsInChildren<T>(inactive));
            return result.ToArray();
        }
    }
}
namespace UnityEngine.UI
{
    public class Text : MonoBehaviour { public string text; }
    public class Button : MonoBehaviour
    {
        public sealed class ButtonClickedEvent
        {
            readonly List<Action> actions = new List<Action>();
            public void AddListener(Action action) { actions.Add(action); }
            public void Invoke() { foreach (var action in actions) action(); }
        }
        public bool interactable = true;
        public ButtonClickedEvent onClick = new ButtonClickedEvent();
        public void Click() { if (isActiveAndEnabled && interactable) onClick.Invoke(); }
    }
    public sealed class LayoutElement : Component { public bool ignoreLayout; }
    public sealed class LayoutGroup : Component { }
    public static class LayoutRebuilder { public static int Rebuilds; public static void ForceRebuildLayoutImmediate(RectTransform rect) { Rebuilds++; } }
}
public sealed class T17Button : Button
{
    public Action<T17Button> OnButtonSelect, OnButtonDeselect, OnButtonPointerEnter, OnButtonPointerExit;
    public Delegate OnButtonMove;
    public bool m_bLocalizeTooltip;
    public string m_TooltipTag;
    public T17EventSystem Domain;
    public void SetEventSystem(T17EventSystem domain) { Domain = domain; }
}
public sealed class T17Text : Text
{
    public string m_NonLocalizedKeyboardText;
    public void SetNonLocalizedText(string text) { this.text = text; }
}
public sealed class T17EventSystem
{
    public GameObject Current, Pending;
    public void ForceDeselectSelectionObject() { Current = null; }
    public void SetSelectedGameObject(GameObject value) { Pending = value; }
}
public enum SupportedLanguages { Chinese, English }
public static class Localization
{
    public static SupportedLanguages Language = SupportedLanguages.Chinese;
    public static SupportedLanguages GetLanguage() { return Language; }
}
public sealed class User { public bool IsLocal = true; }
public sealed class UIPlayerRootMenu
{
    public readonly List<UIPlayerMenuBehaviour> UIPlayers = new List<UIPlayerMenuBehaviour>();
    public T17EventSystem CachedEventSystem = new T17EventSystem();
}
public sealed class InGamePauseMenu { public UIPlayerRootMenu Players; }
public sealed class ClientLobbyFlowController { }
public sealed class LobbyFlowController { public static LobbyFlowController Instance; public UIPlayerRootMenu m_uiPlayerRoot; }
public sealed class UIPlayerMenuBehaviour : MonoBehaviour
{
    public enum UIPlayerMenuOptions { Profile }
    public struct MenuOption { public T17Button m_button; public UIPlayerMenuOptions m_type; public int m_platformsActiveOn; }
    GameObject m_menu;
    readonly List<MenuOption> m_menuOptions = new List<MenuOption>();
    Coroutine m_hideMenu;
    public UIPlayerRootMenu m_rootMenu;
    public User UserInfo = new User();
    public T17Button MenuButton;
    public int NavigationCalls, Selections, ProfileClicks, HideCalls;
    public GameObject Menu { get { return m_menu; } }
    public IList<MenuOption> Options { get { return m_menuOptions; } }
    public Coroutine HideRoutine { get { return m_hideMenu; } }
    public void Initialize(UIPlayerRootMenu root)
    {
        m_rootMenu = root;
        root.UIPlayers.Add(this);
        m_menu = new GameObject("Menu");
        m_menu.transform.SetParent(transform);
        MenuButton = new GameObject("Name").AddComponent<T17Button>();
        var profile = new GameObject("Profile");
        profile.transform.SetParent(m_menu.transform);
        var button = profile.AddComponent<T17Button>();
        button.onClick.AddListener(() => ProfileClicks++);
        profile.AddComponent<T17Text>().text = "PROFILE";
        m_menuOptions.Add(new MenuOption { m_button = button, m_type = UIPlayerMenuOptions.Profile, m_platformsActiveOn = int.MaxValue });
        m_menu.SetActive(false);
    }
    void OnSelected(T17Button button) { Selections++; }
    void OnDeselected(T17Button button) { }
    void UpdateNavigation() { NavigationCalls++; }
    public bool CanBeInteractedWith() { return UserInfo != null && UserInfo.IsLocal; }
    public void HideMenu(bool force) { HideCalls++; m_hideMenu = new Coroutine(); } // Native hide yields a frame.
    public void ShowMenu()
    {
        if (!(bool)MenuHooks.Call("ShowMenu", this)) return;
        m_menu.SetActive(true);
        MenuHooks.Call("BeforeStructure", this);
        foreach (var option in m_menuOptions) option.m_button.gameObject.SetActive(option.m_platformsActiveOn != 0);
        UpdateNavigation();
        MenuHooks.Call("AfterStructure", this);
    }
}
static class MenuHooks
{
    internal static object Call(string name, params object[] args)
    {
        return typeof(ChefGridPlayerMenu).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    }
}
namespace OC2DIYChef
{
    static class ChefGridPlayerContext { internal static UIPlayerRootMenu PausePlayers(InGamePauseMenu menu) { return menu.Players; } }
    static class ChefGridLobby
    {
        internal static UIPlayerRootMenu Root;
        internal static bool Visible = true, Blocking;
        internal static int Opens;
        internal static bool CanShowFor(User user) { return Visible && user != null && user.IsLocal; }
        internal static bool CanOpenFor(User user) { return CanShowFor(user) && !Blocking; }
        internal static bool BlocksMenus(UIPlayerRootMenu root) { return Blocking && root == Root; }
        internal static void OpenFor(UIPlayerMenuBehaviour card) { if (CanOpenFor(card.UserInfo)) Opens++; }
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    sealed class HarmonyPatch : Attribute { public HarmonyPatch() { } public HarmonyPatch(Type type, string name) { } }
    [AttributeUsage(AttributeTargets.Method)] sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] sealed class HarmonyPostfix : Attribute { }
    static class AccessTools
    {
        internal static FieldInfo Field(Type type, string name) { return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); }
        internal static MethodInfo Method(Type type, string name) { return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); }
    }
}
