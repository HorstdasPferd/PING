# CONTRIBUTORS & ATTRIBUTION

Ordered by contribution type.

---

## Mod Code

- **PING - Portal Icarus Neural Gateway** – Mod implementation, Proto registration, Power/Accumulator logic, Harmony patches for Starmap integration, Save/Load via DSPModSave.
  - Author: `https://github.com/HorstdasPferd`

### Adapted from prior Open-Source DSP Mods

- **Fast Travel Enabler** (hetima): Original transpiler patches for `get_sandboxToolsEnabled`, which form the basis of the `PingMod.Patches.FastTravelPatch` Sandbox-Override architecture.
  - Source: `https://github.com/hetima/DSP_FastTravelEnabler`

- **DSP_PlanetFinder** (hetima): Inspired by this mod for the UI: search, filter and select different Planets in easy Way
  - Source: `https://github.com/hetima/DSP_PlanetFinder`

---

## 3D Model

### PING Portal Model base
- **Title**:     Sci-fi Portal
- **Artist**:    **Sousinho** (https://sketchfab.com/Sousinho)
- **Source**:    https://skfb.ly/oqQyv
- **License**:   **CC-BY-4.0** (Creative Commons Attribution 4.0 International)
- **Legal text**: `LICENSE-Model.txt`
- **Derivative / modifications made by the PING project**:
  - Resized / rescaled to DSP’s prefab grid.
  - Texture sets (BaseColor, Normal, Roughness, Metallic, Emissive for the rings) adapted for the Unity HDRP/Lit-shader pipeline used in DSP 0.10.35+.
  - Combined with an in-house mesh for ground contact (visible AO bake in Model/textures).

### Texture / AO bake in `Model/textures/`
- Texture maps listed are derivative works of the Sousinho-provided Sketchfab material set.

---

## Runtime Libraries (not bundled, required dependencies)

These are not part of the repository but must be installed alongside the mod.
They are **copyright of their respective authors** (see their README / package manifest for full attribution).

| Library | Version | Author(s) |
|---|---|---|
| BepInEx 5.x (BaseUnityPlugin, Chainloader) | 5.4.17 | BepInEx Contributors |
| Harmony 2.x (Transpiler / PatchProcessor) | 2.3+ | Andreas Pardeike |
| LDBTool (Proto edit hooks) | 3.0.3 | xiaoye97 |
| CommonAPI (ProtoRegistry, Localization) | 1.6.7 | CommonAPI Team |
| DSPModSave (IModCanSave) | 1.2.2 | CommonAPI Team |
| UnityEngine / Assembly-CSharp (DSP) | 0.10.35.x | Youthcat Studio (Gamersky) |

---

