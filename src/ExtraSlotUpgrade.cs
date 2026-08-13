using REPOLib.Modules;
using UnityEngine;

namespace ExtraInventorySlotUpgrade;

/// <summary>
/// Ties the REPOLib upgrade to the slot engine. REPOLib owns persistence: it stores the upgrade
/// under StatsManager.dictionaryOfDictionaries["playerUpgradeExtraInventorySlot"], so levels save
/// and load with the run and no custom persistence is needed.
/// </summary>
internal static class ExtraSlotUpgrade
{
    private static PlayerUpgrade _upgrade;

    public static PlayerUpgrade Upgrade => _upgrade;

    /// <summary>
    /// Called from a StatsManager.RunStartStats postfix that runs before REPOLib's, so the item is
    /// queued and the upgrade registered in time for REPOLib to wire both into the game.
    /// </summary>
    public static void Register()
    {
        if (_upgrade != null)
        {
            EnsureStatDictionaryRegistered();
            return;
        }

        if (!UpgradeItemFactory.Build()) return;

        _upgrade = Upgrades.RegisterUpgrade(
            UpgradeItemFactory.UpgradeId,
            UpgradeItemFactory.Item,
            StartAction,
            UpgradeAction);

        if (_upgrade == null)
        {
            Log.Error("REPOLib refused to register the upgrade. Buying the item will do nothing.");
            return;
        }

        EnsureStatDictionaryRegistered();
        Log.Info($"Registered upgrade \"{UpgradeItemFactory.UpgradeId}\".");
    }

    /// <summary>
    /// REPOLib adds the upgrade's dictionary to StatsManager from its own RunStartStats postfix.
    /// If our registration ever lands after that (patch ordering is not guaranteed across mod
    /// versions), the dictionary would be missing for the whole run and levels would not persist.
    /// Doing it ourselves when absent makes the ordering irrelevant.
    /// </summary>
    private static void EnsureStatDictionaryRegistered()
    {
        var stats = StatsManager.instance;
        if (stats == null || _upgrade == null) return;

        string key = "playerUpgrade" + UpgradeItemFactory.UpgradeId;
        if (stats.dictionaryOfDictionaries.ContainsKey(key)) return;

        stats.dictionaryOfDictionaries.Add(key, _upgrade.PlayerDictionary);
        Log.Verbose($"Added \"{key}\" to StatsManager ahead of REPOLib.");
    }

    /// <summary>Runs during PlayerController.LateStart with the player's saved level.</summary>
    private static void StartAction(PlayerAvatar player, int level) => ApplyLevel(player, level, "start");

    /// <summary>Runs whenever the level changes, locally or via REPOLib's networked event.</summary>
    private static void UpgradeAction(PlayerAvatar player, int level) => ApplyLevel(player, level, "upgrade");

    private static bool _correcting;

    private static void ApplyLevel(PlayerAvatar player, int level, string source)
    {
        if (player == null) return;

        // Cap layer 2 (§6): clamp on purchase. Runs for every player, not just the local one, so
        // the host is the one that notices and corrects an over-cap level. SetLevel re-broadcasts,
        // which is what makes the correction authoritative for everyone.
        if (!_correcting && PluginConfig.HostEnforcesCap.Value &&
            SemiFunc.IsMasterClientOrSingleplayer() &&
            _upgrade != null && level > SlotEngine.ConfiguredMaxExtra)
        {
            _correcting = true;
            try
            {
                Log.Warn($"{SemiFunc.PlayerGetName(player)} reported level {level}, above the cap " +
                         $"of {SlotEngine.ConfiguredMaxExtra}. Clamping and re-broadcasting.");
                _upgrade.SetLevel(player.steamID, SlotEngine.ConfiguredMaxExtra);
            }
            finally
            {
                _correcting = false;
            }
            return; // SetLevel re-entered ApplyLevel with the corrected value
        }

        // The inventory HUD is local-only; remote players' slot counts matter only to the host's
        // stat bookkeeping, which is keyed by slot index and needs no per-player count.
        if (!player.isLocal)
        {
            Log.Verbose($"Ignoring {source} level {level} for remote player " +
                        $"{SemiFunc.PlayerGetName(player)}.");
            return;
        }

        // Cap layer 1 (§6): clamp on apply. SetUpgradeExtraSlots clamps again internally, so even a
        // corrupted save or a hostile level value cannot produce a seventh slot.
        int clamped = Mathf.Clamp(level, 0, SlotEngine.ConfiguredMaxExtra);
        SlotEngine.SetUpgradeExtraSlots(clamped);

        Log.Info($"Extra Inventory Slot {source}: level {level} -> {clamped} extra slot(s).");
    }

    /// <summary>
    /// The local player's authoritative level, read straight from the dictionary rather than from
    /// the last callback. PunManager.ReceiveSyncData replaces that dictionary wholesale on a host
    /// sync without going through REPOLib's ApplyUpgrade, so polling it is the only way to notice
    /// a correction that arrives mid-run.
    /// </summary>
    public static int GetLocalLevel()
    {
        if (_upgrade == null) return 0;

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null || string.IsNullOrEmpty(local.steamID)) return 0;

        return _upgrade.GetLevel(local.steamID);
    }

    /// <summary>Cap layer 3 support: has this player already bought every slot they can?</summary>
    public static bool IsAtCap(PlayerAvatar player)
    {
        if (_upgrade == null || player == null) return false;
        return _upgrade.GetLevel(player) >= SlotEngine.ConfiguredMaxExtra;
    }
}
