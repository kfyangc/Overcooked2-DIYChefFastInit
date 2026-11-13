using HarmonyLib;
using System.Reflection;

namespace OC2DIYChef
{
    internal static class ChefGridPauseGuard
    {
        static readonly FieldInfo starts = AccessTools.Field(typeof(PauseMenuManager), "m_startButtons");
        static readonly FieldInfo session = AccessTools.Field(typeof(PauseMenuManager), "m_sessionData");
        static readonly FieldInfo[] sessionButtons = Buttons();

        static FieldInfo[] Buttons()
        {
            var type = AccessTools.Inner(typeof(PauseMenuManager), "SessionData");
            return new[] { AccessTools.Field(type, "m_uiUpButton"), AccessTools.Field(type, "m_uiDownButton"),
                AccessTools.Field(type, "m_uiSelectButton"), AccessTools.Field(type, "m_uiCancelButton") };
        }

        internal static void BeforeUpdate(PauseMenuManager manager, bool blocking)
        {
            if (!blocking || manager == null) return;
            var buttons = (ILogicalButton[])starts.GetValue(manager);
            if (buttons != null) foreach (var button in buttons) Claim(button);
            var data = session.GetValue(manager);
            if (data != null) foreach (var field in sessionButtons) Claim((ILogicalButton)field.GetValue(data));
        }

        static void Claim(ILogicalButton button)
        {
            if (button == null) return;
            // Native pause closes on the cancel release, so consume both edges on
            // its actual button instances while retaining their current histories.
            button.HasUnclaimedPressEvent(); button.ClaimPressEvent();
            button.HasUnclaimedReleaseEvent(); button.ClaimReleaseEvent();
        }
    }
}
