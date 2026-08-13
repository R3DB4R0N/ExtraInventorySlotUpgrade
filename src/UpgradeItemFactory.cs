using System;
using System.Collections.Generic;
using REPOLib.Modules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ExtraInventorySlotUpgrade;

/// <summary>
/// Builds the shop item entirely at runtime by cloning a vanilla player-upgrade prop — no asset
/// bundle, no Unity Editor. The clone is stripped of the vanilla upgrade's own behaviour, tinted
/// pink on a private material copy, and handed to REPOLib for item + network-prefab registration.
/// </summary>
internal static class UpgradeItemFactory
{
    public const string UpgradeId = "ExtraInventorySlot";

    /// <summary>Key in StatsManager.itemDictionary. Must be unique across all mods.</summary>
    public const string ItemAssetName = "Item Upgrade Player Extra Inventory Slot";

    public const string DisplayName = "Extra Inventory Slot";

    /// <summary>Colour properties across the shader variants REPO's props might use.</summary>
    private static readonly string[] ColorProperties =
    {
        "_Color", "_BaseColor", "_MainColor", "_TintColor", "_AlbedoColor"
    };

    private static bool _attempted;

    public static Item Item { get; private set; }
    public static GameObject Prefab { get; private set; }

    public static bool Build()
    {
        if (_attempted) return Item != null;
        _attempted = true;

        try
        {
            return BuildInternal();
        }
        catch (Exception e)
        {
            Log.Error("Failed to build the upgrade item. The mod's slot engine still works, but " +
                      "the shop item will be missing. " + e);
            return false;
        }
    }

    private static bool BuildInternal()
    {
        Item baseItem = FindBaseUpgradeItem(out GameObject basePrefab);
        if (baseItem == null || basePrefab == null)
        {
            Log.Error("Could not find any vanilla player-upgrade item to clone. Shop item skipped.");
            return false;
        }
        Log.Info($"Cloning \"{baseItem.name}\" as the base for the Extra Inventory Slot upgrade.");

        // Dump the untouched original, before any cloning or tinting.
        PropTextureDump.Run(basePrefab, baseItem.name);

        Prefab = ClonePrefab(basePrefab);
        if (Prefab == null) return false;

        StripVanillaUpgradeBehaviour(Prefab);

        ColorPresets colours = CreatePinkPreset();
        Item = CreateItemAsset(baseItem, colours);

        var attributes = Prefab.GetComponent<ItemAttributes>();
        if (attributes == null)
        {
            Log.Error("Cloned prop has no ItemAttributes component. Shop item skipped.");
            return false;
        }
        attributes.item = Item;

        var itemUpgrade = Prefab.GetComponent<ItemUpgrade>();
        if (itemUpgrade != null)
        {
            itemUpgrade.isPlayerUpgrade = true;
            itemUpgrade.colorPreset = colours;
        }

        TintPink(Prefab);

        if (Prefab.GetComponent<ExtraSlotUpgradeApplier>() == null)
        {
            Prefab.AddComponent<ExtraSlotUpgradeApplier>();
        }

        // REPOLib registers the network prefab, fills in Item.prefab, and queues the item for
        // StatsManager. It registers immediately if the game has already loaded its item list.
        PrefabRef prefabRef = Items.RegisterItem(attributes);
        if (prefabRef == null)
        {
            Log.Error("REPOLib refused to register the item. Shop item skipped.");
            return false;
        }

        Log.Info($"Registered \"{DisplayName}\" — expected shop price " +
                 $"${PluginConfig.PriceMin.Value:N0}-${PluginConfig.PriceMax.Value:N0} " +
                 $"(raw value {Item.value.valueMin:N1}-{Item.value.valueMax:N1}).");
        return true;
    }

