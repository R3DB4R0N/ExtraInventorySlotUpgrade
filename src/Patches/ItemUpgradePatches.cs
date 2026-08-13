using HarmonyLib;
using UnityEngine;

namespace ExtraInventorySlotUpgrade.Patches;

[HarmonyPatch(typeof(ItemUpgrade))]
internal static class ItemUpgradePatches
{
    /// <summary>
    /// Cap layer 3 (§6). Recon ruled out the approach the spec assumed: Item.maxPurchase is checked
    /// against StatsManager.itemsPurchasedTotal, a run-global counter keyed by item name, and
    /// ShopManager.GetAllItemsFromStatsManager early-returns for non-master clients — so the host
    /// rolls one shop everyone sees and there is no per-player filter to hook. Vanilla works the
    /// same way: the Health upgrade is offered regardless of anyone's level, and whoever picks the
    /// prop up gets it.
    ///
    /// So we block consumption instead of purchase. ItemUpgrade.PlayerUpgrade is where the prop
    /// destroys itself and fires upgradeEvent; returning false leaves the prop intact, un-consumed
    /// and still usable by a team-mate who is not maxed. Nobody wastes 25k and no refund path is
    /// needed.
    /// </summary>
    [HarmonyPatch(nameof(ItemUpgrade.PlayerUpgrade))]
    [HarmonyPrefix]
    private static bool PlayerUpgradePrefix(ItemUpgrade __instance)
    {
        // Only our prop. Every other upgrade in the game must be untouched.
        if (__instance.GetComponent<ExtraSlotUpgradeApplier>() == null) return true;

        var toggle = __instance.GetComponent<ItemToggle>();
        if (toggle == null || !toggle.toggleState) return true; // vanilla would no-op anyway

        PlayerAvatar player = SemiFunc.PlayerAvatarGetFromPhotonID(toggle.playerTogglePhotonID);
        if (player == null) return true;

        if (!ExtraSlotUpgrade.IsAtCap(player)) return true;

        if (player.isLocal)
        {
            int maxSlots = SlotEngine.VanillaSlots + SlotEngine.ConfiguredMaxExtra;
            SemiFunc.UIFocusText(
                $"You already have all {maxSlots} inventory slots",
                new Color(1f, 0.31f, 0.64f),
                Color.white,
                3f);
            Log.Info("Blocked upgrade use: local player is already at the slot cap. " +
                     "The item was not consumed.");
        }

        return false;
    }
}
