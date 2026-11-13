using System;
using System.Collections.Generic;
using OC2DIYChef;

static class ChefGridPromptRegression
{
    static int checks;
    static void Check(bool value, string reason)
    {
        if (!value) throw new Exception(reason);
        checks++;
    }

    internal static int Run()
    {
        checks = 0;
        Check(ChefGridPromptRules.Detect("XInput Controller", "XInput Device #1", "") == ChefGridPromptStyle.Xbox,
            "An XInput virtual controller uses the identity actually exposed by the game");
        Check(ChefGridPromptRules.Detect("Wireless Controller", "", "PlayStation4WinProfile") == ChefGridPromptStyle.PlayStation &&
            ChefGridPromptRules.Detect("DualSense", null, null) == ChefGridPromptStyle.PlayStation,
            "PlayStation profiles and device names select PS prompts");
        Check(ChefGridPromptRules.Detect("Pro Controller", "Nintendo Switch", "") == ChefGridPromptStyle.Switch &&
            ChefGridPromptRules.Detect("Joy-Con (L)", null, null) == ChefGridPromptStyle.Switch,
            "Switch and Joy-Con identities select Nintendo prompts");
        Check(ChefGridPromptRules.Detect("Xbox Elite Pro Controller", "", "") == ChefGridPromptStyle.Xbox &&
            ChefGridPromptRules.Detect(null, null, null) == ChefGridPromptStyle.Xbox, "Generic identities have a stable Xbox fallback");
        Check(ChefGridPromptRules.Key(ChefGridPromptStyle.PlayStation, "A") == "×" &&
            ChefGridPromptRules.Key(ChefGridPromptStyle.PlayStation, "B") == "○" &&
            ChefGridPromptRules.IconField(ChefGridPromptStyle.PlayStation, "A") == "Action1",
            "Normalized south/east actions use the matching PlayStation symbols and native icon fields");
        Check(ChefGridPromptRules.Key(ChefGridPromptStyle.Switch, "A") == "B" &&
            ChefGridPromptRules.Key(ChefGridPromptStyle.Switch, "B") == "A" &&
            ChefGridPromptRules.IconField(ChefGridPromptStyle.Switch, "A") == "B",
            "Switch prompts preserve the physical location of the actual PC mapped button");
        Check(ChefGridPromptRules.Key(ChefGridPromptStyle.PlayStation, "LB") == "L1" &&
            ChefGridPromptRules.Key(ChefGridPromptStyle.Switch, "RB") == "R" &&
            ChefGridPromptRules.IconField(ChefGridPromptStyle.PlayStation, "RB") == "RightBumper",
            "Split-controller shoulder bindings retain their own prompts");

        var lookup = new ControllerIconLookup();
        var prompts = new ChefGridButtonPrompts();
        var pad = new PlayerGameInput { Pad = ControlPadInput.PadNum.One };
        var keyboard = new PlayerGameInput { Pad = ControlPadInput.PadNum.Two, Keyboard = true };
        var accept = new MappedPromptButton(ControlPadInput.Button.A);
        var back = new MappedPromptButton(ControlPadInput.Button.B);
        PCPadInputProvider.Set(pad.Pad, "XInput Controller");
        UnityEngine.Time.unscaledTime = 0;
        prompts.Refresh(pad, accept, back);
        Check(prompts.Accept.Icon.name == "Xbox A" && prompts.Back.Icon.name == "Xbox B", "Xbox uses the native public sprite lookup");
        prompts.Refresh(keyboard, accept, back);
        Check(prompts.Accept.Icon == null && prompts.Accept.Key == "Enter" && prompts.Back.Key == "Esc",
            "Switching to the second keyboard player updates prompts immediately rather than checking player one");
        prompts.Refresh(pad, accept, back);
        Check(prompts.Accept.Icon.name == "Xbox A", "Switching back restores the current player's controller prompt immediately");
        PCPadInputProvider.Set(pad.Pad, "PlayStation 4 Controller");
        UnityEngine.Time.unscaledTime = 1;
        prompts.Refresh(pad, accept, back);
        Check(prompts.Accept.Icon.name == "PS Cross" && prompts.Back.Icon.name == "PS Circle", "PS sprites come from the original serialized platform pack");
        PlayerManagerShared<PCPlayerManager.PCPlayerProfile>.AcceptAndCancelButtonsInverted = true;
        prompts.Refresh(pad, back, accept);
        Check(prompts.Accept.Icon.name == "PS Circle" && prompts.Back.Icon.name == "PS Cross", "PS prompts follow inverted physical confirm/cancel bindings");
        PlayerManagerShared<PCPlayerManager.PCPlayerProfile>.AcceptAndCancelButtonsInverted = false;
        pad.ConfirmOverride = ControlPadInput.Button.RB;
        prompts.Refresh(pad, new MappedPromptButton(ControlPadInput.Button.RB), back);
        Check(prompts.Accept.Icon.name == "PS R1", "A PS split controller uses its original shoulder-button sprite");
        pad.ConfirmOverride = null;
        lookup.Platforms.PS4 = null;
        UnityEngine.Time.unscaledTime = 2;
        prompts.Refresh(pad, accept, back);
        Check(prompts.Accept.Icon == null && prompts.Accept.Key == "×" && prompts.Back.Key == "○",
            "Missing platform sprites fall back to readable matching symbols, without using Xbox icons");
        PCPadInputProvider.Set(pad.Pad, "Nintendo Switch Pro Controller");
        UnityEngine.Time.unscaledTime = 3;
        prompts.Refresh(pad, accept, back);
        Check(prompts.Accept.Icon.name == "Switch B" && prompts.Back.Icon.name == "Switch A", "Switch sprites respect normalized physical button positions");
        PlayerManagerShared<PCPlayerManager.PCPlayerProfile>.AcceptAndCancelButtonsInverted = true;
        PCPadInputProvider.Set(pad.Pad, "XInput Controller");
        prompts.Refresh(pad, back, accept);
        Check(prompts.Accept.Icon.name == "Xbox B" && prompts.Back.Icon.name == "Xbox A",
            "Inverted actual bindings do not get inverted a second time by the native sprite lookup");
        pad.ConfirmOverride = ControlPadInput.Button.RB;
        prompts.Refresh(pad, new MappedPromptButton(ControlPadInput.Button.RB), back);
        Check(prompts.Accept.Icon.name == "Xbox RB", "A split-pad binding change refreshes even within the polling interval");
        pad.ConfirmOverride = null;
        prompts.Refresh(pad, new MappedPromptButton(ControlPadInput.Button.Start), back);
        Check(prompts.Accept.Icon.name == "Xbox B", "The UISelect Start alternative does not replace the primary confirmation prompt");
        PlayerManagerShared<PCPlayerManager.PCPlayerProfile>.AcceptAndCancelButtonsInverted = false;
        prompts.Reset();
        Check(prompts.Accept.Icon == null && prompts.Back.Icon == null, "Closing the grid drops borrowed sprite references without destroying shared assets");
        ControllerIconLookup.Instance = null;
        prompts.Refresh(pad, accept, back);
        Check(prompts.Accept.Icon == null && prompts.Accept.Key == "A" && prompts.Back.Key == "B",
            "Unavailable native UI assets retain usable controller labels");
        return checks;
    }
}

