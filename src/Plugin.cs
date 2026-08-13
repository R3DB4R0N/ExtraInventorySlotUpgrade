using System;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace ExtraInventorySlotUpgrade;

[BepInPlugin(Guid, Name, Version)]
[BepInDependency("REPOLib", BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BaseUnityPlugin
{
    // Also the config filename (BepInEx names it "<GUID>.cfg") and the Harmony instance id.
    public const string Guid = "extrainventoryslotupgrade";
    public const string Name = "Extra Inventory Slot";
    public const string Version = "1.0.0";

    internal static Plugin Instance;
    private Harmony _harmony;

    private void Awake()
    {
        Instance = this;
        Log.Init(Logger);
        PluginConfig.Bind(Config);

        WarnAboutConflicts();

        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(Plugin).Assembly);

        DebugCommands.Register();

        // BepInEx's plugin host object is already DontDestroyOnLoad, so this survives level loads.
        gameObject.AddComponent<SlotEngineRunner>();

        Log.Info($"{Name} v{Version} loaded.");
    }

    /// <summary>
    /// MoreInventorySlots (nickklmao, deprecated) and ExtraInventorySlots (DarkSpider) patch the
    /// same things. Matched loosely by GUID substring so a rename does not slip past us.
    /// We warn rather than fight — running both will produce duplicate HUD slots.
    /// </summary>
    private void WarnAboutConflicts()
    {
        try
        {
            var clashes = Chainloader.PluginInfos.Values
                .Where(p => p != null && p.Metadata != null)
                .Where(p => !string.Equals(p.Metadata.GUID, Guid, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.Metadata.GUID.IndexOf("inventoryslot", StringComparison.OrdinalIgnoreCase) >= 0
                         || p.Metadata.Name.IndexOf("inventory slot", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => $"{p.Metadata.Name} ({p.Metadata.GUID} v{p.Metadata.Version})")
                .ToList();

            foreach (var clash in clashes)
            {
                Log.Warn("=========================================================");
                Log.Warn($" CONFLICT: another inventory-slot mod is loaded: {clash}");
                Log.Warn(" Running it alongside Extra Inventory Slot will duplicate HUD slots");
                Log.Warn(" and corrupt slot bookkeeping. Disable one of them.");
                Log.Warn("=========================================================");
            }
        }
        catch (Exception e)
        {
            Log.Verbose("Conflict scan failed (harmless): " + e.Message);
        }
    }
}

/// <summary>Drives SlotEngine.Tick. Separate component so the plugin class stays declarative.</summary>
internal class SlotEngineRunner : MonoBehaviour
{
    private void Update()
    {
        try
        {
            SlotEngine.Tick();
        }
        catch (Exception e)
        {
            Log.Error("Slot engine tick failed: " + e);
            enabled = false; // do not spam a per-frame exception
        }
    }
}
