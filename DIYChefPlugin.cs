using BepInEx;
using HarmonyLib;
using System.Collections;

namespace OC2DIYChef
{
    [BepInPlugin("dev.gua.overcooked.diychef", "Overcooked2 DIYChef Plugin", PluginVersion.Value)]
    [BepInProcess("Overcooked2.exe")]
    public class DIYChefPlugin : BaseUnityPlugin
    {
        static DIYChefPlugin pluginInstance;
        static Harmony patcher;

        public void Awake()
        {
            pluginInstance = this;
            ChefLoading.Configure(Config);
            Log("Version " + PluginVersion.Value + "; " + typeof(DIYChefPlugin).Assembly.Location);
            patcher = new Harmony("dev.gua.overcooked.diychef");
            patcher.PatchAll(typeof(Patch));
            patcher.PatchAll(typeof(Patch.FrontendChefCustomisationPatch));
            OnlinePatch.Patch(patcher);
            Patch.PatchInternal(patcher);
            foreach (var patched in patcher.GetPatchedMethods())
                Log("Patched: " + patched.FullDescription());
        }

        void Update()
        {
            ChefLoading.Update();
        }

        void OnDestroy() { ChefLoading.Shutdown(); }

        public static void Log(string msg) { pluginInstance.Logger.LogInfo(msg); }
    }
}
