using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using Team17.Online;
using UnityEngine;

namespace OC2DIYChef
{
    // A user binding belongs to the rendered instance, never to a shared avatar prefab.
    public sealed class PlayerHatOwner : MonoBehaviour
    {
        internal User User;
        internal bool DefaultOnly;
        internal bool HideHat;
        internal GameObject AppliedModel;
        internal string AppliedHat;
        internal HatMeshVisibility.VisState DefaultState = HatMeshVisibility.VisState.Fancy;
    }

    internal static class PlayerHats
    {
        internal const string Inherit = "@default";
        static ConfigFile config;
        static bool applying;
        static readonly Dictionary<string, ConfigEntry<string>> preferences = new Dictionary<string, ConfigEntry<string>>();
        static readonly Dictionary<string, string> remote = new Dictionary<string, string>();
        static readonly List<PlayerHatOwner> owners = new List<PlayerHatOwner>();

        internal static void Configure(ConfigFile file) { config = file; }
        static string Key(User.MachineID machine, EngagementSlot slot, User.SplitStatus split)
        { return machine + ":" + (int)slot + ":" + (int)split; }
        internal static string Key(User user) { return Key(user.Machine, user.Engagement, user.Split); }
        static ConfigEntry<string> Preference(User user)
        {
            string key = "Player" + (int)user.Engagement + "Split" + (int)user.Split;
            ConfigEntry<string> entry;
            if (!preferences.TryGetValue(key, out entry))
                preferences[key] = entry = config.Bind("PlayerHats", key, Inherit,
                    "Hat for this local input slot: @default uses prefer.txt; None removes the hat.");
            return entry;
        }

        internal static string Get(User user)
        {
            if (user == null) return Inherit;
            if (user.IsLocal && config != null) return Preference(user).Value;
            string hat;
            return remote.TryGetValue(Key(user), out hat) ? hat : Inherit;
        }

        internal static void Set(User user, string hat, bool send = true)
        {
            if (user == null || !user.IsLocal || !Available(hat)) return;
            if (config != null) Preference(user).Value = hat;
            remote[Key(user)] = hat;
            Invalidate();
            if (send) PlayerHatNetwork.Send(user);
        }

        internal static void Receive(User user, string hat)
        {
            Receive(user.Machine, user.Engagement, user.Split, hat);
        }

        internal static void Receive(User.MachineID machine, EngagementSlot slot, User.SplitStatus split, string hat)
        {
            // Preserve unknown names for forwarding: the host may not own a hat
            // that two other clients have installed. Resolve handles local fallback.
            remote[Key(machine, slot, split)] = hat;
            Invalidate();
        }

        internal static void Remove(User user) { remote.Remove(Key(user)); }

        internal static void Cycle(User user, int step)
        {
            var choices = Choices();
            int index = Math.Max(0, choices.IndexOf(Get(user)));
            Set(user, choices[(index + step % choices.Count + choices.Count) % choices.Count]);
        }

        static List<string> Choices()
        {
            var result = new List<string> { Inherit, "None", "Fancy", "Santa", "Baseballcap" };
            foreach (var hat in DIYChefCustomisation.diyHats)
            {
                string name = hat.HatName.Substring(4);
                if (!result.Contains(name)) result.Add(name);
            }
            return result;
        }

        static bool Available(string hat)
        {
            if (hat == Inherit || hat == "None" || hat == "Fancy" || hat == "Santa" || hat == "Baseballcap") return true;
            return DIYChefCustomisation.diyHats.Exists(item => item.HatName == "Hat_" + hat);
        }

        internal static string Resolve(GameObject actor, string head)
        {
            var owner = actor.GetComponent<PlayerHatOwner>();
            if (owner != null && owner.HideHat) return "None";
            string hat = owner == null || owner.DefaultOnly ? Inherit : Get(owner.User);
            if (hat != Inherit && Available(hat)) return hat;
            var pairs = DIYChefCustomisation.preferredChefs;
            if (pairs != null)
                foreach (var pair in pairs) if (pair.Key == head) return pair.Value;
            return "";
        }

        internal static void Bind(GameObject actor, User user, bool defaultOnly = false)
        {
            var owner = actor.GetComponent<PlayerHatOwner>();
            if (owner == null) { owner = actor.AddComponent<PlayerHatOwner>(); owners.Add(owner); }
            owner.User = user;
            owner.DefaultOnly = defaultOnly;
        }

        internal static void RememberDefault(GameObject actor, HatMeshVisibility.VisState state)
        {
            var owner = actor.GetComponent<PlayerHatOwner>();
            if (!applying && owner != null) owner.DefaultState = state;
        }

        internal static void BindThumbnail(GameObject actor)
        {
            Bind(actor, null, true);
            actor.GetComponent<PlayerHatOwner>().HideHat = true;
        }

        static void Invalidate()
        {
            foreach (var owner in owners) if (owner != null) owner.AppliedModel = null;
        }

        internal static void Update()
        {
            for (int i = owners.Count - 1; i >= 0; i--)
            {
                var owner = owners[i];
                if (owner == null) { owners.RemoveAt(i); continue; }
                if (owner.DefaultOnly || !owner.gameObject.activeInHierarchy) continue;
                var replacer = owner.GetComponent<ChefMeshReplacer>();
                if (replacer == null || replacer.ChefModel == null || replacer.GetChefData() == null) continue;
                string hat = Resolve(owner.gameObject, replacer.GetChefData().Character.HeadName);
                if (owner.AppliedModel == replacer.ChefModel && owner.AppliedHat == hat) continue;
                var custom = DIYChefCustomisation.diyHats.Find(x => x.HatName == "Hat_" + hat);
                if (custom != null && custom.prefab == null)
                {
                    if (!custom.Resources.Failed(ChefMeshReplacer.ChefModelType.FrontEnd)) ChefLoading.RequestHat(custom);
                    continue;
                }
                var frontend = owner.GetComponent<FrontendChef>();
                var visibility = owner.GetComponent<HatMeshVisibility>();
                var state = owner.DefaultState;
                applying = true;
                try
                {
                    if (frontend != null) frontend.SetChefHat(state);
                    else
                    {
                        HatData.SetChefHat(owner.gameObject, ref state);
                        if (visibility != null) visibility.Setup(state);
                    }
                }
                finally { applying = false; }
                owner.AppliedModel = replacer.ChefModel;
                owner.AppliedHat = hat;
            }
        }

        internal static void Shutdown() { owners.Clear(); remote.Clear(); preferences.Clear(); config = null; applying = false; }
    }
}
