using System;
using System.Collections.Generic;
using System.Reflection;
using OC2DIYChef;
using Team17.Online;

// Test the production snapshot, restore ordering and synchronization against a small
// managed adapter. This does not load Unity or start a game process.
static class ChefSelectionRegression
{
    static int assertions;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        assertions++;
    }

    static void Main()
    {
        var local = new User { IsLocal = true, Engagement = 1, Split = 0 };
        var guest = new User { IsLocal = true, Engagement = 1, Split = 1 };
        var remote = new User { IsLocal = false, Engagement = 4, Split = 0 };
        var newcomer = new User { IsLocal = true, Engagement = 2, Split = 0 };
        var customiser = new ChefCustomiser(3);
        var menu = new FrontendChefMenu(customiser);
        ClientUserSystem.m_Users = new UserList(local, guest, remote);

        // Both directions between original and custom chefs, plus custom-to-custom.
        foreach (var pair in new[] { new byte[] { 255, 23 }, new byte[] { 23, 255 }, new byte[] { 23, 48 } })
        {
            DIYChefCustomisation.userDIYChefID = new byte[] { pair[0], 81, 15, 255 };
            ChefSelectionMenu.Capture(customiser);
            DIYChefCustomisation.userDIYChefID[0] = pair[1];
            DIYChefCustomisation.userDIYChefID[1] = 52;
            ChefLoading.Cancelled.Clear();
            Check(ChefSelectionMenu.RestoreBeforeAvatars(customiser), "cancel finds entry snapshot");
            Check(DIYChefCustomisation.userDIYChefID[0] == pair[0], "cancel restores original/custom choice");
            Check(DIYChefCustomisation.userDIYChefID[1] == 81, "split guest has independent choice");
            Check(DIYChefCustomisation.userDIYChefID[2] == 15, "remote choice remains unchanged");
            Check(ChefLoading.Cancelled.Count == 2, "cancel only local preview requests");
            // A camera transition may refuse to hide the menu. Keep its entry snapshot
            // until a successful Hide, so a second cancel still restores the same choice.
            DIYChefCustomisation.userDIYChefID[0] = 94;
            Check(ChefSelectionMenu.RestoreBeforeAvatars(customiser) &&
                DIYChefCustomisation.userDIYChefID[0] == pair[0], "repeated cancel before hide restores entry state");
            ChefSelectionMenu.Release(menu);
        }

        DIYChefCustomisation.userDIYChefID = new byte[] { 31, 72, 12, 255 };
        ChefSelectionMenu.Capture(customiser);
        ClientUserSystem.m_Users = new UserList(remote, guest, local, newcomer);
        DIYChefCustomisation.userDIYChefID = new byte[] { 90, 99, 98, 97 };
        Check(ChefSelectionMenu.RestoreBeforeAvatars(customiser), "cancel after player order changes");
        Check(DIYChefCustomisation.userDIYChefID[0] == 90, "reordered remote user is not restored by old index");
        Check(DIYChefCustomisation.userDIYChefID[1] == 72, "split guest restored by input identity");
        Check(DIYChefCustomisation.userDIYChefID[2] == 31, "local user restored at new index");
        Check(DIYChefCustomisation.userDIYChefID[3] == 97, "new user has no stale entry choice");

        int notifications = 0;
        ClientUserSystem.usersChanged = delegate { notifications++; };
        DIYChefCustomisation.Sent.Clear();
        DIYChefCustomisation.localClient = new object();
        ChefSelectionMenu.SynchronizeRestoredChoices();
        Check(DIYChefCustomisation.Sent.Count == 3, "synchronization only includes current local users");
        Check(DIYChefCustomisation.Sent[1] == 31 && notifications == 1, "restored IDs sent before display notification");
        DIYChefCustomisation.localClient = null;
        ChefSelectionMenu.SynchronizeRestoredChoices();
        Check(notifications == 2 && DIYChefCustomisation.Sent.Count == 3, "display refresh works without a connected client");

        // A/accept calls Hide, which discards the entry state without restoring it.
        ChefSelectionMenu.Release(menu);
        byte accepted = DIYChefCustomisation.userDIYChefID[2] = 42;
        Check(!ChefSelectionMenu.RestoreBeforeAvatars(customiser) &&
            DIYChefCustomisation.userDIYChefID[2] == accepted, "accept keeps preview and discards old snapshot");
        ChefSelectionMenu.Capture(customiser);
        DIYChefCustomisation.userDIYChefID[2] = 89;
        Check(ChefSelectionMenu.RestoreBeforeAvatars(customiser) &&
            DIYChefCustomisation.userDIYChefID[2] == accepted, "reopened menu uses most recently accepted choice");
        ChefSelectionMenu.Clear();
        Check(!ChefSelectionMenu.RestoreBeforeAvatars(customiser), "shutdown discards session snapshots");
        Console.WriteLine("PASS: " + assertions + " selection cancellation assertions; no Unity/game process used.");
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

namespace Team17.Online
{
    sealed class User { public bool IsLocal; public int Engagement, Split; }
    sealed class UserList
    {
        public User[] _items;
        public int Count { get { return _items.Length; } }
        public UserList(params User[] users) { _items = users; }
    }
    static class ClientUserSystem
    {
        public static UserList m_Users;
        public static Action usersChanged;
    }
}

sealed class ChefMeshReplacer { }
sealed class FrontendChefCustomisation
{
#pragma warning disable CS0414
    readonly ChefMeshReplacer m_chef = new ChefMeshReplacer();
#pragma warning restore CS0414
}
sealed class ChefCustomiser
{
#pragma warning disable CS0414
    readonly FrontendChefCustomisation[] m_chefCustomisations;
#pragma warning restore CS0414
    public ChefCustomiser(int count)
    {
        m_chefCustomisations = new FrontendChefCustomisation[count];
        for (int i = 0; i < count; i++) m_chefCustomisations[i] = new FrontendChefCustomisation();
    }
}
sealed class FrontendChefMenu
{
#pragma warning disable CS0414
    readonly ChefCustomiser m_customiser;
#pragma warning restore CS0414
    public FrontendChefMenu(ChefCustomiser customiser) { m_customiser = customiser; }
}
namespace OC2DIYChef
{
    static class DIYChefCustomisation
    {
        public static bool Enabled = true;
        public static byte[] userDIYChefID;
        public static object localClient;
        public static readonly List<byte> Sent = new List<byte>();
        public static void SendClientDIYChefMessage(byte id, User user) { Sent.Add(id); }
    }
    static class ChefLoading
    {
        public static readonly List<ChefMeshReplacer> Cancelled = new List<ChefMeshReplacer>();
        public static void Cancel(ChefMeshReplacer replacer) { Cancelled.Add(replacer); }
    }
}
