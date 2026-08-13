using REPOLib.Modules;
using UnityEngine;
using UnityEngine.Events;

namespace ExtraInventorySlotUpgrade;

/// <summary>
/// Sits on the pink upgrade prop and turns "player used the item" into a REPOLib level increment.
/// Functionally REPOLib's own REPOLibItemUpgrade, reimplemented here only because its _upgradeId is
/// a private [SerializeField] we would otherwise have to poke with reflection.
///
/// This must wire itself per instance rather than being wired on the template: UnityEvent listeners
/// added at runtime are not serialized, so Object.Instantiate does not copy them.
/// </summary>
public class ExtraSlotUpgradeApplier : MonoBehaviour
{
    private ItemToggle _itemToggle;
    private ItemUpgrade _itemUpgrade;
    private bool _wired;

    private void Awake() => Wire();

    private void Start() => Wire();

    private void Wire()
    {
        if (_wired) return;

        _itemUpgrade = GetComponent<ItemUpgrade>();
        _itemToggle = GetComponent<ItemToggle>();

        if (_itemUpgrade == null || _itemUpgrade.upgradeEvent == null)
        {
            Log.Error("Upgrade prop has no ItemUpgrade component; it will do nothing when used.");
            return;
        }

        // The prop was cloned from a vanilla upgrade, so its UnityEvent still points at that
        // upgrade's handler. Mute the inherited persistent calls instead of trying to delete them
        // (persistent listeners cannot be removed at runtime) and add ours alongside.
        int persistent = _itemUpgrade.upgradeEvent.GetPersistentEventCount();
        for (int i = 0; i < persistent; i++)
        {
            _itemUpgrade.upgradeEvent.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        _itemUpgrade.upgradeEvent.RemoveListener(Apply);
        _itemUpgrade.upgradeEvent.AddListener(Apply);
        _wired = true;

        Log.Verbose($"Upgrade prop wired ({persistent} inherited listener(s) muted).");
    }

    /// <summary>
    /// Invoked by ItemUpgrade.PlayerUpgrade via upgradeEvent. Only the buyer's own client calls
    /// AddLevel; REPOLib networks the resulting level to everyone else.
    /// </summary>
    public void Apply()
    {
        if (_itemToggle == null)
        {
            Log.Error("Upgrade prop has no ItemToggle; cannot tell who used it.");
            return;
        }

        PlayerAvatar player = SemiFunc.PlayerAvatarGetFromPhotonID(_itemToggle.playerTogglePhotonID);
        if (player == null)
        {
            Log.Error($"Could not resolve the player from photon ID {_itemToggle.playerTogglePhotonID}.");
            return;
        }

        if (!player.isLocal) return;

        if (!Upgrades.TryGetUpgrade(UpgradeItemFactory.UpgradeId, out PlayerUpgrade upgrade))
        {
            Log.Error($"Upgrade \"{UpgradeItemFactory.UpgradeId}\" is not registered.");
            return;
        }

        int level = upgrade.AddLevel(player);
        Log.Info($"{SemiFunc.PlayerGetName(player)} bought an extra inventory slot (level {level}).");
    }
}
