using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Team17.Online;
using Team17.Online.Multiplayer;
using Team17.Online.Multiplayer.Messaging;
using BitStream;
using OC2DIYChef.Extension;
using System.Collections;

namespace OC2DIYChef
{
    public static partial class DIYChefCustomisation
    {
        public static KeyValuePair<string, string>[] preferredChefs;
        public static uint defaultChefID = 127;
        public static List<DIYChefAvatarData> diyChefs = new List<DIYChefAvatarData>();
        public static List<HatData> diyHats = new List<HatData>();
        public static byte[] userDIYChefID = Enumerable.Repeat((byte)255, 4).ToArray();
        private static Dictionary<string, string> dialogText;
        public static bool enableLobbySwitchChef = false;
        public static ChefAvatarData defaultTemplate;

        public static Client localClient = null;
        public static Server localServer = null;
        public const MessageType diyChefMessageType = (MessageType)67;

        public static GUIStyle guiStyle;
        public static Texture2D background;
        static bool registered, subscribed;
        static readonly HashSet<Mesh> fixedMeshes = new HashSet<Mesh>();

        public static void OnAwake(MetaGameProgress metaGameProgress)
        {
            dialogText = Localization.GetLanguage() == SupportedLanguages.Chinese ?
                new Dictionary<string, string>()
                {
                    {"DefaultInvalid", "默认厨师不可用" },
                    {"MissingINFO", "缺失 INFO 文件" },
                    {"MissingTexture", "缺失主贴图" },
                    {"IDConflict", "ID 冲突" },
                    {"HatInvalid", "帽子不可用" },
                } :
                new Dictionary<string, string>()
                {
                    {"DefaultInvalid", "Invalid default chef" },
                    {"MissingINFO", "Missing INFO file" },
                    {"MissingTexture", "Missing main texture" },
                    {"IDConflict", "ID conflict" },
                    {"HatInvalid", "Invalid hat" },
                };
            if (registered) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            defaultTemplate = metaGameProgress.AvatarDirectory.Avatars[15];
            LoadPreferredChefs();
            bool hasPreferences = preferredChefs.Length != 0;
            var hats = LoadAllDIYHat();
            while (hats.MoveNext()) { }
            var chefs = LoadAllDIYChef(metaGameProgress);
            while (chefs.MoveNext()) { }
            ConfigureChefDefaults(metaGameProgress, hasPreferences);
            registered = true;
            DIYChefPlugin.Log("Register chefs=" + diyChefs.Count + " hats=" + diyHats.Count +
                " elapsedMs=" + timer.Elapsed.TotalMilliseconds.ToString("F2") + " (metadata only)");
            if (subscribed) return;
            subscribed = true;
            ServerUserSystem.OnUserRemovedWithIndex = (GenericVoid<User, int>)Delegate.Combine(ServerUserSystem.OnUserRemovedWithIndex, new GenericVoid<User, int>(OnUserRemoved));
            ServerUserSystem.OnUserAdded = (GenericVoid<User>)Delegate.Combine(ServerUserSystem.OnUserAdded, new GenericVoid<User>(OnUserAdded));
        }

        public static void Shutdown()
        {
            if (!subscribed) return;
            ServerUserSystem.OnUserRemovedWithIndex -= OnUserRemoved;
            ServerUserSystem.OnUserAdded -= OnUserAdded;
            subscribed = false;
        }

