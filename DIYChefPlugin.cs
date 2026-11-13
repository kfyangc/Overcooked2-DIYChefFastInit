using BepInEx;
using HarmonyLib;
using System.Collections;

namespace OC2DIYChef
{
    [BepInPlugin("dev.gua.overcooked.diychef", "厨房暖暖 快速载入 / 厨房暖暖 启动速度优化 / DIYChef FastInit", PluginVersion.Value)]
    [BepInProcess("Overcooked2.exe")]
    public class DIYChefPlugin : BaseUnityPlugin
    {
        static DIYChefPlugin pluginInstance;
        static Harmony patcher;

        public void Awake()
        {
            pluginInstance = this;
            ChefLoading.Configure(Config);
            ChefGridMenu.Configure(Config);
            Log("Version " + PluginVersion.Value + "; " + typeof(DIYChefPlugin).Assembly.Location);
            patcher = new Harmony("dev.gua.overcooked.diychef");
            patcher.PatchAll(typeof(Patch));
            patcher.PatchAll(typeof(Patch.FrontendChefCustomisationPatch));
            patcher.PatchAll(typeof(ChefGridPatches));
            patcher.PatchAll(typeof(ChefGridLobbyPatches));
            patcher.PatchAll(typeof(ChefGridPlayerMenu));
            OnlinePatch.Patch(patcher);
            Patch.PatchInternal(patcher);
            foreach (var patched in patcher.GetPatchedMethods())
                Log("Patched: " + patched.FullDescription());
        }

        void Start()
        {
            ModelTestCompatibility.Initialize();
        }

        void Update()
        {
            ChefLoading.Update();
        }

        void LateUpdate()
        {
            ChefGridLobby.UpdateFrame();
            ModelTestCompatibility.LateUpdate();
        }

        void OnDestroy()
        {
            ChefGridLobby.ShutdownActive();
            if (ChefGridMenu.Active != null) ChefGridMenu.Active.CloseGrid();
            ChefGridEngagementGuard.Shutdown();
            ModelTestCompatibility.Shutdown();
            ChefLoading.Shutdown();
        }

        public static void Log(string msg) { pluginInstance.Logger.LogInfo(msg); }
        internal static void Warn(string msg) { pluginInstance.Logger.LogWarning(msg); }
    }
}
