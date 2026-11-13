using System;
using System.Collections.Generic;
using System.Reflection;
using OC2DIYChef;
using Team17.Online;

// Narrow stand-ins for native APIs, exercising the production lobby menu and
// selection snapshot code. This does not simulate Unity rendering or networking.
namespace UnityEngine
{
    public class Object { }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public class Animator : Component { }
    public class Transform : Component { public void SetParent(Transform parent, bool worldPositionStays) { } }
    public class GameObject : Object
    {
        readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();
        public readonly Transform transform;
        public bool activeSelf = true;
        public GameObject(string name) { transform = new Transform { gameObject = this }; }
        public void SetActive(bool active) { activeSelf = active; }
        public T AddComponent<T>() where T : Component, new()
        {
            var value = new T { gameObject = this };
            components[typeof(T)] = value;
            return value;
        }
        public T GetComponent<T>() where T : Component
        {
            Component result;
            return components.TryGetValue(typeof(T), out result) ? (T)result : null;
        }
    }
    public static class Time { public static int frameCount; }
}
public class BaseMenuBehaviour : UnityEngine.MonoBehaviour { }
public class InGameMenuBehaviour : BaseMenuBehaviour
{
    public virtual bool Show(GamepadUser gamer, BaseMenuBehaviour parent, UnityEngine.GameObject invoker, bool hideInvoker)
    {
        if (gameObject.activeSelf) return false;
        gameObject.SetActive(true);
        return true;
    }
    public virtual bool Hide(bool restoreInvokerState = true, bool isTabSwitch = false)
    {
        bool active = gameObject.activeSelf;
        gameObject.SetActive(false);
        return active;
    }
    public virtual void Close() { Hide(); }
    protected virtual void Update() { }
    protected virtual void OnDestroy() { }
}
public sealed class GamepadUser { }
public enum PadSide { Both, Left, Right }
public static class ControlPadInput { public enum PadNum { One, Two, Three, Four } }
public class AmbiControlsMappingData { }
public sealed class PlayerGameInput
{
    public ControlPadInput.PadNum Pad;
    public PadSide Side;
    public PlayerGameInput(ControlPadInput.PadNum pad, PadSide side, AmbiControlsMappingData mapping) { Pad = pad; Side = side; }
}
public sealed class GameInputConfig { }
public sealed class PlayerManager
{
    public AmbiControlsMappingData UnsidedAmbiMapping = new AmbiControlsMappingData();
    public AmbiControlsMappingData SidedAmbiMapping = new AmbiControlsMappingData();
}
static class GameUtils
{
    static readonly PlayerManager manager = new PlayerManager();
    internal static T RequireManager<T>() where T : class { return manager as T; }
}
static class UserSystemUtils { internal static void BuildGameInputConfig() { PlayerInputLookup.Config = new GameInputConfig(); } }
public static class PlayerInputLookup
{
    public enum Player { One, Two, Three, Four }
    public static GameInputConfig Config;
    public static GameInputConfig GetInputConfig() { return Config; }
    public static void SetInputConfig(GameInputConfig config) { Config = config; }
}
public sealed class ChefMeshReplacer : UnityEngine.MonoBehaviour
{
    public UnityEngine.GameObject ChefModel = new UnityEngine.GameObject("Chef model");
}
public sealed class ChefAvatarData { public string HeadName = "Chef_Test"; }
public static class GameSession { public sealed class SelectedChefData { public ChefAvatarData Character = new ChefAvatarData(); } }
public sealed class FrontendChefCustomisation : UnityEngine.MonoBehaviour
{
    ChefMeshReplacer m_chef;
    PlayerGameInput input;
    GameSession.SelectedChefData selection;
    public ChefMeshReplacer Replacer { get { return m_chef; } }
    public PlayerInputLookup.Player m_actualPlayer;
    public bool IsActive() { return input != null; }
    public PlayerGameInput GetAssignedInput() { return input; }
    public GameSession.SelectedChefData GetCurrentSelection() { return selection; }
    public void Activate(PlayerGameInput value, GameSession.SelectedChefData data) { input = value; selection = data; enabled = true; }
    public void RebindInput(PlayerGameInput value) { input = value; }
    public void Deactivate() { input = null; selection = null; enabled = false; }
    public void SetChefMeshReplacer(ChefMeshReplacer value) { m_chef = value; }
    public void SetChefAnimator(UnityEngine.Animator animator) { }
}
public sealed class ChefCustomiser
{
    FrontendChefCustomisation[] m_chefCustomisations;
    readonly Dictionary<long, uint> avatars = new Dictionary<long, uint>();
    public void SetChefs(FrontendChefCustomisation[] views) { m_chefCustomisations = views; }
    static long Key(User user) { return ((long)user.Engagement << 32) | (uint)user.Split; }
    public void CacheCurrentAvatars()
    {
        avatars.Clear();
        foreach (var user in ClientUserSystem.m_Users._items) if (user.IsLocal) avatars[Key(user)] = user.SelectedChefAvatar;
        ChefSelectionMenu.Capture(this); // Installed CacheCurrentAvatars postfix.
    }
    public void RevertAvatars()
    {
        bool restored = ChefSelectionMenu.RestoreBeforeAvatars(this); // Installed RevertAvatars prefix.
        foreach (var user in ClientUserSystem.m_Users._items)
        {
            uint value;
            if (user.IsLocal && avatars.TryGetValue(Key(user), out value)) user.SelectedChefAvatar = value;
        }
        if (restored) ChefSelectionMenu.SynchronizeRestoredChoices(); // Installed postfix.
    }
}
public sealed class FrontendChefMenu
{
#pragma warning disable CS0414 // Read by the production reflection adapter.
    readonly ChefCustomiser m_customiser = null;
#pragma warning restore CS0414
}
namespace Team17.Online
{
    public sealed class User
    {
        public bool IsLocal;
        public int Engagement, Split;
        public PadSide PadSide;
        public uint SelectedChefAvatar;
        public GameSession.SelectedChefData SelectedChefData = new GameSession.SelectedChefData();
        public static User Create(bool local, int engagement, PadSide side, uint avatar)
        {
            return new User { IsLocal = local, Engagement = engagement, PadSide = side, Split = (int)side, SelectedChefAvatar = avatar };
        }
    }
    public sealed class UserList
    {
        public User[] _items;
        public int Count { get { return _items.Length; } }
        public UserList(params User[] users) { _items = users; }
    }
    public static class ClientUserSystem
    {
        public static UserList m_Users;
        public static Action usersChanged;
    }
}
namespace HarmonyLib
{
    static class AccessTools
    {
        internal static FieldInfo Field(Type type, string name)
        {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(type.FullName, name);
        }
    }
}
namespace OC2DIYChef
{
    public sealed class LobbyFlowController { public readonly UIPlayerRoot m_uiPlayerRoot = new UIPlayerRoot(); }
    public sealed class UIPlayerRoot
    {
        public readonly Dictionary<User, UnityEngine.GameObject> Players = new Dictionary<User, UnityEngine.GameObject>();
        public void Add(User user)
        {
            var card = new UnityEngine.GameObject("Player card");
            card.AddComponent<ChefMeshReplacer>();
            Players[user] = card;
        }
        public UnityEngine.GameObject GetUIPlayerForUser(User user)
        {
            UnityEngine.GameObject result;
            return Players.TryGetValue(user, out result) ? result : null;
        }
    }
    public sealed class ChefGridLobby
    {
        public readonly LobbyFlowController Flow = new LobbyFlowController();
        internal readonly ChefGridLobbySession Session = new ChefGridLobbySession();
        public int Begins, Ends;
        internal void BeginInput(FrontendChefCustomisation[] views) { Begins++; Session.Begin(); }
        internal void EndInput() { Ends++; Session.End(UnityEngine.Time.frameCount); }
    }
    public sealed class ChefGridMenu : UnityEngine.MonoBehaviour
    {
        public static bool FailOpen, ThrowOpen;
        public bool IsOpen, FailTick;
        public FrontendChefCustomisation[] Views;
        Action cancel;
        ChefGridLobbyMenu owner;
        internal static void Show(ChefGridLobbyMenu menu, FrontendChefCustomisation[] views, Action close)
        {
            if (ThrowOpen) throw new InvalidOperationException("Simulated native failure");
            var grid = menu.GetComponent<ChefGridMenu>() ?? menu.gameObject.AddComponent<ChefGridMenu>();
            grid.IsOpen = !FailOpen && Array.Exists(views, x => x.IsActive());
            grid.FailTick = false;
            grid.Views = views;
            grid.cancel = close;
            grid.owner = menu;
        }
        public void Tick() { if (FailTick) IsOpen = false; }
        public void CloseGrid() { IsOpen = false; }
        public void Cancel() { cancel(); }
        public void Accept() { owner.Hide(); }
    }
    static class DIYChefCustomisation
    {
        public static bool Enabled = true;
        public static byte[] userDIYChefID;
        public static object localClient = new object();
        public static void SendClientDIYChefMessage(byte id, User user) { }
    }
    static class ChefLoading
    {
        public static readonly List<ChefMeshReplacer> Cancelled = new List<ChefMeshReplacer>();
        public static void Cancel(ChefMeshReplacer replacer) { Cancelled.Add(replacer); }
    }
}
