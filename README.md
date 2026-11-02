# PING – Portal: Icarus Neural Gateway

A mod for **Dyson Sphere Program** that adds paired interstellar teleportation portals. 
Two portals on two different planets form a gateway. Walk within range (30 m), set your target via the Interface on opening one Portal, wait until the portal is charged – and travel.

It works in normal games; no sandbox tools are needed.

- **Mod GUID:** `org.dspmod.plugin.ping`
- **Version:** 1.0.1
- **Dependencies:** BepInEx, CommonAPI, LDBTool, DSPModSave, EnableAchievementsFix

> Status: work in progress. Report problems together with your `BepInEx/LogOutput.log`.


## Features

- **Paired Portals**: Both ends of the teleport need a PING portal. One-way travel (energy consumption at the sender side only).
- **Accumulator-style Charging**: Portals charge just like Accumulators from the power grid. Max storage 10 GJ (≈ 50 LY).
- **Distance-based Energy Cost**: 192 MJ per Light-Year (exactly the energy needed for a Warper jump, minus sail acceleration overhead) 
- **Max charge rate**: 150 MW by default (configurable).
- **Crafting only in Assemblers** (cannot be hand-crafted in the Replicator). Stack size 5. Crafting time 40 s.
- **Galactic Map Integration**: a new UI: the PING uses a list of all Planets. You can filter and search the list.

## Installation

### With r2modman

1. Install the dependencies (CommonAPI, LDBTool, DSPModSave) through r2modman.
2. Import the mod.
3. Make sure the file **`pingmod`** (the model AssetBundle, no extension) is in the **same folder as the mod DLL**.
   r2modman's "import local mod" copies only the DLL, so you may need to copy `pingmod` into the profile by hand, for example:
   `.../profiles/<your profile>/BepInEx/plugins/<mod folder>/`

If the file is missing, the mod still works. It then uses the vanilla accumulator model and writes `model bundle not found` to the log.

### Manual

Copy the DLL and the `pingmod` file together into `BepInEx/plugins/<any folder>/`.


## Achievements
 
The teleport moves your mecha in a way the game's abnormality check reports. PING therefore depends on [EnableAchievementsFix](https://thunderstore.io/c/dyson-sphere-program/p/IlldariFairlight/EnableAchievementsFix/), which keeps Steam achievements working with mods. It is installed automatically. Without it, using PING can mark your save as abnormal and disable achievements and do weird stuff to Savegames. 

---

## Recipe (Interstellar Power Transmission required)

| Material | Count | 
|---|---|
| Quantum Chip | 20 | 
| Graviton Lens | 25 | 
| Space Warper | 50 | 
| Particle Broadband | 25 | 
| Accumulator (empty) | 40 |

- **Build Bar**: Tab 6 (Logistics), Slot 1
- **Requires**: Interstellar Power Transmission research

## Requirements

- [BepInEx](https://dsp.thunderstore.io/package/xiaoye97/BepInEx/) 5.4.17+
- [LDBTool](https://dsp.thunderstore.io/package/xiaoye97/LDBTool/) 3.0.3+
- [CommonAPI](https://dsp.thunderstore.io/package/CommonAPI/CommonAPI/) 1.6.7+ (LocalizationModule, ProtoRegistry, CustomDescSystem, UtilSystem)
- [DSPModSave](https://dsp.thunderstore.io/package/CommonAPI/DSPModSave/) 1.2.2+ (IModCanSave to persist energy storage / target planets)

## Usage

1. Build a PING portal on the **origin** planet and one on the **destination** planet.
   A destination needs its own portal. Planets without one are not offered.
2. Let the origin portal charge. The energy bar in the window shows the stored energy.
3. Stand within range of the portal and **click it** to open the destination window.
4. Pick a destination in the list. Use the filters or the search field if needed:
   - portal filter: only planets that have a PING portal
   - "<= N ly": only targets within range
   - "This star": only planets of the current star
5. Click **Teleport**. A row click only selects the target; nothing happens until you press the button.
   If the target cannot be used, the status line says why (not enough energy, too far, cooldown, no portal there).

The cost of a trip is **192 MJ per light-year**. The portal stores up to **10 GJ**, which is enough for the full 50 ly range (9.6 GJ).

## Configuration

The file is `BepInEx/config/org.dspmod.plugin.ping.cfg`. It is created on first start.

| Setting | Default | Meaning |
|---|---|---|
| `EnergyPerLightYearMJ` | 192 | Energy cost per light-year |
| `MaxTravelDistanceLY` | 50 | Maximum jump distance |
| `MaxEnergyStoredGJ` | 10 | Energy capacity of one portal |
| `ChargingPowerMW` | 150 | Power drawn from the grid while charging |
| `CooldownSeconds` | 10 | Wait time between two jumps |
| `GateRangeMeters` | 30 | How close you must be to use the portal |
| `WindowOpacity` | 0.78 | Background opacity of the destination window |
| `RemoveAllPortals` | false | Uninstall helper (see below) |

## Uninstalling safely

If you remove the mod while PING portals still exist in a save, the game may complain about unknown buildings.
To avoid this:

1. Set `RemoveAllPortals = true` in the config and load your save.
2. The mod dismantles every PING portal (one per planet when it is loaded) and returns one portal item to your inventory for each. 
3. Save, quit, and then remove the mod.

Visit all planets that have portals before you remove the mod, because planets are processed when they are loaded!
Make a backup of the save first!

---

## Troubleshooting

| Problem | What to check |
|---|---|
| Building is the vanilla accumulator | `pingmod` is not next to the DLL. Look for `model bundle not found` in the log. |
| Building does not charge | It must be inside a power field. Look at the `PING power` lines in the log. |
| No destination in the list | The destination planet needs a PING portal. Check the portal filter. |
| Window does not open | Click the portal itself. Check for errors in the log from `PingUiPatch`. |

---


## Credits / Licensing

This mod bundles or references the following third-party assets:

### 3D Model: Sci-fi Portal
- **Author**: [Sousinho](https://sketchfab.com/Sousinho)
- **Source**: https://skfb.ly/oqQyv
- **License**: [Creative Commons Attribution 4.0 (CC-BY-4.0)](https://creativecommons.org/licenses/by/4.0/)
- **Modifications**: Resized / retextured for Dyson Sphere Program. Original metallic/roughness texture sets kept as provided.

See `LICENSE-Model.txt` for the full CC-BY-4.0 legal text.

### Mod Libraries (not bundled, required dependencies)
- **BepInEx** – Copyright (c) BepInEx, LGPL-2.1
- **Harmony** – Copyright (c) Andreas Pardeike, MIT
- **LDBTool** – xiaoye97, MIT
- **CommonAPI** (incl. DSPModSave) – CommonAPI contributors, MIT
- **Fast Travel Enabler** – Original FastTravel Transpiler logic adapted from hetima’s `DSP_FastTravelEnabler` (MIT-style / public release)

---

Mod code released under the **MIT License** (see source repository).
3D model files under `Model/` remain under **CC-BY-4.0** (attribution to Sousinho).
