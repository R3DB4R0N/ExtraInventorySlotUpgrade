using HarmonyLib;
using UnityEngine;

namespace ExtraInventorySlotUpgrade.Patches;

[HarmonyPatch(typeof(ShopManager))]
internal static class ShopManagerPatches
{
    /// <summary>
    /// Why: ShopManager.UpgradeValueGet inflates every upgrade's price by upgradeValueIncrease
    /// (0.5) for each prior purchase of the same item name —
    ///     _value += _value * upgradeValueIncrease * StatsManager.GetItemsUpgradesPurchased(name)
    /// — and AddItemsUpgradesPurchased is called at checkout in ExtractionPoint, so it counts
    /// purchases rather than uses. That is fine for a 5k vanilla upgrade but turns our 25-30k item
    /// into 75-90k after four purchases, well outside the configured range.
    ///
    /// This replaces the calculation for our item only, keeping vanilla's player-count discount
    /// (10% per extra player) and making the escalation configurable. Every consumer goes through
    /// this one method — the real price in ItemAttributes.GetValue, the shop's affordability filter
    /// and the upgrade stand's — so patching here keeps all three consistent.
    /// </summary>
    [HarmonyPatch(nameof(ShopManager.UpgradeValueGet))]
    [HarmonyPrefix]
    private static bool UpgradeValueGetPrefix(float _value, Item item, ref float __result)
    {
        if (item == null || item.name != UpgradeItemFactory.ItemAssetName) return true;

        float increase = PluginConfig.PriceIncreasePerPurchase.Value;
        if (increase < 0f) return true; // opt back into whatever the game does

        if (GameDirector.instance == null || StatsManager.instance == null) return true;

        int players = Mathf.Min(6, GameDirector.instance.PlayerList.Count);
        float value = _value - _value * 0.1f * (players - 1);
        value += value * increase * StatsManager.instance.GetItemsUpgradesPurchased(item.name);

        __result = Mathf.Ceil(value);
        return false;
    }
}
