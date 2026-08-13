using BepInEx.Configuration;

namespace ExtraInventorySlotUpgrade;

internal static class PluginConfig
{
    /// <summary>Hard ceiling the mod will ever build, independent of config. Slots 4, 5, 6.</summary>
    public const int AbsoluteMaxExtraSlots = 3;

    public static ConfigEntry<int> MaxExtraSlots;
    public static ConfigEntry<bool> HostEnforcesCap;
    public static ConfigEntry<int> DebugForceExtraSlots;
    public static ConfigEntry<bool> SuppressConflictingBinds;
    public static ConfigEntry<bool> SuppressOnlyWhenSlotUnlocked;
    public static ConfigEntry<float> PriceMin;
    public static ConfigEntry<float> PriceMax;
    public static ConfigEntry<float> PriceIncreasePerPurchase;
    public static ConfigEntry<string> UpgradeColor;
    public static ConfigEntry<int> AlbedoResolution;
    public static ConfigEntry<string> FrontFaceUV;
    public static ConfigEntry<string> SideBleedUV;
    public static ConfigEntry<bool> DumpPropTextures;
    public static ConfigEntry<bool> VerboseLogging;

    public static void Bind(ConfigFile cfg)
    {
        PriceMin = cfg.Bind(
            "Shop", "PriceMin", 25000f,
            "Lower bound of the shop price, in the dollars the shop actually displays (so 25000 " +
            "shows as $25K). This is NOT Item.value.valueMin — the game multiplies that by " +
            "ShopManager.itemValueMultiplier and divides by 1000, and the mod does the conversion.");

        PriceMax = cfg.Bind(
            "Shop", "PriceMax", 30000f,
            "Upper bound of the shop price, in displayed dollars. Note the game still applies its " +
            "usual upgrade discount of 10% per extra player, exactly as it does for vanilla upgrades.");

        PriceIncreasePerPurchase = cfg.Bind(
            "Shop", "PriceIncreasePerPurchase", 0f,
            new ConfigDescription(
                "How much each previous purchase inflates the price, as a fraction of the base. " +
                "Vanilla upgrades use 0.5, so a fourth copy costs three times the first — very " +
                "visible at our price point. 0 keeps every roll inside PriceMin..PriceMax. " +
                "Set to -1 to inherit whatever the game's own value is.",
                new AcceptableValueRange<float>(-1f, 4f)));

        UpgradeColor = cfg.Bind(
            "Shop", "UpgradeColor", "#FF4FA3",
            "Albedo colour of the upgrade prop, as an HTML hex string. Applied to a per-instance " +
            "material copy, so vanilla upgrades are never recoloured.");

        AlbedoResolution = cfg.Bind(
            "Shop", "AlbedoResolution", 1024,
            new ConfigDescription(
                "Resolution of the generated prop texture. The vanilla source is 512; going higher " +
                "keeps the custom front art crisper when you hold the prop close, at the cost of " +
                "video memory. Never goes below the source resolution.",
                new AcceptableValueList<int>(512, 1024, 2048, 4096)));

        FrontFaceUV = cfg.Bind(
            "Shop", "FrontFaceUV", "0.5,0,0.86328125,0.55078125",
            "UV rectangle of the box's front panel as \"u0,v0,u1,v1\", measured from the vanilla " +
            "upgrade atlas (pixels x 256-441, y 230-511 of 512). Only touch this if a game update " +
            "moves the panel and the art lands on the wrong face.");

        SideBleedUV = cfg.Bind(
            "Shop", "SideBleedUV", "0.863281,0.277344,0.917969,0.445313",
            "UV rectangle on the side panel where the vanilla hero art spilled across the fold " +
            "(pixels x 442-470, y 284-369 of 512). It gets healed by mirroring the clean texture " +
            "beside it, otherwise a stray blob is left on the side of the box. Empty to disable.");

        SuppressConflictingBinds = cfg.Bind(
            "Input", "SuppressConflictingBinds", true,
            "Keyboard 5 and 6 are bound to Expression1/Expression2 (emotes) in vanilla. When true, " +
            "slot selection wins on those keys and the emote does not fire. Turn off if this fights " +
            "another mod — you will then get both the slot and the emote on the same press.");

        SuppressOnlyWhenSlotUnlocked = cfg.Bind(
            "Input", "SuppressOnlyWhenSlotUnlocked", false,
            "Narrows the setting above: only mute the emote once the slot sharing its key is " +
            "actually unlocked. Keeps emotes 1 and 2 usable until you buy slots 5 and 6, at the " +
            "cost of the key changing behaviour mid-run.");

        MaxExtraSlots = cfg.Bind(
            "Slots", "MaxExtraSlots", 3,
            new ConfigDescription(
                "Maximum extra inventory slots a player can hold (on top of the vanilla 3). " +
                "3 = the intended 6-slot cap.",
                new AcceptableValueRange<int>(1, AbsoluteMaxExtraSlots)));

        HostEnforcesCap = cfg.Bind(
            "Slots", "HostEnforcesCap", true,
            "When you are the host, clamp every player's upgrade level to your MaxExtraSlots and " +
            "push the correction back out. Turn off to let each client use its own cap, which is " +
            "only sensible for solo play or testing.");

        DebugForceExtraSlots = cfg.Bind(
            "Debug", "DebugForceExtraSlots", 0,
            new ConfigDescription(
                "Force this many extra slots, ignoring upgrades. 0 = off (normal behaviour). Also " +
                "settable live with the /eis chat command. Ignored unless you are the host or " +
                "playing solo, so it cannot be used to take slots you never bought in someone " +
                "else's lobby.",
                new AcceptableValueRange<int>(0, AbsoluteMaxExtraSlots)));

        DumpPropTextures = cfg.Bind(
            "Debug", "DumpPropTextures", false,
            "Write the vanilla upgrade prop's textures and UV layout to " +
            "BepInEx/ExtraInventorySlotUpgrade-dump on the next run start. Development aid for " +
            "positioning custom art; leave off for normal play.");

        VerboseLogging = cfg.Bind(
            "Debug", "VerboseLogging", false,
            "Log slot construction, HUD layout details and stat syncing to the BepInEx console.");
    }
}
