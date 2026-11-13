using System;
using System.Collections.Generic;
using System.Reflection;
using OC2DIYChef;
using Team17.Online;

static class ChefGridRegression
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    static ChefAvatarData Chef(string name) { return new ChefAvatarData { HeadName = name }; }

    static void Main()
    {
        checks += ChefGridFramingRegression.Run();
        checks += ChefGridTransparencyRegression.Run();
        checks += ChefGridLayoutRegression.Run();
        checks += ChefGridVisibilityRegression.Run();
        checks += ChefGridPoseRegression.Run();
        checks += ChefGridFaceRegression.Run();
        checks += ChefGridCacheWarmupRegression.Run();
        checks += ChefGridInteractionRegression.Run();
        checks += ChefGridEngagementRegression.Run();
        checks += ChefGridScrollBarRegression.Run();
        checks += ChefGridMouseRegression.Run();
        checks += ChefGridPromptRegression.Run();
        Check(ChefGridNavigation.Move(0, 0, 10, 1, 0) == -1, "empty roster");
        Check(ChefGridNavigation.Move(0, 1, 10, 0, -1) == 0, "single entry");
        Check(ChefGridNavigation.Move(39, 81, 10, 1, 0) == 40, "right crosses a page");
        Check(ChefGridNavigation.Move(40, 81, 10, -1, 0) == 39, "left crosses a page");
        Check(ChefGridNavigation.Move(35, 81, 10, 0, 1) == 45, "down preserves column across pages");
        Check(ChefGridNavigation.Move(79, 81, 10, 0, 1) == 80, "partial row clamps to existing cell");
        Check(ChefGridNavigation.Move(0, 81, 10, 0, -1) == 0, "up stops at first row");
        Check(ChefGridNavigation.Move(80, 81, 10, 1, 0) == 80, "right stops at last entry");
        Check(ChefGridNavigation.Move(0, 81, 10, -1, 0) == 0, "left stops at first entry");
        Check(ChefGridNavigation.Move(4, 81, 10, 0, -1) == 4, "top row preserves its column at the boundary");
        Check(ChefGridNavigation.Move(105, 109, 10, 0, 1) == 105 &&
            ChefGridNavigation.Move(108, 109, 10, 0, 1) == 108, "the partial last row cannot wrap to the first page");
        Check(ChefGridNavigation.RowCount(0) == 0 && ChefGridNavigation.RowCount(50) == 5 &&
            ChefGridNavigation.RowCount(51) == 6, "fixed ten-column row counts");
        Check(ChefGridNavigation.PlayerKey(1, 0) != ChefGridNavigation.PlayerKey(1, 1), "split users have distinct cursors");
        Check(ChefGridNavigation.Follow(0, 30, 109) == 0 && ChefGridNavigation.Follow(0, 40, 109) == 1,
            "downward movement scrolls by one row as the cursor enters the fifth visible row");
        Check(ChefGridNavigation.Follow(3, 40, 109) == 3 && ChefGridNavigation.Follow(3, 30, 109) == 2,
            "upward movement scrolls by one row as the cursor enters the first visible row");
        Check(ChefGridNavigation.Follow(0, 0, 109) == 0 && ChefGridNavigation.Follow(5, 108, 109) == 6,
            "top and bottom rows can reach viewport edges without looping or empty trailing rows");
        Check(ChefGridNavigation.ShiftRows(25, 109, -3) == 5 && ChefGridNavigation.ShiftRows(25, 109, 3) == 55 &&
            ChefGridNavigation.ShiftRows(99, 109, 1) == 108, "row scrolling preserves columns or clamps to the partial last row");
        Check(ChefGridNavigation.ShiftRows(108, 109, int.MaxValue) == 108 &&
            ChefGridNavigation.ShiftRows(25, 109, int.MinValue) == 5 && ChefGridNavigation.ShiftRows(0, 0, 1) == -1,
            "scrolling at either end is bounded, including empty rosters and extreme deltas");
        for (int count = 1; count <= 400; count++)
            for (int index = 0; index < count; index++)
                foreach (var delta in new[] { new[] { -1, 0 }, new[] { 1, 0 }, new[] { 0, -1 }, new[] { 0, 1 } })
                {
                    int next = ChefGridNavigation.Move(index, count, 10, delta[0], delta[1]);
                    Check(next >= 0 && next < count &&
                        ((delta[0] > 0 || delta[1] > 0) ? next >= index : next <= index),
                        "navigation reaches a real entry without reversing or wrapping at an edge");
                }
        for (int count = 0; count <= 400; count++)
        {
            int last = ChefGridNavigation.LastFirstRow(count);
            Check(ChefGridNavigation.Follow(-100, 0, count) == 0 &&
                ChefGridNavigation.Follow(1000, count - 1, count) == last, "invalid initial scroll positions clamp to list ends");
            for (int index = 0; index < count; index++)
            {
                int first = ChefGridNavigation.Follow(0, index, count);
                foreach (int dy in new[] { -1, 1 })
                {
                    int next = ChefGridNavigation.Move(index, count, 10, 0, dy);
                    int follow = ChefGridNavigation.Follow(first, next, count);
                    int visibleRow = next / 10 - follow;
                    Check(follow >= 0 && follow <= last && visibleRow >= 0 && visibleRow < 5 &&
                        (visibleRow >= 1 || follow == 0) && (visibleRow <= 3 || follow == last) &&
                        Math.Abs(follow - first) <= 1,
                        "one-row movement keeps the cursor in rows 2-4 except at the bounded list ends");
                    var rect = new ChefGridLayout().Cell(next - follow * 10);
                    Check(new ChefGridLayout().HitTest(rect.X + rect.Size / 2, rect.Y + rect.Size / 2, follow, count) == next,
                        "mouse hit testing follows the same scrolled entry as keyboard navigation");
                }
            }
        }

        var official = Chef("Chef_First");
        var locked = Chef("Chef_Locked");
        var fallback = Chef("Chef_Snowman");
        var custom = new DIYChefAvatarData { HeadName = "Chef_Extra", id = 23 };
        var all = new[] { official, locked, fallback };
        var unlocked = new[] { official, fallback };
        GameUtils.Directory = new AvatarDirectoryData { Avatars = all };
        DIYChefCustomisation.defaultChefID = 2;
        DIYChefCustomisation.diyChefs.Add(custom);
        DIYChefCustomisation.preferredChefs = new[] {
            new KeyValuePair<string, string>(custom.HeadName, "Fancy"),
            new KeyValuePair<string, string>(locked.HeadName, ""),
            new KeyValuePair<string, string>(official.HeadName, "None"),
            new KeyValuePair<string, string>("missing", "") };
        var entries = ChefGridRoster.Build(all, unlocked);
        Check(entries.Count == 2, "locked and missing entries excluded");
        Check(entries[0].Avatar == custom && entries[1].Avatar == official, "preference order preserved");
        Check(entries[0].OfficialID == 2 && entries[0].CustomID == 23 && entries[0].Hat == "Fancy", "custom fallback and hat preserved");
        Check(entries[1].OfficialID == 0 && entries[1].CustomID == 255, "official identity is directory index");
        Check(entries[0].Label == "Extra", "chef label");

        var local = new User { IsLocal = true, SelectedChefAvatar = 0 };
        var remote = new User { IsLocal = false, SelectedChefAvatar = 0 };
        ClientUserSystem.m_Users = new UserList(local, remote);
        var view = new FrontendChefCustomisation(unlocked, official);
        DIYChefCustomisation.localClient = new object();
        var events = TestMessenger.Events;
        ClientUserSystem.usersChanged = delegate { events.Add("refresh:" + DIYChefCustomisation.userDIYChefID[0]); };
        Check(ChefGridRoster.Find(entries, 0) == 1, "initial official cursor");
        Check(ChefGridRoster.Select(entries[0], 0, view), "custom cell selects directly");
        Check(events[0] == "avatar:2:custom:23" && events[1] == "custom:23" && events[2] == "refresh:23",
            "custom ID installed before official message and final display refresh");
        Check(view.GetCurrentSelection().Character == fallback && view.Selection == 1, "vanilla state remains valid fallback");
        Check(ChefGridRoster.Find(entries, 0) == 0, "custom cursor uses custom ID rather than fallback");
        events.Clear();
        Check(ChefGridRoster.Select(entries[1], 0, view), "official cell selects directly");
        Check(events[0] == "avatar:0:custom:255" && events[1] == "custom:255", "official selection clears custom ID first");
        Check(view.GetCurrentSelection().Character == official && view.Selection == 0, "official state restored");
        events.Clear();
        Check(!ChefGridRoster.Select(entries[0], 1, view) && events.Count == 0, "remote cursor cannot be changed");
        Check(!ChefGridRoster.Select(entries[0], -1, view) && !ChefGridRoster.Select(entries[0], 4, view), "invalid player slots ignored");
        view.Active = false;
        Check(!ChefGridRoster.Select(entries[0], 0, view), "unbound input cannot select");
        view.Active = true;

        // A custom-only list does not need to contain its official network fallback.
        DIYChefCustomisation.preferredChefs = new[] { new KeyValuePair<string, string>(custom.HeadName, "") };
        entries = ChefGridRoster.Build(all, unlocked);
        Check(entries.Count == 1 && entries[0].CustomID == 23, "custom-only roster retains one entry");
        Check(ChefGridRoster.Select(entries[0], 0, new FrontendChefCustomisation(new[] { official }, official)),
            "custom selection accepts fallback outside menu's unlocked array");
        DIYChefCustomisation.Enabled = false;
        entries = ChefGridRoster.Build(all, unlocked);
        Check(entries.Count == 2 && entries[0].Avatar == official && entries[1].Avatar == fallback,
            "without prefer.txt use all unlocked official chefs");
        DIYChefCustomisation.localClient = null;
        events.Clear();
        Check(ChefGridRoster.Select(entries[0], 0, view) && events.Count == 2, "official-only mode uses vanilla message without DIY transport");
        Console.WriteLine("PASS: " + checks + " chef grid assertions; no Unity/game process used.");
    }
}

