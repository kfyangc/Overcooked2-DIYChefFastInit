using System;
using BepInEx.Configuration;
using BitStream;
using OC2DIYChef;
using Team17.Online;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

static class PlayerHatRegression
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    static PlayerHatMessage Roundtrip(PlayerHatMessage packet)
    {
        var writer = new BitStreamWriter(); packet.Serialise(writer);
        var copy = new PlayerHatMessage();
        Check(copy.Deserialise(new BitStreamReader(writer)), "packet deserialises");
        return copy;
    }
    static User Local(EngagementSlot slot, User.SplitStatus split = User.SplitStatus.Whole)
    { return new User { IsLocal = true, Machine = 1, Engagement = slot, Split = split }; }
    static GameObject Actor(User user)
    {
        var actor = new GameObject(); actor.AddComponent<ChefMeshReplacer>(); actor.AddComponent<HatMeshVisibility>();
        PlayerHats.Bind(actor, user); return actor;
    }
    static void Main()
    {
        var config = new ConfigFile(); PlayerHats.Configure(config);
        int chefChanges = 0; ClientUserSystem.usersChanged = () => chefChanges++;
        var p1 = Local(EngagementSlot.One); var p2 = Local(EngagementSlot.Two);
        var split = Local(EngagementSlot.One, User.SplitStatus.SplitPadGuest);
        var first = Actor(p1); var second = Actor(p2); var guest = Actor(split);
        Check(PlayerHats.Resolve(first, "Chef_Fox") == "Fancy", "initial selection inherits prefer.txt");
        PlayerHats.Set(p1, "None"); PlayerHats.Set(p2, "Santa"); PlayerHats.Set(split, "Baseballcap");
        Check(PlayerHats.Resolve(first, "Chef_Fox") == "None" && PlayerHats.Resolve(second, "Chef_Fox") == "Santa" &&
            PlayerHats.Resolve(guest, "Chef_Fox") == "Baseballcap", "same chef has separate hats for each local and split player");
        Check(chefChanges == 0, "hat changes do not emit avatar events or reset unrelated player input");
        PlayerHats.Update(); int applied = HatData.Applied;
        PlayerHats.Update(); Check(HatData.Applied == applied, "unchanged actors do not continually reapply hats");
        first.GetComponent<ChefMeshReplacer>().ChefModel = new GameObject(); PlayerHats.Update();
        Check(HatData.Applied == applied + 1, "replaced player model reacquires the chosen hat");
        var transaction = new PlayerHatTransaction(); transaction.Begin(); transaction.Capture(p1); transaction.Capture(p2);
        PlayerHats.Set(p1, "Fancy"); transaction.Capture(p1); PlayerHats.Set(p2, "None"); transaction.Dispose();
        Check(PlayerHats.Get(p1) == "None" && PlayerHats.Get(p2) == "Santa", "cancel restores each player's initial hat despite repeated refresh capture");
        transaction.Begin(); transaction.Capture(p1); PlayerHats.Set(p1, "Fancy"); transaction.Commit(); transaction.Dispose();
        Check(PlayerHats.Get(p1) == "Fancy", "confirmation preserves the edited hat");
        var custom = new HatData { HatName = "Hat_巫师帽" }; DIYChefCustomisation.diyHats.Add(custom);
        Check(PlayerHats.Choices().Contains("巫师帽"), "installed DIY hats are offered independently of prefer.txt assignments");
        PlayerHats.Set(p1, "巫师帽"); PlayerHats.Update();
        Check(ChefLoading.Requests > 0, "unprepared DIY hats enter deferred resource loading");
        custom.prefab = new GameObject(); PlayerHats.Update();
        Check(first.GetComponent<PlayerHatOwner>().AppliedHat == "巫师帽", "loaded hat attaches after asynchronous completion");
        var thumbnail = Actor(p1); PlayerHats.Bind(thumbnail, p1, true);
        Check(PlayerHats.Resolve(thumbnail, "Chef_Fox") == "Fancy", "shared thumbnail actor keeps author defaults rather than a player's override");
        PlayerHats.BindThumbnail(thumbnail);
        Check(PlayerHats.Resolve(thumbnail, "Chef_Fox") == "None" && PlayerHats.Resolve(thumbnail, "Chef_Other") == "None",
            "thumbnail actor hides both configured hats and native fallback hats independently of chef selection");
        PlayerHats.Set(p1, "Santa");
        Check(PlayerHats.Resolve(thumbnail, "Chef_Fox") == "None", "switching a player's hat cannot affect thumbnail rendering or its resource request");
        PlayerHats.Set(p1, "巫师帽");
        PlayerHats.Shutdown(); PlayerHats.Configure(config);
        Check(PlayerHats.Get(Local(EngagementSlot.One)) == "巫师帽" && PlayerHats.Get(Local(EngagementSlot.Two)) == "Santa",
            "input-slot preferences survive service recreation and new User instances");
        var remote = new User { Machine = 2, Engagement = EngagementSlot.One };
        var remoteActor = Actor(remote);
        PlayerHats.Receive(remote, "PeerOnlyHat");
        Check(PlayerHats.Get(remote) == "PeerOnlyHat" && PlayerHats.Resolve(remoteActor, "Chef_Fox") == "Fancy",
            "missing peer resource falls back locally while preserving its name for forwarding");
        DIYChefCustomisation.localClient = new Client(); PlayerHatNetwork.Send(p1);
        var clientPacket = Roundtrip(DIYChefCustomisation.localClient.Last);
        Check(!clientPacket.ServerSnapshot && clientPacket.Hats[0] == "巫师帽" && clientPacket.Users[0].m_Machine == 1,
            "client transport preserves UTF-8 hat name and stable user identity");
        DIYChefCustomisation.localServer = new Server();
        ServerUserSystem.m_Users._items = new[] { p1, remote, p2, split };
        PlayerHatNetwork.Broadcast();
        var snapshot = Roundtrip(DIYChefCustomisation.localServer.Last);
        Check(snapshot.ServerSnapshot && snapshot.Users.Length == 4 && snapshot.Hats[1] == "PeerOnlyHat" && snapshot.Hats[3] == "Baseballcap",
            "server snapshots include distinct whole/split hats and forward hats absent on the host");
        var identity = new ChefAvatarMessage(); identity.Initialise(127, 2, EngagementSlot.One, User.SplitStatus.Whole);
        PlayerHatNetwork.Receive(Roundtrip(new PlayerHatMessage { Users = new[] { identity }, Hats = new[] { "Baseballcap" } }));
        Check(PlayerHats.Get(remote) == "Baseballcap" && DIYChefCustomisation.localServer.Last.Hats[1] == "Baseballcap",
            "a remote client's change is applied by the server and rebroadcast by stable identity");
        // A late-joining client may receive the hat snapshot before the normal roster.
        PlayerHats.Shutdown(); PlayerHats.Configure(config); DIYChefCustomisation.localServer = null;
        ClientUserSystem.m_Users._items = new User[0]; PlayerHatNetwork.Receive(snapshot);
        Check(PlayerHats.Get(remote) == "PeerOnlyHat", "early snapshots remain available when the user roster arrives later");
        Check(PlayerHats.Get(p1) == "巫师帽", "server snapshots cannot replace locally saved choices");
        var invalid = new BitStreamWriter(); invalid.Write(false); invalid.Write(7, 3);
        Check(!new PlayerHatMessage().Deserialise(new BitStreamReader(invalid)), "invalid roster size is rejected");
        PlayerHats.Remove(remote); Check(PlayerHats.Get(remote) == PlayerHats.Inherit, "departing remote identity releases its old hat");
        Console.WriteLine("PASS: " + checks + " player-hat persistence, transaction, resource and network checks.");
    }
}
