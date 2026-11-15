using System;
using System.Collections.Generic;
using System.Reflection;

namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public T Value; }
    public sealed class ConfigFile
    {
        readonly Dictionary<string, object> values = new Dictionary<string, object>();
        public ConfigEntry<T> Bind<T>(string section, string key, T value, string description)
        {
            string identity = section + "/" + key;
            if (!values.ContainsKey(identity)) values[identity] = new ConfigEntry<T> { Value = value };
            return (ConfigEntry<T>)values[identity];
        }
    }
}
namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { }
    public sealed class GameObject
    {
        readonly List<Component> components = new List<Component>();
        public bool activeInHierarchy = true;
        public T GetComponent<T>() where T : Component
        { foreach (var part in components) if (part is T) return (T)part; return null; }
        public T AddComponent<T>() where T : Component, new()
        { var part = new T { gameObject = this }; components.Add(part); return part; }
    }
}
namespace HarmonyLib
{
    static class AccessTools
    {
        public static FieldInfo Field(Type type, string name) { return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
    }
}
namespace BitStream
{
    public sealed class BitStreamWriter
    {
        public readonly List<bool> Bits = new List<bool>();
        public void Write(bool value) { Bits.Add(value); }
        public void Write(byte value, int count) { for (int i = count - 1; i >= 0; i--) Bits.Add((value & (1 << i)) != 0); }
    }
    public sealed class BitStreamReader
    {
        readonly List<bool> bits;
        int index;
        public BitStreamReader(BitStreamWriter writer) { bits = writer.Bits; }
        public bool ReadBit() { return bits[index++]; }
        public byte ReadByte(int count) { byte value = 0; while (count-- > 0) value = (byte)((value << 1) | (ReadBit() ? 1 : 0)); return value; }
    }
}
public interface Serialisable
{
    void Serialise(BitStream.BitStreamWriter writer);
    bool Deserialise(BitStream.BitStreamReader reader);
}
public class ChefAvatarData { public string HeadName = "Chef_Fox"; }
public static class GameSession { public sealed class SelectedChefData { public ChefAvatarData Character = new ChefAvatarData(); } }
public sealed class ChefMeshReplacer : UnityEngine.MonoBehaviour
{
    public enum ChefModelType { FrontEnd }
    public UnityEngine.GameObject ChefModel = new UnityEngine.GameObject();
    public GameSession.SelectedChefData Data = new GameSession.SelectedChefData();
    public GameSession.SelectedChefData GetChefData() { return Data; }
}
public sealed class HatMeshVisibility : UnityEngine.MonoBehaviour
{
    public enum VisState { None, Fancy, Festive, Baseball }
    public VisState m_initialVisState = VisState.Festive;
    public int Setups;
    public void Setup(VisState state) { Setups++; }
}
public sealed class FrontendChef : UnityEngine.MonoBehaviour
{
    public HatMeshVisibility.VisState m_HatState;
    public void SetChefHat(HatMeshVisibility.VisState state) { OC2DIYChef.HatData.SetChefHat(gameObject, ref state); }
}
namespace Team17.Online
{
    public enum EngagementSlot { One, Two, Three, Four }
    public enum TeamID { Count }
    public sealed class User
    {
        public enum SplitStatus { Whole, SplitPadMain, SplitPadGuest }
        public bool IsLocal;
        public byte Machine;
        public EngagementSlot Engagement;
        public SplitStatus Split;
    }
    public sealed class UserList
    {
        public User[] _items = new User[0];
        public int Count { get { return _items.Length; } }
    }
    public static class ClientUserSystem { public static UserList m_Users = new UserList(); public static Action usersChanged; }
    public static class ServerUserSystem { public static UserList m_Users = new UserList(); }
    public static class UserSystemUtils
    {
        public static User FindUser(UserList users, object unused, byte machine, EngagementSlot slot, TeamID team, User.SplitStatus split)
        { foreach (var user in users._items) if (user.Machine == machine && user.Engagement == slot && user.Split == split) return user; return null; }
    }
}
namespace Team17.Online.Multiplayer.Messaging
{
    public enum MessageType { }
    public sealed class ChefAvatarMessage : Serialisable
    {
        public byte m_Machine;
        public Team17.Online.EngagementSlot m_EngagementSlot;
        public Team17.Online.User.SplitStatus m_Split;
        public void Initialise(uint avatar, byte machine, Team17.Online.EngagementSlot slot, Team17.Online.User.SplitStatus split)
        { m_Machine = machine; m_EngagementSlot = slot; m_Split = split; }
        public void Serialise(BitStream.BitStreamWriter writer)
        { writer.Write(m_Machine, 8); writer.Write((byte)m_EngagementSlot, 8); writer.Write((byte)m_Split, 8); }
        public bool Deserialise(BitStream.BitStreamReader reader)
        { m_Machine = reader.ReadByte(8); m_EngagementSlot = (Team17.Online.EngagementSlot)reader.ReadByte(8); m_Split = (Team17.Online.User.SplitStatus)reader.ReadByte(8); return true; }
    }
}
namespace OC2DIYChef
{
    public sealed class ResourceCache
    {
        public bool Failed(ChefMeshReplacer.ChefModelType type) { return false; }
    }
    public sealed class HatData
    {
        public string HatName;
        public UnityEngine.GameObject prefab;
        public ResourceCache Resources = new ResourceCache();
        public static int Applied;
        public static string Last;
        public static void SetChefHat(UnityEngine.GameObject actor, ref HatMeshVisibility.VisState state)
        { Applied++; Last = PlayerHats.Resolve(actor, actor.GetComponent<ChefMeshReplacer>().Data.Character.HeadName); }
    }
    static class ChefLoading { public static int Requests; public static void RequestHat(HatData hat) { Requests++; } }
    public sealed class Client
    {
        public PlayerHatMessage Last;
        public void SendMessageToServer(Team17.Online.Multiplayer.Messaging.MessageType type, PlayerHatMessage message, bool reliable) { Last = message; }
    }
    public sealed class Server
    {
        public PlayerHatMessage Last;
        public void BroadcastMessageToAll(Team17.Online.Multiplayer.Messaging.MessageType type, PlayerHatMessage message, bool reliable) { Last = message; }
    }
    public static class DIYChefCustomisation
    {
        public static KeyValuePair<string, string>[] preferredChefs = { new KeyValuePair<string, string>("Chef_Fox", "Fancy") };
        public static readonly List<HatData> diyHats = new List<HatData>();
        public static Client localClient;
        public static Server localServer;
    }
}