        private static void LoadPreferredChefs()
        {
            preferredChefs = new KeyValuePair<string, string>[0];
            enableLobbySwitchChef = false;
            string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/prefer.txt";
            if (!File.Exists(path))
                path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/prefer.txt.txt";
            if (!File.Exists(path))
            {
                defaultChefID = 127U;
                return;
            }
            var lines = File.ReadAllLines(path);
            foreach (var line in lines)
            {
                if (line.Contains("LOBBYSWITCHCHEF=TRUE"))
                {
                    enableLobbySwitchChef = true;
                    continue;
                }
                var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ConvertAll(s => s.Trim());
                if (parts.Length == 0) continue;
                string hat = "";
                if (parts.Length > 1 && parts[1].StartsWith("HAT="))
                {
                    hat = parts[1].Substring(4);
                    if (hat == "Festive") hat = "Santa";
                    if (hat == "Baseball") hat = "Baseballcap";
                }
                var pair = new KeyValuePair<string, string>(parts[0], hat);
                if (!preferredChefs.Any(x => x.Key == pair.Key))
                {
                    preferredChefs = preferredChefs.AddToArray(pair);
                }
            }

            if (preferredChefs.Length == 0)
            {
                defaultChefID = 127U;
                return;
            }
        }

        static IEnumerator LoadAllDIYHat()
        {
            diyHats.Clear();
            if (preferredChefs.Length == 0)
                yield break;
            diyHats.Add(HatData.Create("Santa"));
            diyHats.Add(HatData.Create("Baseballcap"));

            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = dir + "/Resources/HATS";
            if (!Directory.Exists(path))
                yield break;

            DirectoryInfo parent = new DirectoryInfo(path);
            foreach (DirectoryInfo folder in parent.GetDirectories())
            {
                string name = folder.Name;
                if (File.Exists($"{path}/{name}/t_{name}.png") &&
                    File.Exists($"{path}/{name}/m_{name}.txt") &&
                    preferredChefs.Any(x => x.Value == name))
                {
                    diyHats.Add(HatData.Create(name));
                }
            }
            yield break;
        }

        static void FixBodyMeshBug(MetaGameProgress metaGameProgress)
        {
            var avatar = metaGameProgress.AvatarDirectory.Avatars[15];
            foreach (var prefab in new GameObject[] { avatar.FrontendModelPrefab, avatar.ModelPrefab, avatar.UIModelPrefab })
            {
                Mesh mesh = prefab.transform.Find("Mesh/Body").GetComponent<SkinnedMeshRenderer>().sharedMesh;
                if (!fixedMeshes.Add(mesh)) continue;
                BoneWeight[] boneWeights = mesh.boneWeights;
                if (boneWeights.Length <= 256) continue;
                boneWeights[166] = boneWeights[255];
                boneWeights[256] = boneWeights[255];
                mesh.boneWeights = boneWeights;
            }
        }

        static IEnumerator LoadAllDIYChef(MetaGameProgress metaGameProgress)
        {
            var newPreferredChefs = new List<KeyValuePair<string, string>>();
            diyChefs.Clear();
            if (preferredChefs.Length == 0)
                yield break;
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string conflictMessage = string.Empty;
            FixBodyMeshBug(metaGameProgress);

            foreach (var pair in preferredChefs)
            {
                if (metaGameProgress.AvatarDirectory.Avatars.Any(x => x.HeadName == pair.Key))
                {
                    if (!HatData.originalHats.Contains(pair.Value) && !diyHats.Any(x => x.HatName == "Hat_" + pair.Value))
                    {
                        conflictMessage += $"{dialogText["HatInvalid"]}: {pair.Value}\n";
                        newPreferredChefs.Add(new KeyValuePair<string, string>(pair.Key, ""));
                    }
                    else
                    {
                        newPreferredChefs.Add(pair);
                    }
                    continue;
                }
                string path = dir + "/Resources/" + pair.Key;
                if (!Directory.Exists(path))
                    continue;
                if (!File.Exists(path + "/INFO"))
                {
                    conflictMessage += $"{dialogText["MissingINFO"]}: {pair.Key}\n";
                    continue;
                }

                if (!File.Exists(path + "/t_Head.png") || !File.Exists(path + "/m_Head.txt"))
                {
                    conflictMessage += $"{dialogText["MissingTexture"]}: {pair.Key}\n";
                    continue;
                }

                var lines = File.ReadAllLines(path + "/INFO");
                byte id = 255;
                foreach (var line in lines)
                {
                    if (line.StartsWith("ID="))
                        if (!byte.TryParse(line.Substring(3), out id)) id = 255;
                }
                var conflictChef = diyChefs.Find(x => x.id == id);
                if (id == 255)
                    conflictMessage += $"{dialogText["IDConflict"]}: {pair.Key}, 255\n";
                else if (conflictChef != null)
                    conflictMessage += $"{dialogText["IDConflict"]}: {conflictChef.name}, {pair.Key}\n";
                else
                {
                    DIYChefAvatarData avatar;
                    try { avatar = DIYChefAvatarData.Create(pair.Key); }
                    catch (Exception ex)
                    {
                        conflictMessage += pair.Key + ": " + ex.Message + "\n";
                        DIYChefPlugin.Log("Registration failed " + path + ": " + ex);
                        continue;
                    }
                    diyChefs.Add(avatar);
                    if (!HatData.originalHats.Contains(pair.Value) && !diyHats.Any(x => x.HatName == "Hat_" + pair.Value))
                    {
                        conflictMessage += $"{dialogText["HatInvalid"]}: {pair.Value}\n";
                        newPreferredChefs.Add(new KeyValuePair<string, string>(avatar.HeadName, ""));
                    }
                    else
                    {
                        newPreferredChefs.Add(new KeyValuePair<string, string>(avatar.HeadName, pair.Value));
                    }
                }
            }

            if (conflictMessage != string.Empty)
                ShowWarningDialog(conflictMessage.TrimEnd());
            preferredChefs = newPreferredChefs.ToArray();
            yield break;
        }

