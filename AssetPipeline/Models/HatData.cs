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
    public class HatData : ScriptableObject
    {
        internal GameObject prefab;
        internal ResourceCache Resources;
        string sourceName;
        public string HatName;
        public static readonly string[] originalHats = new string[] { "", "None", "Santa", "Fancy", "Baseballcap" };
        public const int hatLayer = 6;

        public static HatData Create(string name)
        {
            HatData hatData = CreateInstance<HatData>();
            hatData.Init(name);
            return hatData;
        }

        void Init(string name)
        {
            string hatName = name.StartsWith("Hat_") ? name : "Hat_" + name;
            this.name = hatName;
            HatName = hatName;

            sourceName = name;
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Resources = new ResourceCache(hatName, dir + "/Resources/HATS/" + name, true);
            Resources.Hat = this;
            if (name == "Baseballcap" || name == "Santa")
                prefab = DIYChefCustomisation.defaultTemplate.ModelPrefab.transform
                    .FindChildStartsWithRecursive(hatName).gameObject;
        }

        internal IEnumerator Build()
        {
            string name = sourceName;
            string hatName = HatName;
            Texture2D texture = Resources.Textures["t_" + name];
            var matParams = Resources.Materials["m_" + name];
            GameObject hatPrefab = DIYChefCustomisation.defaultTemplate.ModelPrefab.transform.FindChildStartsWithRecursive("Hat_Baseballcap").gameObject;
            var built = Resources.Own(GameObject.Instantiate(hatPrefab), 4096);
            DIYChefCustomisation.SetHideFlagsRecursive(built, HideFlags.HideAndDontSave);
            built.SetActive(false);
            built.SetObjectLayer(hatPrefab.layer);
            built.name = hatName;
            built.transform.SetParent(null, false);

            SkinnedMeshRenderer hat = built.GetComponent<SkinnedMeshRenderer>();
            Material material = Resources.Own(new Material(hat.sharedMaterial), 512);
            hat.sharedMaterial = material;
            material.SetTexture("_DiffuseMap", texture);
            foreach (var pair in matParams)
                material.SetFloat(pair.Key, pair.Value);

            var hatBone = hat.bones.FirstOrDefault(x => x != null && x.name == "HatBase");
            if (hatBone == null) throw new FormatException(HatName + ": missing bone HatBase");
            MeshBinding.Bind(Resources, Resources.Geometry[name], hat, HatName, new[] { hatBone }, false);
            prefab = built;
            yield break;
        }

        public static void SetChefHat(GameObject chef, ref HatMeshVisibility.VisState _hat)
        {
            if (DIYChefCustomisation.preferredChefs == null) return;
            ChefMeshReplacer replacer = chef.GetComponent<ChefMeshReplacer>();
            if (replacer == null) return;
            string headName = replacer.get_m_currentHeadName();
            int index = DIYChefCustomisation.preferredChefs.FindIndex_Predicate(x => x.Key == headName);
            string hatName = index < 0 ? "" : DIYChefCustomisation.preferredChefs[index].Value;
            if (hatName != "")
                _hat =
                    !originalHats.Contains(hatName) ? HatMeshVisibility.VisState.None : (
                    hatName == "Baseballcap" ? HatMeshVisibility.VisState.Baseball : (
                    hatName == "Santa" ? HatMeshVisibility.VisState.Festive : (
                    hatName == "Fancy" ? HatMeshVisibility.VisState.Fancy : HatMeshVisibility.VisState.None)));

            Transform parent = chef.transform.FindChildRecursive("Mesh");
            if (parent == null) return;
            bool isUI = chef.GetComponent<HatMeshVisibility>() == null;
            GameObject hat = parent.FindChildRecursive("Hat_" + hatName)?.gameObject;
            if (hat == null && DIYChefCustomisation.diyHats.Find(x => x.HatName == "Hat_" + hatName) != null)
            {
                var data = DIYChefCustomisation.diyHats.Find(x => x.HatName == "Hat_" + hatName);
                if (data.prefab == null) return;
                hat = GameObject.Instantiate(data.prefab);
                ResourceUse.Attach(hat, data.Resources);
                hat.SetObjectLayer(parent.gameObject.layer);
                hat.name = "Hat_" + hatName;
                hat.transform.SetParent(parent, false);
                var renderer = hat.GetComponent<SkinnedMeshRenderer>();
                renderer.rootBone = chef.transform.FindChildRecursive("HatBase");
                renderer.bones = renderer.bones.Select(x => chef.transform.FindChildRecursive(x.name)).ToArray();
                if (isUI)
                {
                    Material material = renderer.material;
                    material.shader = Shader.Find("Overcooked_2/OC2_Character_Clothes_UI");
                }
            }

            if (isUI && hat != null && hatName != "Fancy")
                hat.SetObjectLayer(hatLayer);

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name.StartsWith("Hat_"))
                    child.gameObject.SetActive(child.name.Substring(4) == hatName);
            }
            if (hatName == "")
            {
                parent.FindChildRecursive("Hat_Fancy")?.gameObject.SetActive(true);
                parent.FindChildRecursive("Hat_Baseballcap")?.gameObject.SetActive(!isUI);
                parent.FindChildRecursive("Hat_Santa")?.gameObject.SetActive(!isUI);
            }
        }
    }
}
