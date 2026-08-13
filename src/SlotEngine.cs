using System;
using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ExtraInventorySlotUpgrade;

/// <summary>
/// Owns the local player's slot count and the HUD objects for slots 4-6.
///
/// Slots 4-6 are runtime clones of the vanilla slot-3 GameObject, re-parented to the same
/// container. Cloning rather than rebuilding is what guarantees requirement 6 (indistinguishable
/// from vanilla) — identical sprite, frame, font, scale, spring animation and battery widget.
///
/// The awkward part is that SemiUI.Start rewrites its own serialized fields in place:
///     if (showPosition == Vector2.zero) showPosition = transform.localPosition;
///     hidePosition += showPosition;
/// and InventorySpot.Start deactivates the battery child. A clone taken from an already-started
/// slot therefore inherits post-Start state, which makes its own Start compute garbage: it keeps
/// slot 3's showPosition (landing on top of slot 3), double-accumulates hidePosition, and cannot
/// find the now-inactive BatteryVisualLogic via GetComponentInChildren (which skips inactive
/// objects) — that last one is a hard NullReferenceException inside vanilla Start.
///
/// So after instantiating we rewind the clone to authored-equivalent state and let vanilla Start
/// do the real initialisation. That keeps behaviour identical instead of reimplemented.
/// </summary>
internal static class SlotEngine
{
    public const int VanillaSlots = ExtraSlotStats.VanillaSlots;

    private static readonly List<GameObject> Clones = new();

    /// <summary>Untouched resting positions of the vanilla slots, so we can always put them back.</summary>
    private static readonly Dictionary<int, (Vector2 Show, Vector2 Hide)> VanillaRest = new();
    private static Inventory _vanillaRestFor;

    private static Inventory _builtFor;
    private static int _builtExtra = -1;
    private static bool _dumpedLayout;
    private static int _spacingWarnings;

    /// <summary>Extra slots earned through the upgrade. Phase 4 drives this; 0 until then.</summary>
    public static int UpgradeExtraSlots { get; private set; }

    /// <summary>Extra slots actually built right now (already clamped).</summary>
    public static int ExtraSlots { get; private set; }

    public static int TotalSlots => VanillaSlots + ExtraSlots;

    public static int ConfiguredMaxExtra =>
        Mathf.Clamp(PluginConfig.MaxExtraSlots.Value, 0, PluginConfig.AbsoluteMaxExtraSlots);

    /// <summary>
    /// Set the local player's earned extra-slot count. Layer 1 of the purchase cap (§6): the clamp
    /// lives here, so no upgrade level, corrupted save or hostile client can produce a 7th slot.
    /// </summary>
    public static void SetUpgradeExtraSlots(int extra)
    {
        int clamped = Mathf.Clamp(extra, 0, ConfiguredMaxExtra);

        // Called every frame from Tick, so only speak up when something actually changes.
        if (clamped != extra && extra != _lastClampWarnedFor)
        {
            Log.Warn($"Extra slot count {extra} clamped to {clamped}.");
            _lastClampWarnedFor = extra;
        }

        UpgradeExtraSlots = clamped;
    }

    private static int _lastClampWarnedFor = int.MinValue;
    private static bool _warnedAboutForcedSlots;

    /// <summary>Cheap per-frame reconciliation. Handles level loads, HUD teardown and respawns.</summary>
    public static void Tick()
    {
        // Poll the authoritative level rather than trusting the last callback: a host sync replaces
        // the upgrade dictionary wholesale without invoking REPOLib's ApplyUpgrade, so a clamp that
        // arrives mid-run would otherwise never reach the HUD.
        SetUpgradeExtraSlots(ExtraSlotUpgrade.GetLocalLevel());

        // The debug override wins while non-zero, and releases cleanly back to the upgrade-driven
        // count when set back to 0.
        int forced = Mathf.Clamp(PluginConfig.DebugForceExtraSlots.Value, 0, ConfiguredMaxExtra);

        // It bypasses the upgrade entirely, so as a client it would be a straight cheat: the host
        // records items in slots 4-6 for whoever claims them, with no upgrade ever bought. Honour
        // it only where it cannot take anything from anyone else.
        if (forced > 0 && !SemiFunc.IsMasterClientOrSingleplayer())
        {
            if (!_warnedAboutForcedSlots)
            {
                _warnedAboutForcedSlots = true;
                Log.Warn("DebugForceExtraSlots is ignored when you are not the host. Buy the " +
                         "upgrade, or host the lobby yourself.");
            }
            forced = 0;
        }

        int desired = forced > 0 ? forced : UpgradeExtraSlots;

        if (desired != ExtraSlots)
        {
            ExtraSlots = desired;
            _builtExtra = -1;
            Log.Info($"Local extra slot count -> {ExtraSlots} ({TotalSlots} total).");
        }

        var inv = Inventory.instance;
        if (inv == null) return;
        if (SemiFunc.RunIsArena()) return;

        var spots = inv.inventorySpots;
        if (spots.Count < VanillaSlots) return;

        for (int i = 0; i < VanillaSlots; i++)
        {
            if (spots[i] == null) return; // vanilla still registering
        }

        if (_builtFor == inv && _builtExtra == ExtraSlots && ClonesHealthy(spots)) return;

        Rebuild(inv);
    }