    /// <summary>
    /// Prefer Grab Strength (a plain, visually neutral upgrade prop), but accept any player upgrade
    /// so a game update renaming that one does not break the mod.
    /// </summary>
    private static Item FindBaseUpgradeItem(out GameObject prefab)
    {
        prefab = null;
        if (StatsManager.instance == null) return null;

        Item fallback = null;
        GameObject fallbackPrefab = null;

        foreach (Item candidate in StatsManager.instance.itemDictionary.Values)
        {
            if (candidate == null || candidate.disabled) continue;
            if (candidate.itemType != SemiFunc.itemType.item_upgrade) continue;
            if (candidate.name == ItemAssetName) continue;
            if (candidate.prefab == null || !candidate.prefab.IsValid()) continue;

            GameObject candidatePrefab;
            try
            {
                candidatePrefab = candidate.prefab.Prefab;
            }
            catch (Exception e)
            {
                Log.Verbose($"Skipping \"{candidate.name}\": prefab load failed ({e.Message}).");
                continue;
            }

            if (candidatePrefab == null) continue;

            var upgrade = candidatePrefab.GetComponent<ItemUpgrade>();
            if (upgrade == null || !upgrade.isPlayerUpgrade) continue;
            if (candidatePrefab.GetComponent<ItemAttributes>() == null) continue;

            if (candidate.name.IndexOf("Grab Strength", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                prefab = candidatePrefab;
                return candidate;
            }

            if (fallback == null)
            {
                fallback = candidate;
                fallbackPrefab = candidatePrefab;
            }
        }

        prefab = fallbackPrefab;
        return fallback;
    }

    private static GameObject ClonePrefab(GameObject basePrefab)
    {
        // Deactivate the source for the instant of the copy so the clone's Awake/OnEnable do not
        // run before we have stripped components off it. Same trick as the HUD slot clones.
        bool wasActive = basePrefab.activeSelf;
        basePrefab.SetActive(false);

        GameObject clone;
        try
        {
            clone = Object.Instantiate(basePrefab);
        }
        finally
        {
            basePrefab.SetActive(wasActive);
        }

        if (clone == null) return null;

        clone.name = ItemAssetName;
        clone.SetActive(false);
        Object.DontDestroyOnLoad(clone);
        return clone;
    }

    /// <summary>
    /// Removes the cloned prop's own upgrade handler (ItemUpgradePlayerGrabStrength and friends),
    /// so using our item cannot also grant the vanilla upgrade it was cloned from. The UnityEvent
    /// that referenced it is muted per-instance by ExtraSlotUpgradeApplier.
    /// </summary>
    private static void StripVanillaUpgradeBehaviour(GameObject prefab)
    {
        var doomed = new List<Component>();
        foreach (var component in prefab.GetComponents<Component>())
        {
            if (component == null) continue;
            string name = component.GetType().Name;
            if (name.StartsWith("ItemUpgradePlayer", StringComparison.Ordinal) ||
                name.StartsWith("ItemUpgradeDeathHead", StringComparison.Ordinal) ||
                name.StartsWith("ItemUpgradeMap", StringComparison.Ordinal))
            {
                doomed.Add(component);
            }
        }

        foreach (var component in doomed)
        {
            Log.Verbose($"Stripped vanilla upgrade behaviour: {component.GetType().Name}.");
            Object.DestroyImmediate(component);
        }

        if (doomed.Count == 0)
        {
            Log.Warn("Found no vanilla upgrade behaviour to strip on the cloned prop. If buying " +
                     "this item also grants another upgrade, that is why — please report it.");
        }
    }

    private static Item CreateItemAsset(Item baseItem, ColorPresets colours)
    {
        var item = ScriptableObject.CreateInstance<Item>();
        item.name = ItemAssetName;
        item.hideFlags = HideFlags.DontUnloadUnusedAsset;

        item.itemName = DisplayName;
        item.itemNameLocalized = null; // ItemAttributes falls back to itemName when this is null
        item.description = "Adds one extra inventory slot. Maximum 3 per player.";
        item.itemType = baseItem.itemType;
        item.emojiIcon = baseItem.emojiIcon;
        item.itemVolume = baseItem.itemVolume;
        item.itemSecretShopType = SemiFunc.itemSecretShopType.none;
        item.colorPreset = colours;
        item.spawnRotationOffset = baseItem.spawnRotationOffset;
        item.physicalItem = baseItem.physicalItem;
        item.disabled = false;

        item.maxAmount = 1;
        item.maxAmountInShop = 1;
        item.minPlayerCount = 1;

        // The purchase cap is Phase 5; leaving this off keeps the item available while testing.
        item.maxPurchase = false;
        item.maxPurchaseAmount = 1;

        // Value is a ScriptableObject shared between items — reusing the base item's asset would
        // reprice the vanilla upgrade we cloned. Always make our own.
        var value = ScriptableObject.CreateInstance<Value>();
        value.name = "Value - Extra Inventory Slot";
        value.hideFlags = HideFlags.DontUnloadUnusedAsset;
        value.valueMin = ToRawValue(Mathf.Min(PluginConfig.PriceMin.Value, PluginConfig.PriceMax.Value));
        value.valueMax = ToRawValue(Mathf.Max(PluginConfig.PriceMin.Value, PluginConfig.PriceMax.Value));
        item.value = value;

        return item;
    }

    /// <summary>
    /// Item.value is not the shop price. ItemAttributes.GetValue computes
    ///     value = Ceil(Random(valueMin, valueMax) * ShopManager.itemValueMultiplier / 1000)
    /// and ShopCostUI renders that as "$&lt;value&gt;K". So a displayed $25,000 needs a raw value of
    /// 25000 / itemValueMultiplier = 6250, not 25000 — setting it literally would price the item
    /// at four times the intent.
    /// </summary>
    private static float ToRawValue(float displayedDollars)
    {
        float multiplier = ShopValueMultiplier();
        float raw = displayedDollars / multiplier;

        Log.Verbose($"Price ${displayedDollars:N0} -> raw value {raw:N1} (multiplier {multiplier}).");
        return raw;
    }

    private static float ShopValueMultiplier()
    {
        // Normally 4. Read it live when the shop exists so a game rebalance does not silently
        // change our price, and fall back to the known constant when it does not.
        var shop = ShopManager.instance;
        if (shop != null && shop.itemValueMultiplier > 0f) return shop.itemValueMultiplier;
        return 4f;
    }

    private static ColorPresets CreatePinkPreset()
    {
        Color main = ParseColour(PluginConfig.UpgradeColor.Value);
        Color.RGBToHSV(main, out float h, out float s, out float v);

        var preset = ScriptableObject.CreateInstance<ColorPresets>();
        preset.name = "Color Preset - Extra Inventory Slot";
        preset.hideFlags = HideFlags.DontUnloadUnusedAsset;
        preset.colorMain = main;
        preset.colorLight = Color.HSVToRGB(h, Mathf.Clamp01(s * 0.6f), Mathf.Clamp01(v * 1.2f));
        preset.colorDark = Color.HSVToRGB(h, Mathf.Clamp01(s * 1.1f), Mathf.Clamp01(v * 0.55f));
        return preset;
    }

    /// <summary>
    /// Pitfall §8: renderer.sharedMaterial is the asset every vanilla upgrade shares. We build a
    /// fresh Material copy per renderer and assign that, so the vanilla props keep their colour.
    /// </summary>
    private static void TintPink(GameObject prefab)
    {
        Color pink = ParseColour(PluginConfig.UpgradeColor.Value);
        int tinted = 0;

        foreach (var renderer in BodyRenderers(prefab))
        {
            if (renderer == null) continue;

            var materials = renderer.sharedMaterials;
            if (materials == null) continue;

            var copies = new Material[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;

                var copy = new Material(materials[i])
                {
                    name = materials[i].name + " (Extra Inventory Slot)",
                    hideFlags = HideFlags.DontUnloadUnusedAsset
                };

                // Preferred path: replace the albedo outright with a generated one. That keeps the
                // vanilla shading and panel detail while making the surface genuinely different,
                // rather than the same texture behind a colour multiplier.
                bool retextured = ApplyGeneratedAlbedo(copy, pink);

                if (!retextured)
                {
                    var applied = new List<string>();
                    foreach (string property in ColorProperties)
                    {
                        if (!copy.HasProperty(property)) continue;
                        copy.SetColor(property, pink);
                        applied.Add(property);
                    }

                    if (applied.Count == 0)
                    {
                        Log.Warn($"Material \"{materials[i].name}\" (shader \"{copy.shader?.name}\") " +
                                 "has no albedo texture and none of the expected colour properties; " +
                                 "it will keep its original look.");
                    }
                    else
                    {
                        Log.Verbose($"Colour-tinted \"{materials[i].name}\" " +
                                    $"(shader \"{copy.shader?.name}\") via {string.Join(", ", applied)}.");
                    }
                }

                copies[i] = copy;
                tinted++;
            }

            renderer.sharedMaterials = copies;
        }

        Log.Info($"Restyled {tinted} material(s) to {PluginConfig.UpgradeColor.Value}.");
    }

    /// <summary>
    /// Only the box body. The prop also carries impact-particle renderers (a shared "flare"
    /// texture) and an ItemEquipCube VFX overlay; repainting those would recolour effects that are
    /// not ours to change, and swapping their albedo would be plainly wrong.
    /// ItemUpgrade.PlayerUpgrade itself addresses the body as transform.Find("Mesh"), so match that
    /// first and fall back to whichever renderer actually carries an upgrade albedo.
    /// </summary>
    private static IEnumerable<Renderer> BodyRenderers(GameObject prefab)
    {
        Transform mesh = prefab.transform.Find("Mesh");
        if (mesh != null)
        {
            var renderers = mesh.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length > 0)
            {
                Log.Verbose($"Restyling the \"Mesh\" body ({renderers.Length} renderer(s)).");
                return renderers;
            }
        }

        var fallback = new List<Renderer>();
        foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer == null) continue;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                if (FindAlbedo(material, out _) == null) continue;
                fallback.Add(renderer);
                break;
            }
        }

        Log.Warn($"No \"Mesh\" child on the upgrade prop; falling back to {fallback.Count} " +
                 "textured renderer(s). Report this if the prop looks wrong.");
        return fallback;
    }

    /// <summary>
    /// Swaps in a generated albedo. Every texture slot holding the same source texture is replaced
    /// (albedo often appears in more than one slot) while normal/roughness maps are left alone.
    /// Colour properties are deliberately not touched on success — the material's original tint is
    /// part of the vanilla shading, and multiplying an already-pink texture by pink muddies it.
    /// </summary>
    private static bool ApplyGeneratedAlbedo(Material material, Color pink)
    {
        Texture source = FindAlbedo(material, out string primaryProperty);
        if (source == null) return false;

        Texture2D generated = UpgradeAlbedo.Generate(source, pink);
        if (generated == null) return false;

        var replaced = new List<string>();
        foreach (string property in material.GetTexturePropertyNames())
        {
            if (material.GetTexture(property) != source) continue;
            material.SetTexture(property, generated);
            replaced.Add(property);
        }

        Log.Verbose($"Replaced albedo \"{source.name}\" on {string.Join(", ", replaced)} " +
                    $"(primary slot {primaryProperty}, shader \"{material.shader?.name}\").");
        return replaced.Count > 0;
    }

    /// <summary>
    /// Finds the albedo without assuming a render pipeline: check the well-known names first, then
    /// fall back to the first populated texture slot that is clearly not a normal or mask map.
    /// </summary>
    private static Texture FindAlbedo(Material material, out string property)
    {
        foreach (string candidate in new[] { "_MainTex", "_BaseMap", "_BaseColorMap", "_AlbedoMap" })
        {
            if (!material.HasProperty(candidate)) continue;
            Texture texture = material.GetTexture(candidate);
            if (texture == null) continue;

            property = candidate;
            return texture;
        }

        foreach (string candidate in material.GetTexturePropertyNames())
        {
            string lower = candidate.ToLowerInvariant();
            if (lower.Contains("normal") || lower.Contains("bump") || lower.Contains("mask") ||
                lower.Contains("metallic") || lower.Contains("occlusion") || lower.Contains("emis") ||
                lower.Contains("height") || lower.Contains("detail") || lower.Contains("specular"))
            {
                continue;
            }

            Texture texture = material.GetTexture(candidate);
            if (texture == null) continue;

            property = candidate;
            return texture;
        }

        property = null;
        return null;
    }

    private static Color ParseColour(string html)
    {
        if (ColorUtility.TryParseHtmlString(html, out Color parsed)) return parsed;

        Log.Warn($"Could not parse UpgradeColor \"{html}\"; falling back to #FF4FA3.");
        ColorUtility.TryParseHtmlString("#FF4FA3", out parsed);
        return parsed;
    }
}
