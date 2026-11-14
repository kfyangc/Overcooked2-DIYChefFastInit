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
    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled { get { return enabled && gameObject != null && gameObject.activeSelf; } }
    }
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
    public static int Updates;
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
    protected virtual void Update() { Updates++; }
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
    internal static readonly AvatarDirectoryData Directory = new AvatarDirectoryData();
    internal static readonly MetaGameProgress Progress = new MetaGameProgress();
    internal static T RequireManager<T>() where T : class { return manager as T; }
    internal static AvatarDirectoryData GetAvatarDirectoryData() { return Directory; }
    internal static MetaGameProgress GetMetaGameProgress() { return Progress; }
    internal static GameSession Session;
    internal static GameSession GetGameSession() { return Session; }
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
public sealed class ChefAvatarData { public string HeadName = "Chef_Test"; public DlcData ForDlc; }
public sealed class ChefColourData { }
public sealed class DlcData { public int m_DLCID; }
public sealed class AvatarDirectoryData
{
    public readonly ChefAvatarData[] Avatars = new ChefAvatarData[64];
    public AvatarDirectoryData()
    {
        for (int i = 0; i < Avatars.Length; i++) Avatars[i] = new ChefAvatarData { HeadName = "Chef_" + i };
    }
}
public sealed class MetaGameProgress { public ChefAvatarData[] GetUnlockedAvatars() { return (ChefAvatarData[])GameUtils.Directory.Avatars.Clone(); } }
public sealed class T17FrontendFlow
{
    public static T17FrontendFlow Instance;
    public DlcData AutoOpenFrontendDlcData;
    public int ClearAutoOpenCalls;
    public void AutoOpenChefSelectionMenu(object value) { ClearAutoOpenCalls++; AutoOpenFrontendDlcData = null; }
}
public static class ClientMessenger
{
    public static int AvatarMessages;
    public static uint LastAvatar;
    public static void ChefAvatar(uint avatar, User user) { AvatarMessages++; LastAvatar = avatar; }
}
public sealed class GameSession
{
    public sealed class SelectedChefData
    {
        public ChefAvatarData Character = new ChefAvatarData();
        public ChefColourData Colour = new ChefColourData();
        public SelectedChefData() { }
        public SelectedChefData(ChefAvatarData character, ChefColourData colour) { Character = character; Colour = colour; }
    }
    public sealed class Settings { public string WorldMapScene; }
    public Settings TypeSettings;
}
public sealed class FrontendChefCustomisation : UnityEngine.MonoBehaviour
{
    ChefMeshReplacer m_chef;
    PlayerGameInput m_assignedInput;
    GameSession.SelectedChefData m_chefData;
#pragma warning disable CS0169 // Production native-field adapter initializes these.
    ChefAvatarData[] m_avatarDirectory, m_unlockedAvatars;
    ILogicalButton m_leftButton, m_rightButton;
    int m_chefSelection;
#pragma warning restore CS0169
    public static int ActivateCalls, DeactivateCalls;
    public ChefMeshReplacer Replacer { get { return m_chef; } }
    public PlayerInputLookup.Player m_actualPlayer;
    public bool IsActive() { return m_assignedInput != null; }
    public PlayerGameInput GetAssignedInput() { return m_assignedInput; }
    public GameSession.SelectedChefData GetCurrentSelection() { return m_chefData; }
    public void Activate(PlayerGameInput value, GameSession.SelectedChefData data)
    {
        ActivateCalls++;
        m_assignedInput = value;
        m_chefData = data;
        var flow = T17FrontendFlow.Instance;
        // Mirror the native Activate branch that the old stand-in omitted:
        // a lingering frontend DLC request selects and sends an avatar immediately.
        if (data != null && flow != null && flow.AutoOpenFrontendDlcData != null)
        {
            var avatars = GameUtils.Directory.Avatars;
            int index = Array.FindIndex(avatars, x => x.ForDlc != null && x.ForDlc.m_DLCID == flow.AutoOpenFrontendDlcData.m_DLCID);
            if (index < 0) index = avatars.Length - 1;
            m_chefData.Character = avatars[index];
            ClientMessenger.ChefAvatar((uint)index, ClientUserSystem.m_Users._items[(int)m_actualPlayer]);
        }
        enabled = true;
    }
    public void RebindInput(PlayerGameInput value) { if (enabled) m_assignedInput = value; }
    public void Deactivate()
    {
        DeactivateCalls++;
        m_assignedInput = null;
        m_chefData = null;
        enabled = false;
        if (T17FrontendFlow.Instance != null) T17FrontendFlow.Instance.AutoOpenChefSelectionMenu(null);
    }
    public void SetChefMeshReplacer(ChefMeshReplacer value) { m_chef = value; }
    public void SetChefAnimator(UnityEngine.Animator animator) { }
}
public sealed class UIPlayerMenuBehaviour { public User UserInfo; }
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
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    sealed class HarmonyPatch : Attribute { public HarmonyPatch() { } public HarmonyPatch(Type type, string name) { } }
    [AttributeUsage(AttributeTargets.Method)] sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] sealed class HarmonyPostfix : Attribute { }
    static class AccessTools
    {
        internal static Type Inner(Type type, string name)
        {
            return type.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);
        }
        internal static FieldInfo Field(Type type, string name)
        {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(type.FullName, name);
        }
    }
}
namespace OC2DIYChef
{
    public sealed class LobbyFlowController
    {
        public enum LobbyState { PreSetup, Matchmake, LocalSetup, OnlineSetup, LocalThemeSelection, OnlineThemeSelection, LocalThemeSelected }
        public static LobbyFlowController Instance;
        public readonly UIPlayerRootMenu m_uiPlayerRoot = new UIPlayerRootMenu();
    }
    public sealed class ClientLobbyFlowController
    {
        public static ClientLobbyFlowController Instance;
#pragma warning disable CS0414 // Read through the production context adapter.
        readonly LobbyFlowController.LobbyState m_state;
#pragma warning restore CS0414
        public ClientLobbyFlowController(LobbyFlowController.LobbyState state) { m_state = state; }
    }
    public sealed class UIPlayerRootMenu
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
        internal static ChefGridLobby Attached;
        UIPlayerRootMenu root;
        internal static ChefGridLobby Attach(UIPlayerRootMenu players)
        {
            return Attached = new ChefGridLobby { root = players };
        }
        public readonly LobbyFlowController Flow = new LobbyFlowController();
        public UIPlayerRootMenu Players { get { return root ?? Flow.m_uiPlayerRoot; } }
        internal readonly ChefGridLobbySession Session = new ChefGridLobbySession();
        public int Begins, Ends, Commits;
        internal void BeginInput(FrontendChefCustomisation[] views) { Begins++; Session.Begin(); }
        internal void EndInput() { Ends++; Session.End(UnityEngine.Time.frameCount); }
        internal void Committed() { Commits++; }
    }
    public sealed class ChefGridMenu : UnityEngine.MonoBehaviour
    {
        public static bool Enabled = true;
        public static bool FailOpen, ThrowOpen;
        public bool IsOpen, FailTick;
        public int Ticks;
        public FrontendChefCustomisation[] Views;
        Action cancel;
        BaseMenuBehaviour owner;
        internal static void Show(BaseMenuBehaviour menu, FrontendChefCustomisation[] views, Action close)
        {
            if (ThrowOpen) throw new InvalidOperationException("Simulated native failure");
            var grid = menu.GetComponent<ChefGridMenu>() ?? menu.gameObject.AddComponent<ChefGridMenu>();
            grid.IsOpen = !FailOpen && Array.Exists(views, x => x.IsActive());
            grid.FailTick = false;
            grid.Views = views;
            grid.cancel = close;
            grid.owner = menu;
        }
        public void Tick() { Ticks++; if (FailTick || !Array.Exists(Views, x => x.IsActive())) IsOpen = false; }
        public void CloseGrid() { IsOpen = false; }
        public void Cancel() { cancel(); }
        public void Accept()
        {
            var scoped = owner as ChefGridLobbyMenu;
            if (scoped != null) scoped.Commit();
            else ChefGridWorldMap.Commit((InGameCustomisation)owner);
        }
    }
    static class ChefSelectionMenu
    {
        internal static ChefCustomiser Released;
        internal static void Release(ChefCustomiser value) { Released = value; }
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
    sealed class ChefGridEntry { internal uint OfficialID; internal byte CustomID; }
    static class ChefGridRoster
    {
        internal static readonly List<int> Selected = new List<int>();
        internal static bool Select(ChefGridEntry entry, int player, FrontendChefCustomisation view)
        {
            var user = ClientUserSystem.m_Users._items[player];
            if (!user.IsLocal || !view.IsActive()) return false;
            Selected.Add(player);
            DIYChefCustomisation.userDIYChefID[player] = entry.CustomID;
            user.SelectedChefAvatar = entry.OfficialID;
            if (ClientUserSystem.usersChanged != null) ClientUserSystem.usersChanged();
            return true;
        }
    }
}