// Minimal native API stand-ins; the resolver under test is the production implementation.
static class ControlPadInput
{
    internal enum PadNum { One, Two }
    internal enum Button { A, B, X, Y, RB, Start }
}
sealed class PlayerGameInput
{
    internal ControlPadInput.PadNum Pad;
    internal bool Keyboard;
    internal ControlPadInput.Button? ConfirmOverride;
}
static class PlayerInputLookup
{
    internal enum LogicalButtonID { UISelectNotStart }
    internal static ILogicalButton GetFixedButton(LogicalButtonID id, PlayerGameInput input)
    {
        return new MappedPromptButton(input.ConfirmOverride ??
            (PlayerManagerShared<PCPlayerManager.PCPlayerProfile>.AcceptAndCancelButtonsInverted ? ControlPadInput.Button.B : ControlPadInput.Button.A));
    }
}
static class KeyboardUtils { internal static bool IsKeyboard(PlayerGameInput input) { return input.Keyboard; } }
sealed class StandardActionSet { internal InControl.InputDevice Device; }
static class PCPadInputProvider
{
    static readonly Dictionary<ControlPadInput.PadNum, StandardActionSet> devices = new Dictionary<ControlPadInput.PadNum, StandardActionSet>();
    static StandardActionSet GetActionSet(ControlPadInput.PadNum pad) { return devices[pad]; }
    internal static void Set(ControlPadInput.PadNum pad, string name)
    { devices[pad] = new StandardActionSet { Device = new InControl.InputDevice { Name = name } }; }
}
sealed class MappedPromptButton : ILogicalButton
{
    internal readonly ControlPadInput.Button Button;
    internal MappedPromptButton(ControlPadInput.Button button) { Button = button; }
    public bool IsDown() { return false; }
    public bool HasUnclaimedPressEvent() { return false; }
    public bool HasUnclaimedReleaseEvent() { return false; }
    public void ClaimPressEvent() { }
    public void ClaimReleaseEvent() { }
}
static class PlayerButtonImage
{
    internal static T? GetControlPadButton<T>(ILogicalButton logical, ControllerIconLookup.DeviceContext device) where T : struct
    { return logical is MappedPromptButton ? (T)(object)((MappedPromptButton)logical).Button : (T?)null; }
}
static class PlayerManagerShared<T> { internal static bool AcceptAndCancelButtonsInverted; }
static class PCPlayerManager { internal sealed class PCPlayerProfile { } }
sealed class ControllerIconLookup
{
    internal enum IconContext { Bordered }
    internal enum DeviceContext { Pad }
    internal sealed class Pack
    {
        public UnityEngine.Sprite Action1, Action2, RightBumper, A, B;
    }
    internal sealed class PlatformSet { public Pack PS4, NX; }
    readonly PlatformSet m_borderedIcons = new PlatformSet {
        PS4 = new Pack { Action1 = new UnityEngine.Sprite("PS Cross"), Action2 = new UnityEngine.Sprite("PS Circle"),
            RightBumper = new UnityEngine.Sprite("PS R1") },
        NX = new Pack { A = new UnityEngine.Sprite("Switch A"), B = new UnityEngine.Sprite("Switch B") } };
    internal static ControllerIconLookup Instance;
    internal PlatformSet Platforms { get { return m_borderedIcons; } }
    internal ControllerIconLookup() { Instance = this; }
    internal UnityEngine.Sprite GetIcon(ControlPadInput.Button button, IconContext context, DeviceContext device)
    {
        if (PlayerManagerShared<PCPlayerManager.PCPlayerProfile>.AcceptAndCancelButtonsInverted)
            button = button == ControlPadInput.Button.A ? ControlPadInput.Button.B :
                button == ControlPadInput.Button.B ? ControlPadInput.Button.A : button;
        return new UnityEngine.Sprite("Xbox " + button);
    }
}
namespace InControl
{
    class InputDevice { internal string Name; internal string Meta { get { return ""; } } }
    sealed class UnityInputDevice : InputDevice { internal object Profile { get { return null; } } }
}
namespace UnityEngine { sealed class Sprite { internal readonly string name; internal Sprite(string value) { name = value; } } }
