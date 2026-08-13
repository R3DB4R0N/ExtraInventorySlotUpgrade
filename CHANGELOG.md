# Changelog

## 1.0.0

Initial release.

- Purchasable pink upgrade pack, $25K–$30K, sold in the shop and at the upgrade stand, with custom
  front artwork generated onto the vanilla upgrade box at load time.
- The price stays inside that range across the whole run. Vanilla inflates every upgrade by 50% of
  its base for each prior purchase, which is unremarkable on a 5K upgrade but sends a 25K one past
  80K; configurable via `PriceIncreasePerPurchase` if you want the vanilla escalation back.
- Up to 3 purchases per player, for a maximum of 6 inventory slots.
- Slots 4–6 are runtime clones of the vanilla slot: same sprite, frame, font, spacing, hover
  animation, slot number and ammo/battery widget.
- The slot row stays centred on screen as it grows.
- Number keys 4, 5 and 6 select the new slots through the game's own input path. Emotes 1 and 2
  are suppressed on keys 5 and 6 while the mod is active (configurable).
- Slot contents survive level transitions, the truck, saving/loading and rejoining, and sync to
  other players the same way slots 1–3 do.
- Host-authoritative: the host clamps every player's upgrade level and broadcasts the correction.
- Using the upgrade at max slots is blocked without consuming the item, so nobody wastes $25K.
- Save files remain compatible in both directions: a 6-slot save loads without the mod, and a save
  made without the mod loads with it.