    private static bool ClonesHealthy(List<InventorySpot> spots)
    {
        if (spots.Count != VanillaSlots + ExtraSlots) return false;
        for (int i = VanillaSlots; i < spots.Count; i++)
        {
            if (spots[i] == null) return false;
        }
        return true;
    }

    private static void Rebuild(Inventory inv)
    {
        var spots = inv.inventorySpots;

        // Only start from scratch when we have to. Buying a second slot while the first one holds
        // an item must not tear the first one down — that is what dropped your item on the floor.
        if (_builtFor != inv || HasDamagedClones(spots)) TearDownAll(spots);

        CaptureVanillaRest(inv, spots);

        InventorySpot template = spots[VanillaSlots - 1];  // vanilla slot 3
        if (template == null) return;

        Transform parent = template.transform.parent;
        bool hasLayoutGroup = parent != null && parent.GetComponent<LayoutGroup>() != null;

        // showPosition is SemiUI's resting position (UpdatePositionLogic drives localPosition from
        // hidePositionCurrent, which lerps toward showPosition). It is the only position field not
        // animated frame to frame, so it is the one to measure spacing with.
        Vector2 step = VanillaRest[VanillaSlots - 1].Show - VanillaRest[VanillaSlots - 2].Show;

        DumpLayout(inv, template, parent, hasLayoutGroup, step);

        if (ExtraSlots <= 0 && spots.Count == VanillaSlots)
        {
            RepositionStartedSlots(spots, VanillaSlots, hasLayoutGroup);
            _builtFor = inv;
            _builtExtra = 0;
            return;
        }

        if (!hasLayoutGroup && step == Vector2.zero)
        {
            // Most likely cause: vanilla Start has not run yet, so showPosition is still (0,0).
            // Retry next frame rather than building a broken HUD.
            if (_spacingWarnings++ < 3)
            {
                Log.Warn("Slot spacing not resolvable yet (slot 2 and 3 showPosition are equal). " +
                         "Retrying next frame.");
            }
            VanillaRest.Clear();
            _vanillaRestFor = null;
            return;
        }
        _spacingWarnings = 0;

        int total = VanillaSlots + ExtraSlots;

        // 1. Shrink first, releasing only the slots that actually go away.
        while (spots.Count > total) ReleaseAndDestroyLastClone(spots);

        // 2. Re-lay-out everything that already exists. The vanilla row is centred on screen
        //    (slots at -40 / 0 / +40 around x=0), so appending on the right would shove it
        //    off-centre; every slot gets repositioned symmetrically about the original centre.
        //    These have all run Start, so they move via showPosition.
        RepositionStartedSlots(spots, total, hasLayoutGroup);

        // 3. Grow. New clones are placed directly at their final position, because their own
        //    Start captures showPosition from localPosition.
        int added = 0;
        while (spots.Count < total)
        {
            int slot = spots.Count;
            GameObject clone = CloneSpot(template, parent, RestingPositionFor(slot, total),
                                         hasLayoutGroup, slot);
            if (clone == null) break;

            spots.Add(clone.GetComponent<InventorySpot>());
            Clones.Add(clone);
            added++;
        }

        inv.spotsFeched = spots.Count == total;

        _builtFor = inv;
        _builtExtra = spots.Count - VanillaSlots;
        Log.Info($"Slots reconciled: {spots.Count} total ({added} added this pass).");
    }

