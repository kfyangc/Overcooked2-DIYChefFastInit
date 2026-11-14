using HarmonyLib;
using InControl;
using System.Reflection;
using UnityEngine;

namespace OC2DIYChef
{
    internal struct ChefGridButtonPrompt
    {
        internal Sprite Icon;
        internal string Key;
    }

    internal sealed class ChefGridButtonPrompts
    {
        static readonly MethodInfo getActionSet = AccessTools.Method(typeof(PCPadInputProvider), "GetActionSet",
            new[] { typeof(ControlPadInput.PadNum) });
        static readonly MethodInfo sidedButton = AccessTools.Method(typeof(PlayerInputLookup), "GetSidedLogicalCombo",
            new[] { typeof(AmbiPadButton), typeof(PlayerGameInput) });
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PlayerGameInput previousInput;
        ILogicalButton previousAccept, previousCancel;
        float refreshAt;
        internal ChefGridButtonPrompt Accept, Back;

        internal void Refresh(PlayerGameInput input, ILogicalButton accept, ILogicalButton cancel)
        {
            if (previousInput == input && previousAccept == accept && previousCancel == cancel && Time.unscaledTime < refreshAt) return;
            previousInput = input;
            previousAccept = accept;
            previousCancel = cancel;
            refreshAt = Time.unscaledTime + 0.5f;
            if (input == null || KeyboardUtils.IsKeyboard(input))
            {
                if (input != null && input.Side != PadSide.Both)
                {
                    Accept = KeyboardPrompt(input, Binding(input, true));
                    Back = KeyboardPrompt(input, Binding(input, false));
                    return;
                }
                // The unsplit keyboard still has these explicit grid fallback keys.
                Accept = new ChefGridButtonPrompt { Key = "Enter" };
                Back = new ChefGridButtonPrompt { Key = "Esc" };
                return;
            }
            var style = Style(input);
            var lookup = GameUtils.RequestManager<ControllerIconLookup>();
            // UISelect also contains Start; show the primary confirm binding rather
            // than whichever node a composite input graph happens to enumerate first.
            var primaryConfirm = PlayerInputLookup.GetFixedButton(PlayerInputLookup.LogicalButtonID.UISelectNotStart, input);
            Accept = Resolve(lookup, style, primaryConfirm, ControlPadInput.Button.A);
            Back = Resolve(lookup, style, cancel, ControlPadInput.Button.B);
        }

        internal static ILogicalButton Binding(PlayerGameInput input, bool confirm)
        {
            if (input == null) return null;
            if (KeyboardUtils.IsKeyboard(input) && input.Side != PadSide.Both)
                return (ILogicalButton)sidedButton.Invoke(null,
                    new object[] { confirm ? AmbiPadButton.Confirm : AmbiPadButton.Cancel, input });
            return PlayerInputLookup.GetFixedButton(confirm ? PlayerInputLookup.LogicalButtonID.UISelect :
                PlayerInputLookup.LogicalButtonID.UICancel, input);
        }

        static ChefGridButtonPrompt KeyboardPrompt(PlayerGameInput input, ILogicalButton logical)
        {
            InputDevice device;
            var binding = KeyboardBinding(input, logical, out device);
            return new ChefGridButtonPrompt { Key = binding == null ? "—" : binding.Name };
        }

        internal static bool Down(PlayerGameInput input, ILogicalButton logical)
        {
            if (input != null && KeyboardUtils.IsKeyboard(input) && input.Side != PadSide.Both)
            {
                // Read the very same keyboard combo advertised by the prompt, rather
                // than the virtual pad action's cached State. Keep chords/remaps intact.
                InputDevice device;
                var binding = KeyboardBinding(input, logical, out device);
                return binding != null && binding.GetState(device);
            }
            return logical != null && logical.IsDown();
        }

        static KeyBindingSource KeyboardBinding(PlayerGameInput input, ILogicalButton logical, out InputDevice device)
        {
            // A sided input graph contains LogicalPadButton nodes even for a keyboard.
            // Resolve that physical action first, then use its actual InControl key
            // binding. Generic keyboard icon packs cannot represent custom remaps.
            var button = logical == null ? null : PlayerButtonImage.GetControlPadButton<ControlPadInput.Button>(
                logical, ControllerIconLookup.DeviceContext.Pad);
            var actions = getActionSet == null ? null : (StandardActionSet)getActionSet.Invoke(null, new object[] { input.Pad });
            device = actions == null ? null : actions.Device;
            if (button.HasValue && actions != null)
            {
                PlayerAction action;
                if (actions.ButtonActions.TryGetValue(button.Value, out action))
                    foreach (var binding in action.Bindings)
                        if (binding is KeyBindingSource && !string.IsNullOrEmpty(binding.Name))
                            return (KeyBindingSource)binding;
            }
            return null;
        }

        static ChefGridPromptStyle Style(PlayerGameInput input)
        {
            var actions = getActionSet == null ? null : (StandardActionSet)getActionSet.Invoke(null, new object[] { input.Pad });
            var device = actions == null ? null : actions.Device;
            var unityDevice = device as UnityInputDevice;
            string profile = unityDevice == null || unityDevice.Profile == null ? "" : unityDevice.Profile.GetType().Name;
            return ChefGridPromptRules.Detect(device == null ? "" : device.Name, device == null ? "" : device.Meta, profile);
        }

        static ChefGridButtonPrompt Resolve(ControllerIconLookup lookup, ChefGridPromptStyle style,
            ILogicalButton logical, ControlPadInput.Button fallback)
        {
            // Use the current player's actual mapped button, including split-pad and inverted confirm layouts.
            var button = logical == null ? fallback :
                PlayerButtonImage.GetControlPadButton<ControlPadInput.Button>(logical, ControllerIconLookup.DeviceContext.Pad) ?? fallback;
            string name = button.ToString();
            var result = new ChefGridButtonPrompt { Key = ChefGridPromptRules.Key(style, name) };
            if (lookup == null) return result;
            if (style == ChefGridPromptStyle.Xbox)
            {
                // PlayerButtonImage cancels GetIcon's internal A/B inversion this same way:
                // the logical input tree already resolved the physical button to display.
                if (PlayerManagerShared<PCPlayerManager.PCPlayerProfile>.AcceptAndCancelButtonsInverted)
                    button = button == ControlPadInput.Button.A ? ControlPadInput.Button.B :
                        button == ControlPadInput.Button.B ? ControlPadInput.Button.A : button;
                result.Icon = lookup.GetIcon(button, ControllerIconLookup.IconContext.Bordered, ControllerIconLookup.DeviceContext.Pad);
            }
            else
            {
                // The PC public lookup always selects XboxOne. Reuse the serialized PS4/NX
                // sprites explicitly; missing platform assets retain a readable key label.
                object platforms = Field(lookup, "m_borderedIcons");
                object pack = Field(platforms, style == ChefGridPromptStyle.PlayStation ? "PS4" : "NX");
                result.Icon = Field(pack, ChefGridPromptRules.IconField(style, name)) as Sprite;
            }
            return result;
        }

        static object Field(object source, string name)
        {
            if (source == null) return null;
            var field = source.GetType().GetField(name, Fields);
            return field == null ? null : field.GetValue(source);
        }

        internal void Reset()
        {
            previousInput = null;
            previousAccept = previousCancel = null;
            refreshAt = 0;
            Accept = Back = default(ChefGridButtonPrompt);
        }
    }
}
