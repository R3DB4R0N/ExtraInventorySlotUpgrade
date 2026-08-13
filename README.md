# Extra Inventory Slot

Adds a purchasable upgrade to R.E.P.O. that gives you **one extra inventory slot**, up to a
maximum of **6 slots** per player.

The new slots are runtime clones of the vanilla slot, so they use the same sprite, frame, font,
spacing, hover animation and battery/ammo widget. You should not be able to tell which slots are
modded.

![Using the upgrade](https://i.imgur.com/QSOxI3F.gif)

## Features

- **Purchasable upgrade.** A pink upgrade pack appears in the shop and at the upgrade stand
  alongside the vanilla ones, priced at **$25K–$30K** — deliberately more expensive than vanilla
  upgrades. Buy it, take it to the truck, and use it like any other upgrade. It has its own front
  artwork, so it is not mistaken for a recoloured Grab Strength.

![The upgrade in the shop](https://i.imgur.com/pEHqPyh.jpg)

- **Up to 3 purchases per player**, for 6 slots total. Per-player, exactly like Health or Stamina.
- **Number keys 4, 5 and 6** select the new slots, using the game's own input path — same tap to
  equip, tap again to unequip, same cooldown.
- **The whole slot row stays centred.** With 6 slots the HUD re-lays-out symmetrically rather than
  growing off to one side.

![All six slots](https://i.imgur.com/4aDvMI7.jpg)

- **Survives everything vanilla slots survive**: level transitions, the truck, saving and loading,
  and rejoining.
- **Ammo and battery bars work** in the new slots, exactly as in slots 1–3.

## ⚠️ Emote keys 5 and 6

In vanilla, keyboard **5** and **6** are bound to Expression 1 and Expression 2 (emotes). This mod
gives slot selection priority on those keys, so **those two emotes will not fire on 5 and 6 while
the mod is active**.

You have two ways to get them back:

- **Rebind the emotes** in Settings → Controls. The mod matches on the actual binding, not the
  physical key, so once an emote lives on a different key it works again immediately.
- Or set `SuppressConflictingBinds = false` in the config, which lets both fire on the same press.

Emotes 3–6 (keys 7, 8, 9, 0) are untouched. Key 4 is unbound in vanilla, so nothing is lost there.

## Multiplayer

- **Every player needs the mod.** Slot counts are per-player and host-authoritative.
- **The host especially needs it.** If the host does not have the mod the upgrade never spawns, so
  everyone simply stays at 3 slots — no desync, just no extra slots.
- The host clamps every player's upgrade level to its own `MaxExtraSlots` and pushes the correction
  out, so a client cannot claim more slots than the host allows.

## Cap behaviour

Once you own all 3 upgrades, using another one is **blocked and the prop is not consumed** — you
get an on-screen message and the item stays on the ground for a team-mate who still needs it.

![Blocked at maximum slots](https://i.imgur.com/r2zcTum.gif)

The shop cannot filter per player (the host rolls one shop that everyone sees, which is how vanilla
upgrades work too), so this is what stops a maxed player wasting $25K.

## Configuration

`BepInEx/config/extrainventoryslotupgrade.cfg`

| Section | Setting | Default | What it does |
|---|---|---|---|
| Slots | `MaxExtraSlots` | `3` | Extra slots a player can buy, on top of the vanilla 3. |
| Slots | `HostEnforcesCap` | `true` | As host, clamp every player to your cap and broadcast the correction. |
| Input | `SuppressConflictingBinds` | `true` | Slot selection wins over emotes on keys 5 and 6. |
| Input | `SuppressOnlyWhenSlotUnlocked` | `false` | Only mute the emote once you actually own that slot. |
| Shop | `PriceMin` / `PriceMax` | `25000` / `30000` | Shop price range, in the dollars the shop displays. |
| Shop | `PriceIncreasePerPurchase` | `0` | Price inflation per previous purchase. Vanilla upgrades use `0.5`; `0` keeps every roll in range. |
| Shop | `UpgradeColor` | `#FF4FA3` | Colour of the non-front faces of the prop. |
| Shop | `AlbedoResolution` | `1024` | Resolution of the generated prop texture. Higher keeps the front art crisper up close. |
| Shop | `FrontFaceUV` | *(measured)* | UV rectangle of the box's front panel. Only needed if a game update moves it. |
| Shop | `SideBleedUV` | *(measured)* | Region of the side panel where vanilla art spilled over the fold, healed automatically. |
| Debug | `DebugForceExtraSlots` | `0` | Force a slot count regardless of upgrades. `0` = off. Host/solo only. |
| Debug | `DumpPropTextures` | `false` | Development aid: dump the vanilla prop's textures and UV layout. |
| Debug | `VerboseLogging` | `false` | Detailed logging for bug reports. |

There is also an `/eis` chat command: `/eis slots <0-3>` and `/eis status`. Forcing a slot count
works only as the host or in solo play, so it cannot be used to hand yourself slots in someone
else's lobby.

## Known conflicts

Do not run this alongside **MoreInventorySlots** (nickklmao) or **ExtraInventorySlots**
(DarkSpider) — they do overlapping things and you will get duplicate slots. The mod detects them at
startup and logs a loud warning.

## Reporting a bug

Set `VerboseLogging = true`, reproduce the problem, and include `BepInEx/LogOutput.log`. The mod
logs its HUD layout and slot construction in detail, which usually pinpoints the cause immediately.

## Building from source

Requires the .NET SDK and a local R.E.P.O. install — the project references the game's own
assemblies rather than a NuGet package, so it always compiles against your exact game version.

1. Edit `Directory.Repo.props` and point `GameDirectory` at your install.
2. `dotnet build -c Release`

The DLL is copied straight into `BepInEx/plugins/ExtraInventorySlotUpgrade/` after every build.
To produce the Thunderstore zip:

```
powershell -ExecutionPolicy Bypass -File tools/package.ps1
```

## Credits

Built on [REPOLib](https://thunderstore.io/c/repo/p/Zehs/REPOLib/) by Zehs.