        public static void SwitchChef(int player, int step, out uint uAvatarID, out byte uDIYAvatarID)
        {
            FastList<User> users = ClientUserSystem.m_Users;
            MetaGameProgress metaGameProgress = GameUtils.GetMetaGameProgress();
            var unlockedAvatars = metaGameProgress.GetUnlockedAvatars();
            var allAvatars = GameUtils.GetAvatarDirectoryData().Avatars;

            int id = (int)users._items[player].SelectedChefAvatar;
            int id1 = userDIYChefID[player];
            string headName = (diyChefs.Find(x => x.id == id1)?.HeadName) ?? allAvatars[id].HeadName;
            int newIndex = preferredChefs.FindIndex_Predicate(x => x.Key == headName);
            uAvatarID = defaultChefID;
            uDIYAvatarID = 255;
            for (int attempts = 0; attempts < preferredChefs.Length; attempts++)
            {
                newIndex = MathUtils.Wrap(newIndex + step, 0, preferredChefs.Length);
                if (unlockedAvatars.Any(x => x.HeadName == preferredChefs[newIndex].Key))
                    break;
                if (diyChefs.Any(x => x.HeadName == preferredChefs[newIndex].Key))
                    break;
            }

            int avatarID = allAvatars.FindIndex_Predicate(x => x.HeadName == preferredChefs[newIndex].Key);
            uAvatarID = avatarID < 0 ? defaultChefID : (uint)avatarID;
            var diyAvatar = diyChefs.Find(x => x.HeadName == preferredChefs[newIndex].Key);
            uDIYAvatarID = diyAvatar == null ? (byte)255 : diyAvatar.id;
        }

        private static void ShowWarningDialog(string message)
        {
            T17DialogBox dialog = T17DialogBoxManager.GetDialog(false);
            if (dialog != null)
            {
                dialog.Initialize("Text.Warning", $"\"{message}\"", "Text.Button.Continue",
                    string.Empty, string.Empty, T17DialogBox.Symbols.Warning, true, true, false);
                dialog.Show();
            }
        }

        public static bool Enabled
        {
            get { return defaultChefID != 127U; }
        }

        public static void SetHideFlagsRecursive(GameObject gameObject, HideFlags hideFlags)
        {
            gameObject.hideFlags = hideFlags;
            foreach (Transform child in gameObject.transform)
                SetHideFlagsRecursive(child.gameObject, hideFlags);
        }
    }
}