namespace HarmonyLib
{
    static class AccessTools
    {
        public static FieldInfo Field(Type type, string name)
        {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingFieldException(type.FullName, name);
        }
        public static MethodInfo Method(string name) { return typeof(TestMessenger).GetMethod("ChefAvatar"); }
        public static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }
        public static MethodInfo Method(Type type, string name, Type[] parameters)
        {
            return type.GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, parameters, null);
        }
    }
}

namespace Team17.Online
{
    sealed class User { public bool IsLocal; public uint SelectedChefAvatar; }
    sealed class UserList
    {
        public User[] _items;
        public int Count { get { return _items.Length; } }
        public UserList(params User[] users) { _items = users; }
    }
    static class ClientUserSystem { public static UserList m_Users; public static Action usersChanged; }
}

class ChefAvatarData { public string HeadName; public UnityEngine.GameObject FrontendModelPrefab; }
sealed class DIYChefAvatarData : ChefAvatarData { public byte id; }
sealed class AvatarDirectoryData { public ChefAvatarData[] Avatars; }
static class GameUtils
{
    public static AvatarDirectoryData Directory;
    public static AvatarDirectoryData GetAvatarDirectoryData() { return Directory; }
    public static T RequestManager<T>() where T : class
    {
        if (typeof(T) == typeof(ControllerIconLookup)) return ControllerIconLookup.Instance as T;
        return GamepadEngagementManager.Instance as T;
    }
}
sealed class SelectedChefData { public ChefAvatarData Character; }
sealed class FrontendChefCustomisation
{
    readonly ChefAvatarData[] m_unlockedAvatars;
#pragma warning disable CS0649
    int m_chefSelection;
#pragma warning restore CS0649
    readonly SelectedChefData data;
    public bool Active = true;
    public int Selection { get { return m_chefSelection; } }
    public FrontendChefCustomisation(ChefAvatarData[] unlocked, ChefAvatarData selected)
    {
        m_unlockedAvatars = unlocked;
        data = new SelectedChefData { Character = selected };
    }
    public bool IsActive() { return Active; }
    public SelectedChefData GetCurrentSelection() { return data; }
}
static class TestMessenger
{
    public static readonly List<string> Events = new List<string>();
    public static void ChefAvatar(uint id, User user)
    {
        Events.Add("avatar:" + id + ":custom:" + DIYChefCustomisation.userDIYChefID[0]);
        user.SelectedChefAvatar = id;
    }
}
namespace OC2DIYChef
{
    static class DIYChefCustomisation
    {
        public static bool Enabled = true;
        public static KeyValuePair<string, string>[] preferredChefs;
        public static readonly List<DIYChefAvatarData> diyChefs = new List<DIYChefAvatarData>();
        public static byte[] userDIYChefID = { 255, 255, 255, 255 };
        public static uint defaultChefID;
        public static object localClient;
        public static void SendClientDIYChefMessage(byte id, User user) { TestMessenger.Events.Add("custom:" + id); }
    }
}
