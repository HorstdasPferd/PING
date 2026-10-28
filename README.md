# PING – Portal Icarus Neural Gateway

A **Dyson Sphere Program** mod that adds a late-game building: the **PING portal**.
It charges from your power grid like an accumulator and, once charged, teleports your mecha to another PING portal on a different planet or star (up to 50 light-years away). It works in normal games; no sandbox tools are needed.

- **Mod GUID:** `ping.horstdaspferd`
- **Version:** 0.9.10
- **Dependencies:** BepInEx, CommonAPI, LDBTool, DSPModSave and any mod that disabled AbnormalyCheck as it will use a Sandbox-Fasttravel call. 

> Status: work in progress. Report problems together with your `BepInEx/LogOutput.log`.

## Features

- **Paired portals:** both ends need a PING portal. Energy is consumed at the sending portal only.
- **Accumulator-style charging:** portals charge from the power grid. Storage is 10 GJ, enough for the full 50 ly range.
- **Distance-based cost:** 192 MJ per light-year.
- **Destination window:** opens when you click a portal. It lists all planets and can be filtered and searched.
- **Custom model:** a three-step sci-fi podium with a warm orange LED edge.
- **Assembler only:** the portal cannot be hand-crafted. Stack size 5, crafting time 40 s.

## Installation

### With r2modman or the Thunderstore app

Install the mod. All dependencies are installed automatically.

### Manual

1. Install BepInEx 5 and the dependencies listed below.
2. Copy `PingMod.dll` **and** the file `pingmod` (the model AssetBundle, no file extension) together into `BepInEx/plugins/<any folder>/`.

If `pingmod` is missing, the mod still works but shows the vanilla accumulator model and writes `model bundle not found` to the log.

## Unlocking and building

- **Technology:** *Interstellar Power Transmission*
- **Recipe** (assembler, 40 s):

| Ingredient | Amount |
|---|---|
| Quantum Chip | 20 |
| Graviton Lens | 25 |
| Space Warper | 50 |
| Particle Broadband | 25 |
| Accumulator (empty) | 100 |

Place the portal inside the range of your Power-Grid and it starts charging.

## Usage

1. Build a PING portal on the **origin** planet and one on the **destination** planet. Planets without a portal are not offered as destinations.
2. Let the origin portal charge. The energy bar in the window shows the stored energy.
3. Stand on the platform or within 2 m of its edge and **click the portal** to open the destination window.
4. Pick a destination. You can either use Galactic Map View and select an Planet or use the filters or the search field if needed:
   - portal filter: only planets that have a PING portal
   - "<= N ly": only targets within range
   - "This star": only planets of the current star
5. Click **Teleport**. A click on a row only selects the target. If the target cannot be used, the status line says why (not enough energy, too far, cooldown, no portal there).

A trip costs **192 MJ per light-year**. A full 50 ly jump needs 9.6 GJ.

## Configuration

The file is `BepInEx/config/ping.horstdaspferd.cfg`. It is created on first start.

| Setting | Default | Meaning |
|---|---|---|
| `EnergyPerLightYearMJ` | 192 | Energy cost per light-year |
| `MaxTravelDistanceLY` | 50 | Maximum jump distance |
| `MaxEnergyStoredGJ` | 10 | Energy capacity of one portal |
| `ChargingPowerMW` | 150 | Power drawn from the grid while charging |
| `CooldownSeconds` | 10 | Wait time between two jumps |
| `WindowOpacity` | 0.78 | Background opacity of the destination window |
| `RemoveAllPortals` | false | Uninstall helper (see below) |

Recipe, model and usage range are fixed on purpose.

## Uninstalling safely

If you remove the mod while PING portals still exist in a save, the game may complain about unknown buildings.

1. Make a backup of your save.
2. Set `RemoveAllPortals = true` in the config and load your save.
3. The mod dismantles every PING portal and returns one portal item to your inventory for each. Planets are processed when they are loaded, so visit every planet that has a portal.
4. Save, quit, and then remove the mod.

## Troubleshooting

| Problem | What to check |
|---|---|
| Building looks like the vanilla accumulator | `pingmod` is not next to the DLL. Look for `model bundle not found` in the log. |
| Building does not charge | It must be inside a power field. Look at the `PING power` lines in the log. |
| No destination in the list | The destination planet needs a PING portal. Check the portal filter. |
| Window does not open | Click the portal itself while standing on or next to the platform. |

## Credits and license

- Mod code, 3D model, textures and icon: [HorstdasPferd](https://github.com/HorstdasPferd)
- The sandbox-check patch is based on `DSP_FastTravelEnabler` by hetima; the destination window was inspired by `DSP_PlanetFinder` by hetima.
- Thanks to the authors of BepInEx, Harmony, LDBTool, CommonAPI and DSPModSave.

See `CONTRIBUTORS.md` for the full list. The mod is released under the license in the `LICENSE` file.

## Source

https://github.com/HorstdasPferd/PING
