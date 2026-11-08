using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OC2DIYChef;
using Team17.Online;

// Execute the production default resolution and initial-selection dispatch without Unity.
static class ChefDefaultsRegression
{
    static int assertions;
    static readonly ChefAvatarData first = new ChefAvatarData { HeadName = "Chef_First" };
    static readonly ChefAvatarData snowman = new ChefAvatarData { HeadName = "Chef_Snowman" };
    static readonly ChefAvatarData locked = new ChefAvatarData { HeadName = "Chef_Locked" };
    static readonly ChefAvatarData later = new ChefAvatarData { HeadName = "Chef_Later" };
    static readonly DIYChefAvatarData custom = new DIYChefAvatarData { HeadName = "Chef_Custom", id = 23 };
    static readonly DIYChefAvatarData custom2 = new DIYChefAvatarData { HeadName = "Chef_Custom2", id = 48 };

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    static void Configure(bool hasPreferences, params string[] names)
    {
        DIYChefCustomisation.preferredChefs = names.Select(x => new KeyValuePair<string, string>(x, "")).ToArray();
        DIYChefCustomisation.diyChefs = new List<DIYChefAvatarData> { custom, custom2 };
        DIYChefCustomisation.Warnings.Clear();
        ClientMessenger.Sent.Clear();
        DIYChefCustomisation.Sent.Clear();
        DIYChefCustomisation.userDIYChefID = new byte[] { 255, 255, 255, 255 };
        var progress = new MetaGameProgress {
            AvatarDirectory = new AvatarDirectoryData { Avatars = new[] { first, snowman, locked, later } },
            Unlocked = new[] { first, snowman, later }
        };
        DIYChefCustomisation.ConfigureChefDefaults(progress, hasPreferences);
    }

    static void CheckInitial(uint avatar, byte diy)
    {
        var user = new User { IsLocal = true, SelectedChefAvatar = 127 };
        ClientUserSystem.m_Users = new UserList(user);
        DIYChefCustomisation.ApplyDefaultChefSelection();
        Check(ClientMessenger.Sent.Count == 1 && ClientMessenger.Sent[0].Avatar == avatar,
            "initial ordinary avatar ID");
        Check(DIYChefCustomisation.userDIYChefID[0] == diy && DIYChefCustomisation.Sent.Count == 1 &&
            DIYChefCustomisation.Sent[0].ID == diy, "initial custom ID stored and synchronized");
        Check(ClientMessenger.Sent[0].DIYAtDispatch == diy, "custom ID installed before ordinary dispatch");
        Check(DIYChefCustomisation.Sent[0].User == user, "custom ID sent for the correct user");
    }

