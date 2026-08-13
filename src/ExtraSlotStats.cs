using System.Collections.Generic;

namespace ExtraInventorySlotUpgrade;

/// <summary>
/// Vanilla stores inventory-slot contents in three hand-rolled dictionaries on StatsManager
/// (playerInventorySpot1/2/3 plus a matching ...Taken set). This class supplies the same pair of
/// dictionaries for slots 4-6 and gives every consumer one uniform, index-based way to reach
/// whichever dictionary backs slot N — vanilla or ours.
///
/// The extra dictionaries are registered into StatsManager.dictionaryOfDictionaries under the
/// obvious next names ("playerInventorySpot4"...), which buys us:
///   - PunManager.UpdateStat / UpdateStatRPC syncing for free (it routes by dictionary name),
///   - save/load tolerance for free (StatsManager.LoadGame adds unknown keys, logs, moves on).
/// </summary>
internal static class ExtraSlotStats
{
    /// <summary>Vanilla slot count. Slots below this index live on StatsManager's own fields.</summary>
    public const int VanillaSlots = 3;

    /// <summary>Highest slot index the mod ever addresses (exclusive). 6 total slots.</summary>
    public const int AbsoluteMaxSlots = VanillaSlots + PluginConfig.AbsoluteMaxExtraSlots;

    private static readonly Dictionary<int, Dictionary<string, int>> Contents = new();
    private static readonly Dictionary<int, Dictionary<string, int>> Taken = new();

    private static StatsManager _registeredWith;

    public static string DictionaryName(int slot) => "playerInventorySpot" + (slot + 1);

    /// <summary>steamID -> item-name hash, for whichever dictionary backs this slot.</summary>
    public static Dictionary<string, int> ContentsDict(int slot)
    {
        switch (slot)
        {
            case 0: return StatsManager.instance.playerInventorySpot1;
            case 1: return StatsManager.instance.playerInventorySpot2;
            case 2: return StatsManager.instance.playerInventorySpot3;
        }
        if (!Contents.TryGetValue(slot, out var d))
        {
            d = new Dictionary<string, int>();
            Contents[slot] = d;
        }
        return d;
    }

    /// <summary>The "already re-equipped this scene" marker set, per slot.</summary>
    public static Dictionary<string, int> TakenDict(int slot)
    {
        switch (slot)
        {
            case 0: return StatsManager.instance.playerInventorySpot1Taken;
            case 1: return StatsManager.instance.playerInventorySpot2Taken;
            case 2: return StatsManager.instance.playerInventorySpot3Taken;
        }
        if (!Taken.TryGetValue(slot, out var d))
        {
            d = new Dictionary<string, int>();
            Taken[slot] = d;
        }
        return d;
    }

    /// <summary>
    /// Mirrors what StatsManager.Start does for slots 1-3: register the dictionary, mark it
    /// non-persistent, and give it the same "-1 means absent" strip value.
    /// </summary>
    public static void RegisterWithStatsManager(StatsManager sm)
    {
        if (sm == null) return;

        bool freshInstance = _registeredWith != sm;
        _registeredWith = sm;

        for (int slot = VanillaSlots; slot < AbsoluteMaxSlots; slot++)
        {
            string name = DictionaryName(slot);
            var contents = ContentsDict(slot);

            if (freshInstance)
            {
                // New StatsManager => new run. Slot contents are per-scene state, never carried over.
                contents.Clear();
                TakenDict(slot).Clear();
            }

            if (!sm.dictionaryOfDictionaries.ContainsKey(name))
            {
                sm.dictionaryOfDictionaries.Add(name, contents);
            }
            if (!sm.doNotSaveTheseDictionaries.Contains(name))
            {
                sm.doNotSaveTheseDictionaries.Add(name);
            }
            // StatsManager.Start seeds -1 for every "playerInventorySpot*" key; match that exactly.
            sm.stripTheseDictionaries[name] = -1;

            Log.Verbose($"Registered stat dictionary \"{name}\".");
        }
    }

    /// <summary>Counterpart to StatsManager.StuffNeedingResetAtTheEndOfAScene.</summary>
    public static void ClearTaken()
    {
        foreach (var d in Taken.Values) d.Clear();
        Log.Verbose("Cleared extra-slot Taken markers.");
    }
}
