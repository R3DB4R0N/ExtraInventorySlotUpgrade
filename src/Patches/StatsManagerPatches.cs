using System.Collections.Generic;
using HarmonyLib;

namespace ExtraInventorySlotUpgrade.Patches;

[HarmonyPatch(typeof(StatsManager))]
internal static class StatsManagerPatches
{
    /// <summary>
    /// Why: StatsManager.Start builds dictionaryOfDictionaries and registers exactly three
    /// playerInventorySpot* dictionaries (Assembly-CSharp StatsManager.Start, the block adding
    /// "playerInventorySpot1".."3" and their doNotSave entries). We append 4-6 right after, so
    /// they exist before LoadGame merges a save into them.
    /// </summary>
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void StartPostfix(StatsManager __instance)
    {
        ExtraSlotStats.RegisterWithStatsManager(__instance);
    }

    /// <summary>
    /// Why: StatsManager.RunStartStats calls LoadItemsFromFolder, which is what fills
    /// itemDictionary — so this postfix is the first moment a vanilla upgrade prefab exists to
    /// clone. REPOLib patches the same method to flush its item and upgrade queues into the game,
    /// so we must register before it does; HarmonyBefore pins that ordering explicitly rather than
    /// relying on priority numbers.
    /// </summary>
    [HarmonyPatch("RunStartStats")]
    [HarmonyPostfix]
    [HarmonyBefore("REPOLib")]
    private static void RunStartStatsPostfix()
    {
        ExtraSlotUpgrade.Register();
    }

    /// <summary>
    /// Why: StatsManager.PlayerInventoryUpdate is three copy-pasted "if (spot == N)" blocks and
    /// silently ignores any spot >= 3. A postfix is enough — vanilla never touches our range, so
    /// there is nothing to suppress, only something to add.
    /// Vanilla's master-client guard sits at the top of the method, so we repeat it here (a
    /// postfix still runs after an early return).
    /// </summary>
    [HarmonyPatch(nameof(StatsManager.PlayerInventoryUpdate))]
    [HarmonyPostfix]
    private static void PlayerInventoryUpdatePostfix(string _steamID, string itemName, int spot, bool sync)
    {
        if (spot < ExtraSlotStats.VanillaSlots || spot >= ExtraSlotStats.AbsoluteMaxSlots) return;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        int hash = string.IsNullOrEmpty(itemName) ? -1 : itemName.GetHashCode();

        Dictionary<string, int> contents = ExtraSlotStats.ContentsDict(spot);
        Dictionary<string, int> taken = ExtraSlotStats.TakenDict(spot);

        if (hash != -1) contents[_steamID] = hash;
        else contents.Remove(_steamID);

        if (contents.TryGetValue(_steamID, out int stored) && stored != -1) taken[_steamID] = 1;
        else taken.Remove(_steamID);

        if (sync && PunManager.instance != null)
        {
            PunManager.instance.UpdateStat(ExtraSlotStats.DictionaryName(spot), _steamID, hash);
        }

        Log.Verbose($"Slot {spot + 1} of {_steamID} -> \"{itemName}\" (hash {hash}, sync {sync}).");
    }

    /// <summary>
    /// Why: StatsManager.StuffNeedingResetAtTheEndOfAScene clears the three vanilla ...Taken
    /// dictionaries. Ours need the same treatment or items refuse to re-equip next scene.
    /// </summary>
    [HarmonyPatch(nameof(StatsManager.StuffNeedingResetAtTheEndOfAScene))]
    [HarmonyPostfix]
    private static void StuffNeedingResetPostfix()
    {
        ExtraSlotStats.ClearTaken();
    }
}
