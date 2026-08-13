using System;
using System.Collections.Generic;
using REPOLib.Modules;

namespace ExtraInventorySlotUpgrade;

/// <summary>
/// Phase 2 validation hook. Lets the slot engine be exercised in isolation, before the shop item
/// exists. Registered through REPOLib so it shows up in the normal chat command list.
/// </summary>
internal static class DebugCommands
{
    public static void Register()
    {
        try
        {
            Commands.RegisterCommand(new DebugCommandHandler.ChatCommand(
                "eis",
                "Extra Inventory Slot: /eis slots <0-3> | /eis status",
                Execute,
                Suggest,
                null,
                debugOnly: false));
        }
        catch (Exception e)
        {
            Log.Warn("Could not register the /eis chat command; use the DebugForceExtraSlots " +
                     "config entry instead. " + e.Message);
        }
    }

    private static void Execute(bool isDebugConsole, string[] args)
    {
        if (args == null || args.Length == 0)
        {
            Status();
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "slots":
                if (args.Length < 2 || !int.TryParse(args[1], out int extra))
                {
                    Log.Info("Usage: /eis slots <0-3>  (number of EXTRA slots on top of the vanilla 3)");
                    return;
                }
                // Same reasoning as DebugForceExtraSlots: handing yourself slots as a client takes
                // shop-bought capacity you never paid for, in someone else's lobby.
                if (!SemiFunc.IsMasterClientOrSingleplayer())
                {
                    Log.Info("[/eis] Only the host can force a slot count.");
                    return;
                }
                PluginConfig.DebugForceExtraSlots.Value = Math.Max(0,
                    Math.Min(extra, PluginConfig.AbsoluteMaxExtraSlots));
                // SlotEngine.Tick picks the new value up on the next frame.
                Status();
                break;

            case "status":
            default:
                Status();
                break;
        }
    }

    private static void Status()
    {
        int listCount = Inventory.instance != null ? Inventory.instance.inventorySpots.Count : -1;
        Log.Info($"[/eis] extra={SlotEngine.ExtraSlots} total={SlotEngine.TotalSlots} " +
                 $"Inventory.inventorySpots.Count={listCount} " +
                 $"maxExtra={SlotEngine.ConfiguredMaxExtra}");
    }

    private static List<string> Suggest(bool isDebugConsole, string partial, string[] args)
    {
        return new List<string> { "slots", "status" };
    }
}
