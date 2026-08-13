using System;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;

namespace ExtraInventorySlotUpgrade.Patches;

[HarmonyPatch(typeof(PunManager))]
internal static class PunManagerPatches
{
    /// <summary>
    /// Why: PunManager.TruckPopulateItemVolumes clears the three vanilla ...Taken dictionaries
    /// when the truck repopulates. Same reasoning as StuffNeedingResetAtTheEndOfAScene.
    /// Vanilla's non-master early-return is above that clear, so mirror the guard.
    /// </summary>
    [HarmonyPatch(nameof(PunManager.TruckPopulateItemVolumes))]
    [HarmonyPostfix]
    private static void TruckPopulateItemVolumesPostfix()
    {
        if (SemiFunc.IsNotMasterClient()) return;
        ExtraSlotStats.ClearTaken();
    }

    /// <summary>
    /// Why: PunManager.SetItemNameLOGIC is the level-transition / truck re-equip path. It walks
    /// every player and tests playerInventorySpot1, then 2, then 3 as three literal branches,
    /// breaking on the first hit. There is no seam a prefix or postfix can widen — the slot search
    /// is the method — so this replaces the body with a byte-for-byte equivalent that loops over
    /// N slots instead of three.
    ///
    /// Fidelity note: everything before the slot search (PhotonView resolution, instanceName
    /// assignment, battery restore) is reproduced verbatim from Assembly-CSharp as of the
    /// 2026-05-25 build. If any of it throws we fall through to the untouched vanilla method
    /// rather than eat the item, and log loudly — a game update changing this method is the most
    /// likely way this mod breaks.
    /// </summary>
    [HarmonyPatch("SetItemNameLOGIC")]
    [HarmonyPrefix]
    private static bool SetItemNameLogicPrefix(string _name, int photonViewID, ItemAttributes _itemAttributes)
    {
        try
        {
            if (photonViewID == -1 && SemiFunc.IsMultiplayer()) return false;

            ItemAttributes itemAttributes = _itemAttributes;
            if (SemiFunc.IsMultiplayer())
            {
                PhotonView photonView = PhotonView.Find(photonViewID);
                if (!photonView) return false;
                itemAttributes = photonView.GetComponent<ItemAttributes>();
            }

            if (!_itemAttributes && !SemiFunc.IsMultiplayer()) return false;

            ItemBattery itemBattery = null;
            ItemEquippable itemEquippable = null;
            if ((bool)itemAttributes)
            {
                itemAttributes.instanceName = _name;
                itemBattery = itemAttributes.GetComponent<ItemBattery>();
                itemEquippable = itemAttributes.GetComponent<ItemEquippable>();
            }

            if ((bool)itemBattery)
            {
                itemBattery.SetBatteryLife(StatsManager.instance.GetBatteryLevel(_name));
            }

            if (!itemEquippable) return false;

            // --- the generalised part: vanilla's three branches, as a loop ---
            int spot = 0;
            bool found = false;
            PlayerAvatar owner = null;
            int hash = _name.GetHashCode();

            List<PlayerAvatar> players = SemiFunc.PlayerGetList();
            foreach (PlayerAvatar player in players)
            {
                string steamId = player.steamID;
                for (int i = 0; i < ExtraSlotStats.AbsoluteMaxSlots; i++)
                {
                    Dictionary<string, int> contents = ExtraSlotStats.ContentsDict(i);
                    Dictionary<string, int> taken = ExtraSlotStats.TakenDict(i);

                    if (contents.TryGetValue(steamId, out int stored) && stored == hash &&
                        !taken.ContainsKey(steamId))
                    {
                        spot = i;
                        found = true;
                        owner = player;
                        taken[steamId] = 1;
                        break;
                    }
                }
                if (found) break;
            }

            if (found)
            {
                int requestingPlayerId = -1;
                if (SemiFunc.IsMultiplayer()) requestingPlayerId = owner.photonView.ViewID;
                itemEquippable.RequestEquip(spot, requestingPlayerId);
                Log.Verbose($"Restored \"{_name}\" into slot {spot + 1} of {owner.steamID}.");
            }

            return false; // body fully handled
        }
        catch (Exception e)
        {
            Log.Error("SetItemNameLOGIC replacement threw; falling back to the vanilla method. " +
                      "Extra slots will not restore this transition. " + e);
            return true;
        }
    }
}