    private static void RepositionStartedSlots(List<InventorySpot> spots, int total, bool hasLayoutGroup)
    {
        for (int slot = 0; slot < spots.Count; slot++)
        {
            MoveStartedSlot(spots[slot], RestingPositionFor(slot, total), hasLayoutGroup);
        }
    }

    private static bool HasDamagedClones(List<InventorySpot> spots)
    {
        for (int i = VanillaSlots; i < spots.Count; i++)
        {
            if (spots[i] == null) return true;
        }
        return Clones.Exists(c => c == null);
    }

    /// <summary>
    /// Evenly spaced, centred on wherever the vanilla three were centred. For total == 3 this
    /// reproduces the vanilla positions exactly, which is what makes teardown a no-op.
    /// </summary>
    private static Vector2 RestingPositionFor(int slot, int total)
    {
        Vector2 centre = Vector2.zero;
        for (int i = 0; i < VanillaSlots; i++) centre += VanillaRest[i].Show;
        centre /= VanillaSlots;

        Vector2 step = VanillaRest[VanillaSlots - 1].Show - VanillaRest[VanillaSlots - 2].Show;
        return centre + step * (slot - (total - 1) / 2f);
    }

    private static void CaptureVanillaRest(Inventory inv, List<InventorySpot> spots)
    {
        if (_vanillaRestFor == inv && VanillaRest.Count == VanillaSlots) return;

        VanillaRest.Clear();
        for (int slot = 0; slot < VanillaSlots; slot++)
        {
            var spot = spots[slot];
            if (spot == null) return;
            VanillaRest[slot] = (spot.showPosition, spot.hidePosition);
        }
        _vanillaRestFor = inv;
        Log.Verbose("Captured vanilla slot resting positions.");
    }

    /// <summary>
    /// Slide an already-started vanilla slot to a new resting position. hidePosition is absolute by
    /// this point (Start did `hidePosition += showPosition`), so shift it by the same delta to keep
    /// the slide-in animation identical. SemiUI.HideAnimationLogic lerps toward the new
    /// showPosition on its own, so the move animates rather than snapping.
    /// </summary>
    private static void MoveStartedSlot(InventorySpot spot, Vector2 target, bool hasLayoutGroup)
    {
        if (spot == null || hasLayoutGroup) return;

        if (spot.showPosition == Vector2.zero)
        {
            // Start has not run yet, so showPosition is still the sentinel it recaptures from.
            // Moving the transform is the correct lever at this point.
            spot.transform.localPosition =
                new Vector3(target.x, target.y, spot.transform.localPosition.z);
            return;
        }

        Vector2 hideOffset = spot.hidePosition - spot.showPosition;
        spot.showPosition = target;
        spot.hidePosition = target + hideOffset;
    }

    private static GameObject CloneSpot(
        InventorySpot template, Transform parent, Vector2 target, bool hasLayoutGroup, int slot)
    {
        // Instantiate from a momentarily-inactive template so the clone's Awake/OnEnable/Start do
        // not run until we have finished rewinding and configuring it.
        bool wasActive = template.gameObject.activeSelf;
        template.gameObject.SetActive(false);

        GameObject clone;
        try
        {
            clone = Object.Instantiate(template.gameObject, parent, false);
        }
        finally
        {
            template.gameObject.SetActive(wasActive);
        }

        if (clone == null) return null;

        clone.name = $"Inventory Spot {slot + 1} (ExtraInventorySlot)";
        clone.SetActive(false);

        var spot = clone.GetComponent<InventorySpot>();
        if (spot == null)
        {
            Log.Error("Cloned slot has no InventorySpot component. Skipping this slot.");
            Object.Destroy(clone);
            return null;
        }

        spot.inventorySpotIndex = slot;

        // The battery pip reads its own slot index, independently of InventorySpot.
        foreach (var battery in clone.GetComponentsInChildren<InventoryBattery>(true))
        {
            battery.inventorySpot = slot;
        }

        // A duplicated PhotonView would try to claim an already-registered ViewID. InventorySpot
        // caches one but never uses it, so dropping it is free. DestroyImmediate while the clone is
        // still inactive, before PhotonView.OnEnable can run.
        var photonView = clone.GetComponent<PhotonView>();
        if (photonView != null)
        {
            Object.DestroyImmediate(photonView);
            Log.Verbose($"Stripped duplicate PhotonView from slot {slot + 1}.");
        }

        RelabelSlotNumber(clone, template, slot);
        RewindSemiUiState(template, spot, target, hasLayoutGroup, slot);
        PrepareBatteryWidget(clone, template, slot);

        clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + (slot - (VanillaSlots - 1)));
        clone.SetActive(true);

