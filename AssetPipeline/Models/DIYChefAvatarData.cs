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
    public class DIYChefAvatarData : ChefAvatarData
    {
        public byte id = 255;
        internal ChefAvatarData template;
        internal ResourceCache Resources;
        Dictionary<string, Texture2D> textureDict { get { return Resources.Textures; } }
        Dictionary<string, ObjImporter.MeshData> meshDict { get { return Resources.Geometry; } }
        Dictionary<string, Dictionary<string, float>> materialDict { get { return Resources.Materials; } }

        private static readonly Dictionary<string, string> allowedPartToBone = new Dictionary<string, string>
        {
            { "Eyes", "Eyes" },
            { "Eyebrows", "Eyebrows" },
            { "Eyes2_Blinks", "Eyes2_Blinks" },
            { "Hand_Grip_L", "LeftHand" },
            { "Hand_Grip_R", "RightHand" },
            { "Hand_Open_L", "LeftHand" },
            { "Hand_Open_R", "RightHand" },
            { "Tail", "Jnt_Tail" },
            { "Head", "Head" },
            { "Head1", "Head" },
            { "Head2", "Head" },
            { "Body_NeckTie", "NeckTie" },
            { "Body_Top", "Body_Top" },
            { "Body_Bottom", "Jnt_Body" },
            { "Body_Tail", "Jnt_Tail" },
            { "Body_Body", "" },
            { "Wheelchair", "Jnt_Wheelchair" },
            { "Knife", "HeldItem" }, // Cleaver
        };

        internal static bool AllowsPart(string name) { return allowedPartToBone.ContainsKey(name); }

        public static DIYChefAvatarData Create(string name)
        {
            var chefAvatarData = CreateInstance<DIYChefAvatarData>();
            chefAvatarData.Init(name);
            return chefAvatarData;
        }

        void Init(string name)
        {
            string chefName = name.StartsWith("Chef_") ? name : "Chef_" + name;
            this.name = chefName;
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = dir + "/Resources/" + name;
            Resources = new ResourceCache(chefName, path, false);
            Resources.Chef = this;
            template = DIYChefCustomisation.defaultTemplate;

            var lines = File.ReadAllLines(path + "/INFO");
            foreach (var line in lines)
            {
                if (line.StartsWith("ID=") && byte.TryParse(line.Substring(3), out byte id))
                    this.id = id;
                if (line.StartsWith("BODY="))
                {
                    var bodyName = line.Substring(5);
                    var allAvatars = GameUtils.GetAvatarDirectoryData().Avatars;
                    int i = allAvatars.FindIndex_Predicate(x => x.HeadName == bodyName);
                    if (i >= 0) template = allAvatars[i];
                }
            }

            HeadName = chefName;
            ColourisationMode = template.ColourisationMode;
        }

        internal GameObject GetPrefab(ChefMeshReplacer.ChefModelType type)
        {
            return type == ChefMeshReplacer.ChefModelType.FrontEnd ? FrontendModelPrefab :
                type == ChefMeshReplacer.ChefModelType.UI ? UIModelPrefab : ModelPrefab;
        }

        internal void ClearPrefabs()
        {
            FrontendModelPrefab = UIModelPrefab = ModelPrefab = null;
        }

        internal IEnumerator CreatePrefabModel(ChefMeshReplacer.ChefModelType modelType)
        {
            GameObject templatePrefab =
                modelType == ChefMeshReplacer.ChefModelType.FrontEnd ? template.FrontendModelPrefab : (
                modelType == ChefMeshReplacer.ChefModelType.UI ? template.UIModelPrefab :
                template.ModelPrefab);
            string postfix =
                modelType == ChefMeshReplacer.ChefModelType.FrontEnd ? "_FrontEnd" : (
                modelType == ChefMeshReplacer.ChefModelType.UI ? "_UI" : "");

            GameObject prefab = GameObject.Instantiate(templatePrefab);
            DIYChefCustomisation.SetHideFlagsRecursive(prefab, HideFlags.HideAndDontSave);
            prefab.SetActive(false);
            prefab.SetObjectLayer(templatePrefab.layer);
            prefab.name = name + postfix;
            prefab.transform.SetParent(null, false);
            Resources.Own(prefab, 4096);
            yield return null;

            Transform tMesh = prefab.transform.Find("Mesh");
            for (int i = tMesh.childCount - 1; i >= 0; i--)
            {
                var child = tMesh.GetChild(i);
                if (child.name.StartsWith("Chef_") && !child.name.Equals(template.HeadName))
                    GameObject.DestroyImmediate(child.gameObject);
            }

            SkinnedMeshRenderer head = tMesh.Find(template.HeadName).GetComponent<SkinnedMeshRenderer>();
            head.gameObject.name = name;
            SkinnedMeshRenderer body = tMesh.Find("Body").GetComponent<SkinnedMeshRenderer>();
            SkinnedMeshRenderer[] knife;
            if (modelType == ChefMeshReplacer.ChefModelType.FrontEnd)
                knife = new SkinnedMeshRenderer[1] {
                    tMesh.Find("Knife").GetComponent<SkinnedMeshRenderer>(),
                };
            else if (modelType == ChefMeshReplacer.ChefModelType.InGame)
                knife = new SkinnedMeshRenderer[3] {
                    tMesh.Find("Cleaver").GetComponent<SkinnedMeshRenderer>(),
                    tMesh.Find("Hatchet").GetComponent<SkinnedMeshRenderer>(),
                    tMesh.Find("Hammer").GetComponent<SkinnedMeshRenderer>(),
                };
            else
                knife = new SkinnedMeshRenderer[0];

            foreach (string partName in meshDict.Keys)
            {
                SkinnedMeshRenderer[] allParts;
                string[] boneNames = partName == "Body_Body" ?
                    new string[] { "NeckTie", "Body_Top", "Jnt_Body", "Jnt_Tail" } :
                    new string[] { allowedPartToBone[partName] };
                bool isBody = partName.StartsWith("Body_");

                if (partName.Equals("Knife")) allParts = knife;
                else
                {
                    Transform tPart = partName.Equals("Head") ? head.transform : head.transform.FindChildStartsWithRecursive(partName);
                    if (tPart == null)
                    {
                        if (partName.StartsWith("Hand")) continue;
                        SkinnedMeshRenderer partTemplate = isBody ? body : head;
                        GameObject insPart = GameObject.Instantiate(partTemplate.gameObject);
                        insPart.hideFlags = HideFlags.HideAndDontSave;
                        // Detach before deferred destruction, so same-frame cloning is safe.
                        while (insPart.transform.childCount != 0)
                        {
                            var child = insPart.transform.GetChild(0);
                            child.SetParent(null, false);
                            child.gameObject.SetActive(false);
                            GameObject.Destroy(child.gameObject);
                        }
                        insPart.SetObjectLayer(partTemplate.gameObject.layer);
                        insPart.name = partName;
                        insPart.transform.SetParent(isBody ? body.transform.parent : head.transform, false);
                        insPart.transform.localPosition = Vector3.zero;
                        insPart.transform.localRotation = Quaternion.identity;
                        insPart.transform.localScale = Vector3.one;
                        Transform[] bones = insPart.GetComponent<SkinnedMeshRenderer>().bones;
                        foreach (var boneName in boneNames)
                            if (!bones.Any(x => x.name.Equals(boneName)))
                            {
                                Transform bone = prefab.transform.Find("Skeleton").FindChildRecursive(boneName);
                                bones = bones.AddToArray(bone);
                            }
                        insPart.GetComponent<SkinnedMeshRenderer>().bones = bones;
                        tPart = insPart.transform;
                    }
                    allParts = new SkinnedMeshRenderer[1] { tPart.GetComponent<SkinnedMeshRenderer>() };
                }

                foreach (var part in allParts)
                {
                    //part.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                    Material material = Resources.Own(new Material(part.sharedMaterial), 512);
                    part.sharedMaterial = material;
                    if (textureDict.ContainsKey("t_" + partName))
                        material.SetTexture("_DiffuseMap", textureDict["t_" + partName]);
                    else if (textureDict.ContainsKey("t_Body") && isBody)
                        material.SetTexture("_DiffuseMap", textureDict["t_Body"]);
                    else
                        material.SetTexture("_DiffuseMap", textureDict["t_Head"]);
                    var materialFloat =
                        materialDict.ContainsKey("m_" + partName) ? materialDict["m_" + partName] : (
                        materialDict.ContainsKey("m_Body") && isBody ? materialDict["m_Body"] :
                        materialDict["m_Head"]);
                    foreach (var pair in materialFloat)
                        material.SetFloat(pair.Key, pair.Value);
                }
                yield return null;
            }

            if (meshDict.ContainsKey("Body_Body"))
                GameObject.DestroyImmediate(body.gameObject);

            foreach (string partName in meshDict.Keys)
            {
                SkinnedMeshRenderer[] allParts;
                string boneName = allowedPartToBone[partName];
                if (partName.Equals("Knife")) allParts = knife;
                else
                {
                    Transform tPart = partName.Equals("Head") ? head.transform : tMesh.FindChildStartsWithRecursive(partName);
                    if (tPart == null) continue;
                    allParts = new SkinnedMeshRenderer[1] { tPart.GetComponent<SkinnedMeshRenderer>() };
                }

                foreach (var part in allParts)
                {
                    string[] names = partName == "Body_Body" ?
                        new[] { "Body_Top", "Jnt_Body" } : new[] { boneName };
                    Transform[] bones = names.Select(n => part.bones.FirstOrDefault(x => x != null && x.name == n)).ToArray();
                    if (bones.Any(x => x == null))
                        throw new FormatException(partName + ": missing bones " + string.Join(",", names));
                    MeshBinding.Bind(Resources, meshDict[partName], part, partName, bones, partName == "Body_Body");
                }
                yield return null;
            }
            // Publish only a complete, bound model.
            if (modelType == ChefMeshReplacer.ChefModelType.FrontEnd) FrontendModelPrefab = prefab;
            else if (modelType == ChefMeshReplacer.ChefModelType.UI) UIModelPrefab = prefab;
            else ModelPrefab = prefab;
        }
    }
}
