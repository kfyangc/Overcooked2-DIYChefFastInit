namespace OC2DIYChef
{
    internal enum ChefGridPromptStyle { Keyboard, Xbox, PlayStation, Switch }

    internal static class ChefGridPromptRules
    {
        internal static ChefGridPromptStyle Detect(string name, string meta, string profile)
        {
            string identity = ((name ?? "") + " " + (meta ?? "") + " " + (profile ?? "")).ToLowerInvariant();
            if (identity.Contains("playstation") || identity.Contains("dualshock") || identity.Contains("dualsense") ||
                identity.Contains("sony") || identity.Contains("ps3") || identity.Contains("ps4") || identity.Contains("ps5"))
                return ChefGridPromptStyle.PlayStation;
            if (identity.Contains("nintendo") || identity.Contains("switch") || identity.Contains("joy-con") ||
                identity.Contains("joycon") || (identity.Contains("pro controller") &&
                !identity.Contains("xbox") && !identity.Contains("xinput"))) return ChefGridPromptStyle.Switch;
            // XInput/Steam virtual devices expose Xbox controls when their real identity is hidden.
            return ChefGridPromptStyle.Xbox;
        }

        internal static string Key(ChefGridPromptStyle style, string button)
        {
            if (style == ChefGridPromptStyle.PlayStation)
                switch (button)
                {
                    case "A": return "×";
                    case "B": return "○";
                    case "X": return "□";
                    case "Y": return "△";
                    case "LB": return "L1";
                    case "RB": return "R1";
                    case "LTrigger": return "L2";
                    case "RTrigger": return "R2";
                    case "Start": return "Options";
                    case "Back": return "Share";
                }
            if (style == ChefGridPromptStyle.Switch)
                switch (button)
                {
                    // StandardActionSet binds A/B/X/Y to the normalized south/east/west/north actions.
                    case "A": return "B";
                    case "B": return "A";
                    case "X": return "Y";
                    case "Y": return "X";
                    case "LB": return "L";
                    case "RB": return "R";
                    case "LTrigger": return "ZL";
                    case "RTrigger": return "ZR";
                    case "Start": return "+";
                    case "Back": return "−";
                }
            switch (button)
            {
                case "DPadUp": return "↑";
                case "DPadDown": return "↓";
                case "DPadLeft": return "←";
                case "DPadRight": return "→";
                case "LeftAnalog": return style == ChefGridPromptStyle.PlayStation ? "L3" : "LS";
                case "RightAnalog": return style == ChefGridPromptStyle.PlayStation ? "R3" : "RS";
                case "LTrigger": return "LT";
                case "RTrigger": return "RT";
                default: return button ?? "";
            }
        }

        internal static string IconField(ChefGridPromptStyle style, string button)
        {
            switch (button)
            {
                case "A": return style == ChefGridPromptStyle.Switch ? "B" : "Action1";
                case "B": return style == ChefGridPromptStyle.Switch ? "A" : "Action2";
                case "X": return style == ChefGridPromptStyle.Switch ? "Y" : "Action3";
                case "Y": return style == ChefGridPromptStyle.Switch ? "X" : "Action4";
                case "LB": return "LeftBumper";
                case "RB": return "RightBumper";
                case "LTrigger": return "LeftTrigger";
                case "RTrigger": return "RightTrigger";
                case "LeftAnalog": return "LeftStick";
                case "RightAnalog": return "RightStick";
                default: return button ?? "";
            }
        }
    }
}