    public static void Main()
    {
        Configure(true, first.HeadName, custom.HeadName, later.HeadName);
        Check(DIYChefCustomisation.defaultChefID == 0, "official first remains both initial and fallback");
        Check(DIYChefCustomisation.Warnings.Count == 0, "valid official default has no warning");
        CheckInitial(0, 255);

        Configure(true, custom.HeadName, later.HeadName, first.HeadName);
        Check(DIYChefCustomisation.defaultChefID == 3, "fallback follows preference order, not avatar directory order");
        Check(DIYChefCustomisation.Warnings.Count == 0, "custom first is valid when an official fallback follows");
        Check(DIYChefCustomisation.preferredChefs[0].Key == custom.HeadName,
            "custom chef remains first");
        CheckInitial(3, custom.id);

        Configure(true, custom.HeadName, custom2.HeadName);
        Check(DIYChefCustomisation.defaultChefID == 1, "custom-only list falls back to snowman by name");
        Check(DIYChefCustomisation.Warnings.SequenceEqual(new[] { "默认厨师不可用" }),
            "missing official fallback produces the requested warning once");
        Check(DIYChefCustomisation.preferredChefs.Length == 2 &&
            DIYChefCustomisation.preferredChefs[1].Key == custom2.HeadName,
            "snowman is not inserted into a valid custom-only selection list");
        CheckInitial(1, custom.id);

        Configure(true, locked.HeadName, custom2.HeadName, later.HeadName, first.HeadName);
        Check(DIYChefCustomisation.defaultChefID == 3, "locked official fallback is skipped");
        Check(DIYChefCustomisation.Warnings.Count == 0, "later unlocked fallback prevents a warning");
        CheckInitial(3, custom2.id);

        Configure(true, locked.HeadName);
        Check(DIYChefCustomisation.Warnings.Count == 1 && DIYChefCustomisation.defaultChefID == 1,
            "no unlocked official fallback uses snowman and warns");
        Check(DIYChefCustomisation.preferredChefs.Last().Key == snowman.HeadName,
            "an otherwise unselectable list gets a usable switching entry");
        CheckInitial(1, 255);

        // Invalid resource entries have already been removed by registration.
        Configure(true);
        Check(DIYChefCustomisation.Enabled && DIYChefCustomisation.preferredChefs.Length == 1 &&
            DIYChefCustomisation.preferredChefs[0].Key == snowman.HeadName,
            "a configured list whose resources all failed remains usable with snowman");
        CheckInitial(1, 255);

        Configure(true, custom2.HeadName, first.HeadName);
        var local = new User { IsLocal = true, SelectedChefAvatar = 127 };
        var remote = new User { IsLocal = false, SelectedChefAvatar = 127 };
        var existing = new User { IsLocal = true, SelectedChefAvatar = 3 };
        var guest = new User { IsLocal = true, SelectedChefAvatar = 127 };
        ClientUserSystem.m_Users = new UserList(local, remote, existing, guest);
        DIYChefCustomisation.userDIYChefID = new byte[] { 255, 91, 23, 255 };
        DIYChefCustomisation.ApplyDefaultChefSelection();
        Check(ClientMessenger.Sent.Count == 2 && DIYChefCustomisation.Sent.Count == 2,
            "both unselected local users receive defaults");
        Check(DIYChefCustomisation.userDIYChefID.SequenceEqual(new byte[] { 48, 91, 23, 48 }),
            "remote and already selected local choices remain intact");
        Check(DIYChefCustomisation.Sent[0].User == local && DIYChefCustomisation.Sent[1].User == guest,
            "split guest receives its own synchronization");
        DIYChefCustomisation.ApplyDefaultChefSelection();
        Check(ClientMessenger.Sent.Count == 2, "repeated selection does not overwrite selected users");

        // Simulate a new local user after existing users have selected other appearances.
        var newcomer = new User { IsLocal = true, SelectedChefAvatar = 127 };
        ClientUserSystem.m_Users = new UserList(existing, remote, newcomer);
        DIYChefCustomisation.userDIYChefID = new byte[] { 23, 91, 255, 255 };
        DIYChefCustomisation.ApplyDefaultChefSelection();
        Check(ClientMessenger.Sent.Count == 3 && DIYChefCustomisation.Sent.Last().User == newcomer,
            "new local user receives the custom default");
        Check(DIYChefCustomisation.userDIYChefID.SequenceEqual(new byte[] { 23, 91, 48, 255 }),
            "new-user defaults preserve existing choices");

        // Reconfiguring to an official default must clear any previous custom ID.
        Configure(true, first.HeadName);
        DIYChefCustomisation.userDIYChefID[0] = 48;
        CheckInitial(0, 255);
        Configure(false);
        Check(!DIYChefCustomisation.Enabled && DIYChefCustomisation.defaultChefID == 127,
            "missing or empty preferences disable custom defaults");
        ClientUserSystem.m_Users = new UserList(new User { IsLocal = true, SelectedChefAvatar = 127 });
        DIYChefCustomisation.ApplyDefaultChefSelection();
        Check(ClientMessenger.Sent.Count == 0 && DIYChefCustomisation.Sent.Count == 0 &&
            DIYChefCustomisation.Warnings.Count == 0, "disabled defaults neither send messages nor warn");

        Console.WriteLine("PASS: " + assertions + " default-chef assertions; no Unity/game process used.");
    }
}

namespace HarmonyLib
{
    static class AccessTools
    {
        internal static MethodInfo Method(string name)
        {
            if (name != "ClientMessenger:ChefAvatar") throw new Exception("Unexpected method " + name);
            return typeof(ClientMessenger).GetMethod("ChefAvatar");
        }
    }
}

namespace Team17.Online
{
    public sealed class User
    {
        public bool IsLocal;
        public uint SelectedChefAvatar;
    }
    public sealed class UserList
    {
        public User[] _items;
        public int Count { get { return _items.Length; } }
        public UserList(params User[] users) { _items = users; }
    }
    static class ClientUserSystem
    {
        internal static UserList m_Users;
    }
}

public class ChefAvatarData { public string HeadName; }
public sealed class AvatarDirectoryData { public ChefAvatarData[] Avatars; }
public sealed class MetaGameProgress
{
    public AvatarDirectoryData AvatarDirectory;
    public ChefAvatarData[] Unlocked;
    public ChefAvatarData[] GetUnlockedAvatars() { return Unlocked; }
}

static class ClientMessenger
{
    internal sealed class Selection { public uint Avatar; public byte DIYAtDispatch; }
    internal static readonly List<Selection> Sent = new List<Selection>();
    public static void ChefAvatar(uint avatar, User user)
    {
        int index = Array.IndexOf(ClientUserSystem.m_Users._items, user);
        Sent.Add(new Selection { Avatar = avatar, DIYAtDispatch = DIYChefCustomisation.userDIYChefID[index] });
        // Model the ordinary server response so already selected users are skipped next time.
        user.SelectedChefAvatar = avatar;
    }
}

namespace OC2DIYChef
{
    public sealed class DIYChefAvatarData : ChefAvatarData { public byte id; }
    public static partial class DIYChefCustomisation
    {
        public static KeyValuePair<string, string>[] preferredChefs;
        public static uint defaultChefID = 127;
        public static List<DIYChefAvatarData> diyChefs;
        public static byte[] userDIYChefID;
        public static bool Enabled { get { return defaultChefID != 127; } }
        static readonly Dictionary<string, string> dialogText = new Dictionary<string, string> {
            { "DefaultInvalid", "默认厨师不可用" }
        };
        internal sealed class Selection { public byte ID; public User User; }
        internal static readonly List<Selection> Sent = new List<Selection>();
        internal static readonly List<string> Warnings = new List<string>();
        static void ShowWarningDialog(string text) { Warnings.Add(text); }
        public static void SendClientDIYChefMessage(byte id, User user) { Sent.Add(new Selection { ID = id, User = user }); }
    }
}
