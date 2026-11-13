using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OC2DIYChef;
using UnityEngine;
using UnityEngine.UI;

// Minimal native surface with a nested tree, queued focus and stepped hide
// coroutines. All extension hooks execute the production menu implementation.
namespace UnityEngine
{
    public class Object
    {
        public static GameObject Instantiate(GameObject source, Transform parent)
        {
            var copy = new GameObject(source.name);
            copy.SetActive(source.activeSelf);
            copy.transform.SetParent(parent, false);
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
                    clone.OnButtonPointerEnter = button.OnButtonPointerEnter;
                    clone.OnButtonPointerExit = button.OnButtonPointerExit;
                    clone.OnButtonMove = button.OnButtonMove;
                    clone.navigation = button.navigation;
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
        public int Stops;
        public void StopCoroutine(Coroutine routine) { routine.Stopped = true; Stops++; }
    }
    public sealed class Coroutine
    {
        readonly IEnumerator routine;
        public bool Stopped;
        public Coroutine(IEnumerator value) { routine = value; }
        public bool Step() { return !Stopped && routine.MoveNext(); }
    }
    public class Transform : Component, IEnumerable<Transform>
    {
        public Transform parent;
        public Vector3 localPosition;
        public readonly List<Transform> Children = new List<Transform>();
        public void SetParent(Transform value, bool worldPositionStays = false)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = value;
            if (parent != null) parent.Children.Add(this);
        }
        public void SetAsLastSibling()
        {
            if (parent == null) return;
            parent.Children.Remove(this); parent.Children.Add(this);
        }
        public int GetSiblingIndex() { return parent == null ? 0 : parent.Children.IndexOf(this); }
        public bool IsChildOf(Transform other)
        {
            for (var node = this; node != null; node = node.parent) if (node == other) return true;
            return false;
        }
        public IEnumerator<Transform> GetEnumerator() { return Children.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
    public sealed class RectTransform : Transform
    {
        public Vector2 sizeDelta = new Vector2(200, 50);
        public Vector2 anchorMin = new Vector2(0.5f, 0.5f), anchorMax = new Vector2(0.5f, 0.5f), pivot;
        public Vector2 anchoredPosition;
        public Rect rect
        {
            get
            {
                var container = parent as RectTransform;
                var bounds = container == null ? new Rect() : container.rect;
                return new Rect { width = sizeDelta.x + bounds.width * (anchorMax.x - anchorMin.x),
                    height = sizeDelta.y + bounds.height * (anchorMax.y - anchorMin.y) };
            }
        }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(); } }
        public static Vector2 one { get { return new Vector2(1, 1); } }
    }
    public struct Vector3
    {
        public float x, y, z;
        public static Vector3 zero { get { return new Vector3(); } }
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
    }
    public struct Rect { public float width, height; public Vector2 size { get { return new Vector2(width, height); } } }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Abs(float a) { return Math.Abs(a); }
        public static int RoundToInt(float value) { return (int)Math.Round(value); }
    }
    public static class Time { public static int frameCount; }
    public sealed class GameObject : Object
    {
        public string name;
        public bool activeSelf = true;
        public bool activeInHierarchy { get { return activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); } }
        public readonly Transform transform;
        internal readonly List<Component> Components = new List<Component>();
        public GameObject(string name, params Type[] types)
        {
            this.name = name;
            transform = new RectTransform { gameObject = this };
            foreach (var type in types)
            {
                if (typeof(Transform).IsAssignableFrom(type)) continue;
                var component = (Component)Activator.CreateInstance(type);
                component.gameObject = this; Components.Add(component);
            }
        }
        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : Component, new()
        {
            var value = new T { gameObject = this }; Components.Add(value); return value;
        }
        public T GetComponent<T>() where T : Component
        {
            if (transform is T) return (T)(Component)transform;
            foreach (var item in Components) if (item is T) return (T)item;
            return null;
        }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component
        {
            var result = new List<T>();
            if (inactive || activeInHierarchy)
            {
                if (transform is T) result.Add((T)(Component)transform);
                foreach (var item in Components) if (item is T) result.Add((T)item);
            }
            foreach (var child in transform.Children) result.AddRange(child.gameObject.GetComponentsInChildren<T>(inactive));
            return result.ToArray();
        }
    }
}
namespace UnityEngine.UI
{
    public struct Navigation
    {
        public enum Mode { None, Explicit }
        public Mode mode;
        public Selectable selectOnUp, selectOnDown, selectOnLeft, selectOnRight;
    }
    public class Selectable : MonoBehaviour
    {
        public bool interactable = true;
        public Navigation navigation;
        public bool IsInteractable() { return interactable; }
        public virtual void Select() { }
    }
    public class Text : MonoBehaviour { public string text; }
    public class Button : Selectable
    {
        public sealed class ButtonClickedEvent
        {
            readonly List<Action> actions = new List<Action>();
            public void AddListener(Action action) { actions.Add(action); }
            public void Invoke() { foreach (var action in actions) action(); }
        }
        public ButtonClickedEvent onClick = new ButtonClickedEvent();
        public void Click() { if (isActiveAndEnabled && interactable) onClick.Invoke(); }
    }
    public sealed class LayoutElement : Component { public bool ignoreLayout; public float preferredWidth, preferredHeight; }
    public class LayoutGroup : Component { }
    public sealed class GridLayoutGroup : LayoutGroup { public Vector2 cellSize; }
    public static class LayoutRebuilder
    {
        public static int Rebuilds;
        public static void ForceRebuildLayoutImmediate(RectTransform rect)
        {
            Rebuilds++;
            var grid = rect.GetComponent<GridLayoutGroup>();
            var nativeGrid = rect.GetComponent<T17GridLayoutGroup>();
            foreach (Transform child in rect)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                var bounds = (RectTransform)child;
                if (grid != null) bounds.sizeDelta = grid.cellSize;
                else if (nativeGrid != null) bounds.sizeDelta = nativeGrid.cellSize;
                else if (rect.GetComponent<LayoutGroup>() != null)
                {
                    var element = child.GetComponent<LayoutElement>();
                    if (element != null && !element.ignoreLayout)
                        bounds.sizeDelta = new Vector2(element.preferredWidth, element.preferredHeight);
                }
            }
        }
    }
}
public sealed class T17GridLayoutGroup : LayoutGroup { public Vector2 cellSize; }
public sealed class T17Button : Button
{
    public Action<T17Button> OnButtonSelect, OnButtonDeselect, OnButtonPointerEnter, OnButtonPointerExit;
    public Delegate OnButtonMove;
    public bool m_bShowTooltip, m_bLocalizeTooltip;
    public string m_TooltipTag;
    public T17EventSystem Domain;
    public void SetEventSystem(T17EventSystem domain) { Domain = domain; }
    public override void Select()
    {
        if (Domain != null && !Domain.IsDisabled()) Domain.SetSelectedGameObject(gameObject);
    }
    public void Enter() { if (OnButtonPointerEnter != null) OnButtonPointerEnter(this); }
    public void Exit() { if (OnButtonPointerExit != null) OnButtonPointerExit(this); }
    public void Focus() { if (OnButtonSelect != null) OnButtonSelect(this); }
    public void Blur() { if (OnButtonDeselect != null) OnButtonDeselect(this); }
}
public sealed class T17Text : Text
{
    public string m_NonLocalizedKeyboardText;
    public void SetNonLocalizedText(string text) { this.text = text; }
}
public sealed class T17EventSystem
{
    public GameObject Current, Pending;
    public bool Disabled;
    public GameObject currentSelectedGameObject { get { return Current; } }
    public GameObject GetPendingSelectedGameObject() { return Pending; }
    public void ForceDeselectSelectionObject() { Current = null; }
    public void SetSelectedGameObject(GameObject value) { Pending = value; }
    public bool IsDisabled() { return Disabled; }
}
public enum SupportedLanguages { Chinese, English }
public static class Localization
{
    public static SupportedLanguages Language = SupportedLanguages.Chinese;
    public static SupportedLanguages GetLanguage() { return Language; }
}
public sealed class User { public bool IsLocal = true; public int Engagement, Split; public uint SelectedChefAvatar = 7; }
public sealed class UIPlayerRootMenu
{
    public readonly List<UIPlayerMenuBehaviour> UIPlayers = new List<UIPlayerMenuBehaviour>();
    public T17EventSystem CachedEventSystem = new T17EventSystem();
    public void NotifyUsers(Action nativeRefresh)
    {
        object state = MenuHooks.BeforeUsers(this);
        nativeRefresh();
        MenuHooks.Call("AfterUsers", this, state);
    }
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
    User user = new User();
    public UIPlayerRootMenu m_rootMenu;
    public User UserInfo
    {
        get { return user; }
        set { user = value; MenuHooks.Call("Changed", this); }
    }
    public T17Button MenuButton;
    public int NavigationCalls, ProfileClicks, HideCalls, NameClicks, NativePointerEnters;
    public Action<UIPlayerMenuBehaviour> OtherStructure;
    public bool OtherRunsAfter;
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
        MenuButton.transform.SetParent(transform);
        MenuButton.SetEventSystem(root.CachedEventSystem);
        MenuButton.onClick.AddListener(() => { NameClicks++; ToggleMenu(); });
        MenuButton.OnButtonPointerEnter = _ => NativePointerEnters++;
        var profile = new GameObject("Profile");
        profile.transform.SetParent(m_menu.transform);
        var button = profile.AddComponent<T17Button>();
        button.onClick.AddListener(() => ProfileClicks++);
        profile.AddComponent<T17Text>().text = "PROFILE";
        m_menuOptions.Add(new MenuOption { m_button = button, m_type = UIPlayerMenuOptions.Profile, m_platformsActiveOn = int.MaxValue });
        m_menu.SetActive(false);
    }
    void UpdateNavigation()
    {
        NavigationCalls++;
        var nav = MenuButton.navigation;
        foreach (Transform child in m_menu.transform)
        {
            var button = child.GetComponent<T17Button>();
            if (button != null && button.isActiveAndEnabled) nav.selectOnUp = button;
        }
        MenuButton.navigation = nav;
        MenuHooks.Call("NavigationChanged", this);
    }
    public bool CanBeInteractedWith() { return UserInfo != null && UserInfo.IsLocal; }
    public bool IsSelected()
    {
        bool selected = m_rootMenu.CachedEventSystem.Current == null;
        var args = new object[] { this, selected };
        MenuHooks.Invoke("IsSelected", args);
        return (bool)args[1];
    }
    IEnumerator NativeHide(bool force)
    {
        if (!force)
        {
            yield return null;
            while (IsSelected()) yield return null;
        }
        m_menu.SetActive(false);
        UpdateNavigation();
    }
    public void HideMenu(bool _force = false)
    {
        HideCalls++;
        IEnumerator routine = NativeHide(_force);
        var args = new object[] { this, _force, routine };
        MenuHooks.Invoke("HidingRoutine", args);
        m_hideMenu = new Coroutine((IEnumerator)args[2]);
        m_hideMenu.Step();
        MenuHooks.Call("Hiding", this, _force);
    }
    public void ToggleMenu()
    {
        if (!(bool)MenuHooks.Call("Toggle", this)) return;
        if (m_menu.activeSelf) HideMenu();
        else ShowMenu();
    }
    public void Structure()
    {
        foreach (var option in m_menuOptions) option.m_button.gameObject.SetActive(option.m_platformsActiveOn != 0);
        UpdateNavigation();
        if (OtherStructure != null && !OtherRunsAfter) OtherStructure(this);
        MenuHooks.Call("Structure", this);
        if (OtherStructure != null && OtherRunsAfter) OtherStructure(this);
    }
    public void ShowMenu()
    {
        if (!(bool)MenuHooks.Call("ShowMenu", this)) return;
        m_menu.SetActive(true);
        Structure();
    }
}
static class MenuHooks
{
    internal static object Invoke(string name, object[] args)
    {
        try { return typeof(ChefGridPlayerMenu).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    internal static object Call(string name, params object[] args) { return Invoke(name, args); }
    internal static object BeforeUsers(UIPlayerRootMenu root)
    {
        var args = new object[] { root, null };
        Invoke("BeforeUsers", args);
        return args[1];
    }
}
namespace OC2DIYChef
{
    static class ChefGridPlayerContext
    {
        internal static UIPlayerRootMenu CurrentPlayers() { return ChefGridLobby.Root; }
        internal static UIPlayerRootMenu PausePlayers(InGamePauseMenu menu) { return menu.Players; }
    }
    static class ChefGridLobby
    {
        internal static UIPlayerRootMenu Root;
        internal static bool Visible = true, Blocking;
        internal static int Opens, AvatarMessages;
        internal static UIPlayerMenuBehaviour OpenedFor;
        internal static bool CanShowFor(User user) { return Visible && user != null && user.IsLocal; }
        internal static bool CanOpenFor(User user) { return CanShowFor(user) && !Blocking; }
        internal static bool BlocksMenus(UIPlayerRootMenu root) { return Blocking && root == Root; }
        internal static void OpenFor(UIPlayerMenuBehaviour card)
        {
            if (!CanOpenFor(card.UserInfo)) return;
            Opens++; OpenedFor = card;
        }
        internal static void ExternalChange(User user) { AvatarMessages++; user.SelectedChefAvatar++; }
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    sealed class HarmonyPatch : Attribute { public HarmonyPatch() { } public HarmonyPatch(Type type, string name) { } }
    [AttributeUsage(AttributeTargets.Method)] sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] sealed class HarmonyPriority : Attribute { public HarmonyPriority(int value) { } }
    static class Priority { internal const int Last = 0; }
    static class AccessTools
    {
        internal static FieldInfo Field(Type type, string name) { return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); }
        internal static MethodInfo Method(Type type, string name) { return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); }
    }
}
