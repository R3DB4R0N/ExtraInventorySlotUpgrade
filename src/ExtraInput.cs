using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace ExtraInventorySlotUpgrade;

/// <summary>
/// Vanilla's InputKey enum stops at Inventory3, and an enum cannot be extended from a mod. But
/// InputManager stores its actions in a Dictionary&lt;InputKey, InputAction&gt; and
/// InputManager.KeyDown falls through to a generic WasPressedThisFrame() for anything it does not
/// special-case — so casting unused integers to InputKey gives us real, first-class InputActions
/// for slots 4-6 without touching the enum or reimplementing input handling.
///
/// Nothing outside InputManager enumerates inputActions, so the settings menu is unaffected.
/// </summary>
internal static class ExtraInput
{
    /// <summary>Well clear of the real enum, which currently ends at 32.</summary>
    private const int KeyBase = 9000;

    /// <summary>Vanilla keys we may have to mute because they sit on the same physical key.</summary>
    private static readonly InputKey[] VanillaExpressionKeys =
    {
        InputKey.Expression1, InputKey.Expression2, InputKey.Expression3,
        InputKey.Expression4, InputKey.Expression5, InputKey.Expression6
    };

    public static InputKey KeyForSlot(int slot) => (InputKey)(KeyBase + slot);

    public static bool IsSlotKey(InputKey key)
    {
        int raw = (int)key;
        return raw >= KeyBase + ExtraSlotStats.VanillaSlots &&
               raw < KeyBase + ExtraSlotStats.AbsoluteMaxSlots;
    }

    public static IEnumerable<InputKey> SlotKeys()
    {
        for (int slot = ExtraSlotStats.VanillaSlots; slot < ExtraSlotStats.AbsoluteMaxSlots; slot++)
        {
            yield return KeyForSlot(slot);
        }
    }

    /// <summary>
    /// Registers one InputAction per extra slot, bound to the matching number key. Called from a
    /// postfix on InputManager.InitializeInputs, i.e. before StoreDefaultBindings, so the game
    /// records defaults for our keys too and "reset to defaults" keeps working.
    /// </summary>
    public static void RegisterActions(InputManager manager)
    {
        if (manager?.inputActions == null) return;

        for (int slot = ExtraSlotStats.VanillaSlots; slot < ExtraSlotStats.AbsoluteMaxSlots; slot++)
        {
            int number = slot + 1;
            InputKey key = KeyForSlot(slot);

            var action = new InputAction($"Inventory{number}", InputActionType.Value, $"<Keyboard>/{number}");
            action.Enable();
            manager.inputActions[key] = action;

            Log.Verbose($"Registered input action Inventory{number} -> <Keyboard>/{number} as InputKey {(int)key}.");
        }
    }

    /// <summary>
    /// True when a vanilla key currently resolves to the same physical control as one of our slot
    /// keys. Comparing effective binding paths rather than hardcoding "5 and 6 are emotes" means a
    /// player who rebinds their emotes keeps them working.
    /// </summary>
    public static bool ConflictsWithSlotKey(InputKey vanillaKey)
    {
        var manager = InputManager.instance;
        if (manager?.inputActions == null) return false;
        if (!manager.inputActions.TryGetValue(vanillaKey, out var vanillaAction)) return false;

        foreach (InputKey slotKey in SlotKeys())
        {
            if (!manager.inputActions.TryGetValue(slotKey, out var slotAction)) continue;

            foreach (var vanillaBinding in vanillaAction.bindings)
            {
                if (string.IsNullOrEmpty(vanillaBinding.effectivePath)) continue;
                foreach (var slotBinding in slotAction.bindings)
                {
                    if (vanillaBinding.effectivePath == slotBinding.effectivePath) return true;
                }
            }
        }
        return false;
    }

    public static bool IsVanillaExpressionKey(InputKey key)
    {
        foreach (var expressionKey in VanillaExpressionKeys)
        {
            if (key == expressionKey) return true;
        }
        return false;
    }

    /// <summary>
    /// Decides whether a colliding vanilla key should be muted this frame. Slot selection wins over
    /// the emote (requirement 7), but the player can opt out or restrict it to unlocked slots.
    /// </summary>
    public static bool ShouldSuppress(InputKey vanillaKey)
    {
        if (!PluginConfig.SuppressConflictingBinds.Value) return false;
        if (!IsVanillaExpressionKey(vanillaKey)) return false;
        if (!ConflictsWithSlotKey(vanillaKey)) return false;

        if (!PluginConfig.SuppressOnlyWhenSlotUnlocked.Value) return true;

        // Only mute the emote if the slot sharing its key is actually built.
        var manager = InputManager.instance;
        if (!manager.inputActions.TryGetValue(vanillaKey, out var vanillaAction)) return false;

        for (int slot = ExtraSlotStats.VanillaSlots; slot < ExtraSlotStats.VanillaSlots + SlotEngine.ExtraSlots; slot++)
        {
            if (!manager.inputActions.TryGetValue(KeyForSlot(slot), out var slotAction)) continue;

            foreach (var vanillaBinding in vanillaAction.bindings)
            {
                if (string.IsNullOrEmpty(vanillaBinding.effectivePath)) continue;
                foreach (var slotBinding in slotAction.bindings)
                {
                    if (vanillaBinding.effectivePath == slotBinding.effectivePath) return true;
                }
            }
        }
        return false;
    }
}