        // Awake ran synchronously on that SetActive, so the cached originals are readable now.
        // A zero here means the widget can never scale up and the ammo/charge bars stay invisible.
        var widget = clone.GetComponentInChildren<BatteryVisualLogic>(true);
        if (widget != null)
        {
            if (widget.targetScaleOriginal <= 0.001f)
            {
                Log.Error($"Slot {slot + 1}: battery widget cached targetScaleOriginal " +
                          $"{widget.targetScaleOriginal} — ammo/charge bars will be invisible.");
            }
            else
            {
                Log.Verbose($"Slot {slot + 1}: battery widget cached targetScaleOriginal " +
                            $"{widget.targetScaleOriginal:0.###}.");
            }
        }

        return clone;
    }

    /// <summary>
    /// A slot draws its number through two separate labels, swapped by InventorySpot.SetEmoji:
    /// the "Numbers" child while an item is held, and the "No Item" child (InventorySpot.noItem)
    /// while the slot is empty. SetEmoji only toggles .enabled and never touches the text, so both
    /// keep whatever the clone inherited from slot 3 — hence 1,2,3,3,3,3 on empty slots.
    ///
    /// Relabel anything that either is named like a number label or currently reads the template's
    /// own slot number; that covers both without hardcoding the child names.
    /// </summary>
    private static void RelabelSlotNumber(GameObject clone, InventorySpot template, int slot)
    {
        string templateNumber = (template.inventorySpotIndex + 1).ToString();
        string wanted = (slot + 1).ToString();
        var relabelled = new List<string>();

        foreach (var label in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label == null) continue;

            bool namedLikeNumber =
                label.gameObject.name.StartsWith("Numbers", StringComparison.OrdinalIgnoreCase);
            bool readsTemplateNumber =
                label.text != null && label.text.Trim() == templateNumber;

            if (!namedLikeNumber && !readsTemplateNumber) continue;

            label.text = wanted;
            relabelled.Add(label.gameObject.name);
        }

        if (relabelled.Count > 0)
        {
            Log.Verbose($"Slot {slot + 1}: relabelled {string.Join(", ", relabelled)} to \"{wanted}\".");
        }
        else
        {
            Log.Warn($"Slot {slot + 1}: found no number label; it will read \"{templateNumber}\".");
        }
    }

    /// <summary>
    /// Undo the in-place field mutations SemiUI.Start performed on the template, so the clone's own
    /// Start sees authored-equivalent input and initialises exactly the way slots 1-3 did.
    /// </summary>
    private static void RewindSemiUiState(
        InventorySpot template, InventorySpot clone, Vector2 target, bool hasLayoutGroup, int slot)
    {
        // Start does `hidePosition += showPosition`, so the clone inherited an absolute value. Put
        // back the authored relative one, or the slide-in animation starts twice as far out.
        clone.hidePosition = template.hidePosition - template.showPosition;

        // Zero it so the clone's Start recaptures it from localPosition (its authored behaviour)
        // instead of keeping slot 3's value, which is what stacked the clones on top of slot 3.
        clone.showPosition = Vector2.zero;
        clone.isHidden = false;

        if (!hasLayoutGroup)
        {
            clone.transform.localPosition =
                new Vector3(target.x, target.y, template.transform.localPosition.z);
        }

        // The template's live localScale may be mid spring-animation; originalScale is its rest
        // value, and SemiUI.Start caches originalScale from whatever it finds.
        if (template.originalScale != Vector3.zero)
        {
            clone.transform.localScale = template.originalScale;
        }

        Log.Verbose($"Slot {slot + 1}: resting position {target}, " +
                    $"hidePosition {clone.hidePosition}, scale {clone.transform.localScale}.");
    }

    /// <summary>
    /// Rehabilitates the cloned battery/charge widget. Three separate problems, all caused by
    /// cloning a live object rather than a prefab:
    ///
    /// 1. InventorySpot.Start does GetComponentInChildren&lt;BatteryVisualLogic&gt;() — which skips
    ///    inactive objects — and dereferences the result. Vanilla Start had already deactivated
    ///    that widget, so the clone hits a NullReferenceException. Only the chain from the clone
    ///    root down to the widget is reactivated; its own children are left exactly as inherited,
    ///    because BatteryVisualLogic owns their active states and re-enabling them by hand is what
    ///    produced the mixed-colour overlay.
    ///
    /// 2. BatteryVisualLogic.bars is an internal (unserialized) List, so Instantiate copies the
    ///    three "Battery Bar(Clone)" child objects but not the list that owns them. Start then adds
    ///    three more, leaving six bars — three of them orphaned at the previous item's colour.
    ///    Destroy the inherited ones so Start starts from nothing, exactly like a fresh slot.
    ///
    /// 3. An empty slot has run BatteryOutro, which drives targetScale to 0. Awake caches that as
    ///    targetScaleOriginal, so ResetOutro can only ever restore 0 and the widget stays invisible
    ///    forever. Restore the template's original transform values before Awake runs.
    /// </summary>
    private static void PrepareBatteryWidget(GameObject clone, InventorySpot template, int slot)
    {
        var templateWidget = template.GetComponentInChildren<BatteryVisualLogic>(true);

        foreach (var widget in clone.GetComponentsInChildren<BatteryVisualLogic>(true))
        {
            if (widget == null) continue;

            // (1) Drop the inherited runtime bars.
            if (widget.batteryBarContainer != null)
            {
                var stale = new List<GameObject>();
                foreach (Transform child in widget.batteryBarContainer)
                {
                    if (child != null) stale.Add(child.gameObject);
                }
                foreach (var bar in stale) Object.DestroyImmediate(bar);

                if (stale.Count > 0)
                {
                    Log.Verbose($"Slot {slot + 1}: removed {stale.Count} inherited battery bar(s).");
                }
            }

            // (2) Restore the rest pose Awake will cache.
            //
            // ORDER IS LOAD-BEARING: everything that Awake reads has to be correct *before* the
            // widget's hierarchy goes active, because Awake fires synchronously on activation and
            // caches targetScaleOriginal from transform.localScale. Reactivating first and fixing
            // the transform afterwards silently caches a scale of ~0, and ResetOutro can then only
            // ever restore 0 — the widget exists, updates, and stays invisible forever.
            if (templateWidget != null)
            {
                float restScale = templateWidget.targetScaleOriginal > 0.001f
                    ? templateWidget.targetScaleOriginal
                    : 1f;
                widget.transform.localScale = new Vector3(restScale, restScale, restScale);
                widget.transform.localPosition = templateWidget.targetPositionOriginal;
                widget.transform.localRotation =
                    Quaternion.Euler(0f, 0f, templateWidget.targetRotationOriginal);

                // Start reads these as the "full" scales for the charge/drain animations; the
                // template's live values may be mid-lerp.
                if (widget.batteryBarDrain != null && templateWidget.batteryDrainFullXScale > 0f)
                {
                    var s = widget.batteryBarDrain.localScale;
                    widget.batteryBarDrain.localScale =
                        new Vector3(templateWidget.batteryDrainFullXScale, s.y, s.z);
                }
                if (widget.batteryBarCharge != null && templateWidget.batteryChargeFullXScale > 0f)
                {
                    var s = widget.batteryBarCharge.localScale;
                    widget.batteryBarCharge.localScale =
                        new Vector3(templateWidget.batteryChargeFullXScale, s.y, s.z);
                }
            }
            else if (widget.transform.localScale.x <= 0.001f)
            {
                widget.transform.localScale = Vector3.one;
            }

            // Stale references to whatever the template was holding.
            widget.itemBattery = null;
            widget.doOutro = false;

            // (3) Only now reactivate, and only the chain from the widget up to — but excluding —
            // the clone root, which CloneSpot activates last once everything else is configured.
            for (Transform t = widget.transform; t != null && t.gameObject != clone; t = t.parent)
            {
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            }

            Log.Verbose($"Slot {slot + 1}: battery widget rest scale " +
                        $"{widget.transform.localScale.x:0.###}, position {widget.transform.localPosition}.");
        }

        if (clone.GetComponentInChildren<BatteryVisualLogic>(true) == null)
        {
            Log.Error($"Slot {slot + 1}: no BatteryVisualLogic at all. InventorySpot.Start will " +
                      "throw. Please report the layout dump.");
        }
    }

    /// <summary>Drops the highest extra slot, releasing its item so nothing is stranded.</summary>
    private static void ReleaseAndDestroyLastClone(List<InventorySpot> spots)
    {
        int index = spots.Count - 1;
        if (index < VanillaSlots) return;

        var spot = spots[index];
        if (spot != null && spot.IsOccupied() && spot.CurrentItem != null)
        {
            Log.Verbose($"Releasing item from slot {index + 1} before removing it.");
            spot.CurrentItem.RequestUnequip();
        }

        spots.RemoveAt(index);

        int cloneIndex = index - VanillaSlots;
        if (cloneIndex >= 0 && cloneIndex < Clones.Count)
        {
            if (Clones[cloneIndex] != null) Object.Destroy(Clones[cloneIndex]);
            Clones.RemoveAt(cloneIndex);
        }
    }

    /// <summary>Full reset — only used when the Inventory changed or a clone was destroyed on us.</summary>
    private static void TearDownAll(List<InventorySpot> spots)
    {
        while (spots.Count > VanillaSlots) ReleaseAndDestroyLastClone(spots);

        foreach (var clone in Clones)
        {
            if (clone != null) Object.Destroy(clone);
        }
        Clones.Clear();

        // Put the vanilla slots back where the game left them, so "0 extra slots" is pixel-vanilla.
        for (int slot = 0; slot < VanillaSlots && slot < spots.Count; slot++)
        {
            if (spots[slot] == null || !VanillaRest.ContainsKey(slot)) continue;
            spots[slot].showPosition = VanillaRest[slot].Show;
            spots[slot].hidePosition = VanillaRest[slot].Hide;
        }

        _builtFor = null;
        _builtExtra = -1;
    }

    /// <summary>
    /// Recon dump of the live HUD, one line per log entry so nothing is lost to multi-line
    /// formatting. Logged once at Info level, then every rebuild when verbose logging is on.
    /// </summary>
    private static void DumpLayout(
        Inventory inv, InventorySpot template, Transform parent, bool hasLayoutGroup, Vector2 step)
    {
        if (_dumpedLayout && !PluginConfig.VerboseLogging.Value) return;
        _dumpedLayout = true;

        Log.Info("--- inventory HUD layout ---");
        Log.Info($"  Inventory owner : {inv.gameObject.name}");
        Log.Info($"  Slot container  : {DescribePath(parent)}");
        Log.Info($"  Container comps : {string.Join(", ", ComponentNames(parent.gameObject))}");
        Log.Info($"  LayoutGroup     : {(hasLayoutGroup ? parent.GetComponent<LayoutGroup>().GetType().Name : "none")}");
        Log.Info($"  Inferred step   : {step}");

        for (int i = 0; i < inv.inventorySpots.Count; i++)
        {
            var spot = inv.inventorySpots[i];
            if (spot == null)
            {
                Log.Info($"  Slot {i + 1}: <null>");
                continue;
            }
            Log.Info($"  Slot {i + 1}: {spot.gameObject.name} show={spot.showPosition} " +
                     $"hide={spot.hidePosition} local={spot.transform.localPosition} " +
                     $"scale={spot.transform.localScale} origScale={spot.originalScale} " +
                     $"animateAll={spot.animateTheEntireObject} sibling={spot.transform.GetSiblingIndex()}");
        }

        Log.Info($"  Template components: {string.Join(", ", ComponentNames(template.gameObject))}");
    }

    private static List<string> ComponentNames(GameObject go)
    {
        var names = new List<string>();
        foreach (var c in go.GetComponents<Component>())
        {
            if (c != null) names.Add(c.GetType().Name);
        }
        return names;
    }

    private static string DescribePath(Transform t)
    {
        if (t == null) return "<no parent>";
        string path = t.name;
        var cur = t.parent;
        while (cur != null)
        {
            path = cur.name + "/" + path;
            cur = cur.parent;
        }
        return path;
    }
}
