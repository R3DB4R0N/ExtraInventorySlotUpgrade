using HarmonyLib;

namespace ExtraInventorySlotUpgrade.Patches;

[HarmonyPatch(typeof(InputManager))]
internal static class InputManagerPatches
{
    /// <summary>
    /// Why: InputManager.InitializeInputs creates the inputActions dictionary and populates every
    /// binding (Assembly-CSharp InputManager.InitializeInputs, the block registering Inventory1..3
    /// on Keyboard 1/2/3 and Expression1..6 on 5/6/7/8/9/0). We append actions for slots 4-6 here,
    /// before Awake calls StoreDefaultBindings, so our keys get default bindings recorded too.
    /// </summary>
    [HarmonyPatch("InitializeInputs")]
    [HarmonyPostfix]
    private static void InitializeInputsPostfix(InputManager __instance)
    {
        ExtraInput.RegisterActions(__instance);
    }

    /// <summary>
    /// Why: two jobs on the same seam.
    ///
    /// 1. Vanilla KeyDown returns false for Inventory1/2/3 while disableMovementTimer is running.
    ///    Our synthetic slot keys are not in that hardcoded list, so we reproduce the gate — without
    ///    it, slots 4-6 would respond during cutscenes and menus when 1-3 do not.
    ///
    /// 2. Keyboard 5 and 6 are bound to Expression1/Expression2 in vanilla. Requirement 7 says slot
    ///    selection wins, so we mute the emote. Patching KeyDown/KeyHold/KeyUp covers every consumer
    ///    at once (PlayerExpression polls both InputHold and InputDown), which is far safer than
    ///    chasing individual call sites.
    /// </summary>
    [HarmonyPatch(nameof(InputManager.KeyDown))]
    [HarmonyPrefix]
    private static bool KeyDownPrefix(InputManager __instance, InputKey key, ref bool __result)
    {
        if (ExtraInput.IsSlotKey(key) && __instance.disableMovementTimer > 0f)
        {
            __result = false;
            return false;
        }

        if (ExtraInput.ShouldSuppress(key))
        {
            __result = false;
            return false;
        }

        return true;
    }

    [HarmonyPatch(nameof(InputManager.KeyHold))]
    [HarmonyPrefix]
    private static bool KeyHoldPrefix(InputKey key, ref bool __result)
    {
        if (ExtraInput.ShouldSuppress(key))
        {
            __result = false;
            return false;
        }
        return true;
    }

    [HarmonyPatch(nameof(InputManager.KeyUp))]
    [HarmonyPrefix]
    private static bool KeyUpPrefix(InputKey key, ref bool __result)
    {
        if (ExtraInput.ShouldSuppress(key))
        {
            __result = false;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(InventorySpot))]
internal static class InventorySpotPatches
{
    /// <summary>
    /// Why: InventorySpot.Update opens with a hardcoded if/else chain —
    ///     if (SemiFunc.InputDown(InputKey.Inventory1) &amp;&amp; inventorySpotIndex == 0) HandleInput();
    ///     else if (... Inventory2 ... == 1) ... else if (... Inventory3 ... == 2) ...
    /// which simply never fires for index >= 3. A prefix adds the missing cases while reusing
    /// vanilla's own HandleInput, so equip/unequip, the 0.2s cooldown and the InputDisableTimer
    /// check behave identically to slots 1-3 rather than being reimplemented.
    /// </summary>
    [HarmonyPatch("Update")]
    [HarmonyPrefix]
    private static void UpdatePrefix(InventorySpot __instance)
    {
        int index = __instance.inventorySpotIndex;
        if (index < ExtraSlotStats.VanillaSlots || index >= ExtraSlotStats.AbsoluteMaxSlots) return;

        if (SemiFunc.InputDown(ExtraInput.KeyForSlot(index)))
        {
            __instance.HandleInput();
        }
    }
}
