using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CommonAPI;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using crecheng.DSPModSave;
using HarmonyLib;
using UnityEngine;
using xiaoye97;
using PingMod.Patches;

namespace PingMod
{
    // PING - Portal Icarus Neural Gateway
    //
    // How it works:
    //  * The portal is registered as a copy of the vanilla Accumulator, so the game itself charges it from the power grid.
    //  * The stored energy is the accumulator's curEnergy. A teleport simply removes energy from it.
    //  * While the player stands near a portal, the Galactic Map's "fast travel" button is available.
    //    Clicking it on a planet starts a PING teleport (cost, range, cooldown and partner portal are checked first).
    //  * On arrival the player is placed next to a PING portal of the destination planet.
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency(CommonAPIPlugin.GUID)]
    [BepInDependency(LDBToolPlugin.MODGUID)]
    [CommonAPISubmoduleDependency(nameof(ProtoRegistry), nameof(CustomDescSystem), nameof(UtilSystem), nameof(LocalizationModule))]
    public class PingPlugin : BaseUnityPlugin, IModCanSave
    {
        public const string MODID = "ping";
        public const string GUID = "ping.horstdaspferd";
        public const string NAME = "PING - Portal Icarus Neural Gateway";
        public const string VERSION = "0.9.10";

        public const int PING_ITEM_ID = 3500;
        public const int PING_MODEL_ID = 699;
        public const int PING_RECIPE_ID = 3500;

        // Tech that unlocks the PING recipe (1512 = Interstellar Power Transmission).
        public const int UNLOCK_TECH_ID = 1512;

        // The portal borrows the model, icon and accumulator behaviour of this vanilla building until a custom model exists.
        const int REFERENCE_ITEM_ID = 2206; // Accumulator
        const string REFERENCE_PREFAB_HINT = "accumulator";
        const string REFERENCE_PREFAB_EXCLUDE = "full";

        public static ManualLogSource Log;

        public static ItemProto PingItem;
        public static ModelProto PingModel;
        public static RecipeProto PingRecipe;

        public static ConfigEntry<float> EnergyPerLightYearMJ;
        public static ConfigEntry<float> MaxTravelDistanceLY;
        public static ConfigEntry<float> MaxEnergyStoredGJ;
        public static ConfigEntry<float> ChargingPowerMW;
        public static ConfigEntry<int> CooldownSeconds;
        public static ConfigEntry<float> WindowOpacity;
        public static ConfigEntry<bool> RemoveAllPortals;

        // Recipe: 20 Quantum Chip (1305), 25 Graviton Lens (1209), 50 Space Warper (1210), 25 Particle Broadband (1402), 100 Accumulator (2206).
        const string DEFAULT_INGREDIENTS = "1305x20,1209x25,1210x50,1402x25,2206x100";
        const int RECIPE_SECONDS = 40;

        // Null-safe config accessors (in case binding failed in Awake).
        // The portal can be used while standing on the platform or up to GATE_MARGIN meters from its edge.
        const float PODIUM_RADIUS = 3f;
        const float GATE_MARGIN = 2f;
        public static float GateRange => PODIUM_RADIUS + GATE_MARGIN;
        public static bool RequirePaired => true; // a portal is needed on the destination planet
        public static float WindowAlpha => WindowOpacity?.Value ?? 0.78f;
        public static long CooldownTicks => (CooldownSeconds?.Value ?? 10) * 60L;
        public static double MaxTravelDistanceLightYears => MaxTravelDistanceLY?.Value ?? 50f;
        public static long MaxEnergyJoules => (long)((MaxEnergyStoredGJ?.Value ?? 10f) * 1_000_000_000.0);
        public static double EnergyPerLightYearJoules => (EnergyPerLightYearMJ?.Value ?? 192f) * 1_000_000.0;
        // Accumulator input per game tick (60 ticks per second): MW -> J/tick.
        public static long ChargeJoulesPerTick => (long)Math.Round((ChargingPowerMW?.Value ?? 150f) * 1_000_000.0 / 60.0);

        void Awake()
        {
            Log = Logger;
            Log.LogInfo($"PING v{VERSION} starting...");

            Guard("config", BindConfig);
            Guard("proto registration", RegisterProtos);
            Guard("loading-finished hook", () => ProtoRegistry.onLoadingFinished += OnLoadingFinished);
            Guard("harmony patches", InstallPatches);

            Log.LogInfo($"PING v{VERSION} initialized.");
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        void BindConfig()
        {
            EnergyPerLightYearMJ = Config.Bind("Balancing", "EnergyPerLightYearMJ", 192f,
                new ConfigDescription("Energy cost per light year of travel distance (MJ). Every jump costs at least 192 MJ.",
                    new AcceptableValueRange<float>(0f, 10000f)));
            MaxTravelDistanceLY = Config.Bind("Balancing", "MaxTravelDistanceLY", 50f,
                new ConfigDescription("Maximum distance of a single jump (light years). Longer jumps are refused.",
                    new AcceptableValueRange<float>(1f, 500f)));
            MaxEnergyStoredGJ = Config.Bind("Balancing", "MaxEnergyStoredGJ", 10f,
                new ConfigDescription("Energy capacity of a PING portal (GJ). At 192 MJ/LY a full 50 LY jump costs 9.6 GJ.",
                    new AcceptableValueRange<float>(0.1f, 1000f)));
            ChargingPowerMW = Config.Bind("Balancing", "ChargingPowerMW", 150f,
                new ConfigDescription("Maximum charging power of a PING portal (MW). It charges from surplus power of the grid.",
                    new AcceptableValueRange<float>(0.1f, 10000f)));
            CooldownSeconds = Config.Bind("Balancing", "CooldownSeconds", 10,
                new ConfigDescription("Cooldown after a teleport before the same portal can be used again (seconds).",
                    new AcceptableValueRange<int>(0, 3600)));
            RemoveAllPortals = Config.Bind("Uninstall", "RemoveAllPortals", false,
                "Set to true, load your save once and save again: every PING Portal on each planet is dismantled and returned to your inventory (once per planet per session). Do this before removing the mod, then set it back to false.");
            WindowOpacity = Config.Bind("UI", "WindowOpacity", 0.78f,
                new ConfigDescription("Opacity of the destination window (0.2 = very transparent, 1 = solid).",
                    new AcceptableValueRange<float>(0.2f, 1f)));
        }

        static void RegisterProtos()
        {
            using (ProtoRegistry.StartModLoad(GUID))
            {
                Guard("localization", RegisterLocalization);
                Guard("item", RegisterItem);
                Guard("model", RegisterModel);
                Guard("recipe", RegisterRecipe);
                Guard("build bar", () => LDBTool.SetBuildBar(6, 1, PingItem.ID));
            }
        }

        static void RegisterLocalization()
        {
            LocalizationModule.RegisterTranslation("ping_gate",
                "PING Portal",
                "PING神经网关",
                "Portail PING");
            LocalizationModule.RegisterTranslation("ping_gateDesc",
                "PING - Portal Icarus Neural Gateway. Charges from surplus power like an accumulator. While standing on the platform or within 2 m of it, open the Galactic Map, select a planet and use the fast travel button to jump. The destination planet must also have a PING portal, you arrive next to it. Cost: 192 MJ / Light-Year.",
                "PING - Icarus神经传输网关。像蓄电器一样充电。站在平台上或距离平台2米内时打开星图，选择星球并点击快速旅行即可传送。目标星球也必须有网关，你会出现在它旁边。能耗：每光年192兆焦。",
                "PING - Portail Icarus Neural Gateway. Se charge avec le surplus d'énergie comme un accumulateur. Sur la plateforme ou à moins de 2 m, ouvrez la carte galactique, sélectionnez une planète et utilisez le voyage rapide. La planète de destination doit aussi posséder un portail PING, vous arrivez à côté. Coût : 192 MJ / année-lumière.");
            LocalizationModule.RegisterTranslation("ping_gateRecipe",
                "PING Portal",
                "PING神经网关",
                "Portail PING");
        }

        static void RegisterItem()
        {
            ItemProto reference = ResolveReferenceItem();
            PingItem = ProtoRegistry.RegisterItem(PING_ITEM_ID, "ping_gate", "ping_gateDesc",
                reference?.IconPath, ProtoRegistry.GetGridIndex(2, 9, 10));
            if (PingItem == null) return;

            PingItem.StackSize = 5;
            Log.LogInfo($"PING item registered (id={PING_ITEM_ID}).");
        }

        static void RegisterModel()
        {
            ItemProto reference = ResolveReferenceItem();
            if (reference == null)
            {
                Log.LogError($"PING: no vanilla accumulator found (item {REFERENCE_ITEM_ID} / prefab hint '{REFERENCE_PREFAB_HINT}'). Model registration skipped.");
                return;
            }

            ModelProto referenceModel = LDB.models.Select(reference.ModelIndex);
            if (referenceModel == null || string.IsNullOrEmpty(referenceModel.PrefabPath))
            {
                Log.LogError($"PING: reference model of item {reference.ID} is missing or has no prefab path.");
                return;
            }

            int[] descFields = reference.DescFields != null && reference.DescFields.Length > 0
                ? (int[])reference.DescFields.Clone()
                : new[] { 22, 11, 12, 1 };
            int[] upgrades = reference.Upgrades != null && reference.Upgrades.Length > 0
                ? (int[])reference.Upgrades.Clone()
                : new[] { reference.ID, 0 };

            string customPath = TryLoadCustomModel();
            string prefabPath = customPath ?? referenceModel.PrefabPath;
            if (customPath != null && PingItem != null)
            {
                // The icon is rendered into the same bundle (Assets/pingmod/icon.png).
                PingItem.IconPath = CUSTOM_ICON_PATH;
                Log.LogInfo($"PING: item icon path set to '{CUSTOM_ICON_PATH}'.");
            }

            PingModel = ProtoRegistry.RegisterModel(
                PING_MODEL_ID,
                PingItem,
                prefabPath,
                null,
                descFields,
                reference.BuildIndex,
                reference.BuildMode,
                upgrades);

            Log.LogInfo($"PING model registered (id={PING_MODEL_ID}, prefab='{prefabPath}', isNull={PingModel == null}).");
        }

        // The custom model lives in an AssetBundle named "pingmod" that is copied next to the plugin DLL.
        // CommonAPI hands out the prefab when the game asks for a path that contains the key word and
        // the bundle contains "<path>.prefab" (names inside a bundle are lower case).
        const string CUSTOM_KEYWORD = "pingmod";
        const string CUSTOM_BUNDLE = "pingmod";
        const string CUSTOM_PREFAB_PATH = "assets/pingmod/pingportal";
        const string CUSTOM_ICON_PATH = "assets/pingmod/icon";

        // Returns the custom prefab path, or null to fall back to the vanilla accumulator model.
        static string TryLoadCustomModel()
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(PingPlugin).Assembly.Location);
                string file = Path.Combine(dir, CUSTOM_BUNDLE);
                if (!File.Exists(file))
                {
                    Log.LogWarning($"PING: model bundle not found at '{file}'. Using the vanilla accumulator model.");
                    return null;
                }

                ResourceData resource = new ResourceData(GUID, CUSTOM_KEYWORD, dir);
                resource.LoadAssetBundle(CUSTOM_BUNDLE);
                if (!resource.HasAssetBundle())
                {
                    Log.LogWarning("PING: the model bundle could not be loaded. Using the vanilla accumulator model.");
                    return null;
                }

                ProtoRegistry.AddResource(resource);
                Log.LogInfo($"PING: custom model bundle loaded ('{file}'), prefab '{CUSTOM_PREFAB_PATH}'.");
                return CUSTOM_PREFAB_PATH;
            }
            catch (Exception ex)
            {
                SafeError("custom model bundle failed, using the vanilla accumulator model", ex);
                return null;
            }
        }

        // Finds the vanilla accumulator: by item id first, then by prefab path.
        static ItemProto ResolveReferenceItem()
        {
            ItemProto preferred = LDB.items.Select(REFERENCE_ITEM_ID);
            if (IsAccumulatorReference(preferred)) return preferred;

            ItemProto[] items = LDB.items.dataArray;
            if (items == null) return null;
            foreach (ItemProto item in items)
            {
                if (IsAccumulatorReference(item)) return item;
            }
            return null;
        }

        static bool IsAccumulatorReference(ItemProto item)
        {
            if (item == null || item.ID <= 0) return false;

            ModelProto model = LDB.models.Select(item.ModelIndex);
            string path = model?.PrefabPath;
            return !string.IsNullOrEmpty(path)
                && path.IndexOf(REFERENCE_PREFAB_HINT, StringComparison.OrdinalIgnoreCase) >= 0
                && path.IndexOf(REFERENCE_PREFAB_EXCLUDE, StringComparison.OrdinalIgnoreCase) < 0;
        }

        static void RegisterRecipe()
        {
            if (PingItem == null) return;

            // Ingredients come from the config ("itemIdxcount,..."). Verified ids: 1305 Quantum Chip,
            // 1209 Graviton Lens, 1210 Space Warper, 1402 Particle Broadband, 2206 Accumulator.
            List<int> parsedIds = new List<int>();
            List<int> parsedCounts = new List<int>();
            ParseIngredients(DEFAULT_INGREDIENTS, parsedIds, parsedCounts);
            if (parsedIds.Count == 0)
            {
                Log.LogWarning("PING: the configured ingredients are empty or invalid, using the defaults.");
                ParseIngredients(DEFAULT_INGREDIENTS, parsedIds, parsedCounts);
            }
            if (parsedIds.Count > 6)
            {
                Log.LogWarning("PING: more than 6 ingredients configured, only the first 6 are used.");
                parsedIds.RemoveRange(6, parsedIds.Count - 6);
                parsedCounts.RemoveRange(6, parsedCounts.Count - 6);
            }
            int[] wantedInputs = parsedIds.ToArray();
            int[] wantedCounts = parsedCounts.ToArray();

            // Items that do not exist would show as "?" in the recipe, so they are dropped (and logged).
            List<int> inputs = new List<int>();
            List<int> inputCounts = new List<int>();
            for (int i = 0; i < wantedInputs.Length; i++)
            {
                if (LDB.items.Select(wantedInputs[i]) == null)
                {
                    Log.LogWarning($"PING: recipe ingredient {wantedInputs[i]} does not exist in this game version and was left out.");
                    continue;
                }
                inputs.Add(wantedInputs[i]);
                inputCounts.Add(wantedCounts[i]);
            }

            PingRecipe = ProtoRegistry.RegisterRecipe(PING_RECIPE_ID, ERecipeType.Assemble,
                RECIPE_SECONDS * 60, inputs.ToArray(), inputCounts.ToArray(), new[] { PingItem.ID }, new[] { 1 },
                "ping_gateDesc", 0);
            if (PingRecipe == null) return;

            TrySetField(PingRecipe, "ping_gateRecipe", "name");
            TrySetField(PingRecipe, "ping_gateDesc", "desc");
            TrySetField(PingRecipe, 1, "proliferateEffect", "ProliferateEffect");

            // The recipe shows the same icon as the item.
            if (PingItem != null) PingRecipe.IconPath = PingItem.IconPath;

            Log.LogInfo($"PING recipe registered (id={PING_RECIPE_ID}, {inputs.Count} ingredients).");
        }

        static void ParseIngredients(string text, List<int> ids, List<int> counts)
        {
            foreach (string part in (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = part.Trim().Split(new[] { 'x', 'X', ':', '*' }, StringSplitOptions.RemoveEmptyEntries);
                int id, count;
                if (kv.Length != 2 || !int.TryParse(kv[0].Trim(), out id) || !int.TryParse(kv[1].Trim(), out count) || id <= 0 || count <= 0)
                {
                    Log.LogWarning($"PING: could not read ingredient '{part.Trim()}' (expected itemIdxcount, e.g. 1305x20).");
                    continue;
                }
                ids.Add(id);
                counts.Add(count);
            }
        }

        // The game only lists an item or recipe in its grids (assembler recipe picker, replicator) when
        // GridIndex is a free cell of a 14 x 8 page: format X YY ZZ = page, row (1-8), column (1-14).
        // Without a valid GridIndex the recipe is registered but never shown.
        static void AssignGridIndex()
        {
            if (PingItem == null) return;

            HashSet<int> used = new HashSet<int>();
            foreach (ItemProto item in LDB.items.dataArray)
                if (item != null && item != PingItem && item.GridIndex > 0) used.Add(item.GridIndex);
            foreach (RecipeProto recipe in LDB.recipes.dataArray)
                if (recipe != null && recipe != PingRecipe && recipe.GridIndex > 0) used.Add(recipe.GridIndex);

            // Same page as the accumulator, starting right after its cell.
            ItemProto reference = ResolveReferenceItem();
            int refGrid = reference != null && reference.GridIndex >= 1101 ? reference.GridIndex : 2101;
            int page = refGrid / 1000;
            int startRow = (refGrid - page * 1000) / 100;
            int startCol = refGrid % 100;

            int chosen = 0;
            for (int pass = 0; pass < 2 && chosen == 0; pass++)
            {
                for (int row = pass == 0 ? startRow : 1; row <= 8 && chosen == 0; row++)
                {
                    for (int col = (pass == 0 && row == startRow) ? startCol + 1 : 1; col <= 14; col++)
                    {
                        int index = page * 1000 + row * 100 + col;
                        if (used.Contains(index)) continue;
                        chosen = index;
                        break;
                    }
                }
            }

            if (chosen == 0)
            {
                Log.LogWarning($"PING: no free grid cell found on page {page}. The recipe may not be visible in the assembler.");
                return;
            }

            PingItem.GridIndex = chosen;
            if (PingRecipe != null) PingRecipe.GridIndex = chosen;
            Log.LogInfo($"PING grid cell: {chosen} (page {page}, row {chosen % 1000 / 100}, column {chosen % 100}).");
        }

        void InstallPatches()
        {
            Harmony harmony = new Harmony(GUID);
            Guard("fast-travel button patches", () => FastTravelPatch.Install(harmony));
            Guard("starmap teleport patch", () => PingStarmapPatch.Install(harmony));
            Guard("target window patches", () => PingUiPatch.Install(harmony));
            Guard("factory tick patch", () => PingTicker.Install(harmony));
        }

        // ------------------------------------------------------------------
        // After all protos are loaded
        // ------------------------------------------------------------------

        static void OnLoadingFinished()
        {
            Guard("grid cell", AssignGridIndex);
            Guard("attach recipe to tech", AttachRecipeToTech);
            Guard("build bar", () =>
            {
                if (PingItem != null) LDBTool.SetBuildBar(6, 1, PingItem.ID);
            });
            Guard("diagnostics", LogDiagnostics);
            Guard("reference size", LogReferenceSize);
            Guard("game materials and power", ApplyGameMaterialsAndPower);
        }

        // The game draws buildings with its own instancing shaders, so the Unity "Standard" materials of the bundle
        // would draw nothing visible. For the portal, copies of the vanilla accumulator's materials are used instead
        // (with the texture of the bundle's material, if it has one). The wiring values are copied from the vanilla accumulator too.
        static void ApplyGameMaterialsAndPower()
        {
            if (PingItem == null) return;
            ItemProto reference = ResolveReferenceItem();
            PrefabDesc vanilla = reference?.prefabDesc;
            PrefabDesc ours = PingItem.prefabDesc;
            if (vanilla == null || ours == null)
            {
                Log.LogWarning("PING: prefab descriptions missing, cannot apply materials and wiring.");
                return;
            }

            LogPower("vanilla accumulator", vanilla);
            LogPower("PING portal (before)", ours);

            // Wiring: the vanilla accumulator is a "power node" (it connects to the grid by wire, like a pole).
            // Without that flag the portal would never join a power network.
            ours.isPowerNode = vanilla.isPowerNode;
            ours.powerConnectDistance = vanilla.powerConnectDistance;
            ours.powerCoverRadius = vanilla.powerCoverRadius;
            LogPower("PING portal (after)", ours);

            if (PingModel == null || PingModel.PrefabPath == null || PingModel.PrefabPath.IndexOf(CUSTOM_KEYWORD, StringComparison.Ordinal) < 0)
                return; // vanilla model in use, its own materials are fine

            Material[][] vm = vanilla.lodMaterials;
            Material[][] om = ours.lodMaterials;
            if (vm == null || vm.Length == 0 || vm[0] == null || vm[0].Length == 0 || om == null || om.Length == 0 || om[0] == null)
            {
                Log.LogWarning("PING: no materials available to copy.");
                return;
            }

            LogMaterial("vanilla accumulator material", vm[0][0]);

            Material[] source = om[0];
            Material[] result = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Material template = vm[0][Math.Min(i, vm[0].Length - 1)];
                Material copy = new Material(template);
                copy.name = "PING_" + i;

                CopyTextures(source[i], copy, i);

                // Draw both sides of every surface (the model has open shells, e.g. the dark disc inside the ring).
                if (copy.HasProperty("_CullMode")) copy.SetFloat("_CullMode", 0f);

                result[i] = copy;
            }
            om[0] = result;
            Log.LogInfo($"PING: {result.Length} game materials applied to the portal model.");
        }

        // Hands the textures of the bundle's (Unity Standard) material to the game's material. The game's property names are
        // looked up from its shader: albedo = "_MainTex", normal = a name with "normal"/"bump", emission = "emi", metal/smoothness = "ms"/"metal".
        // Textures the bundle material does not have are left as in the vanilla material.
        static void CopyTextures(Material from, Material to, int index)
        {
            if (from == null || to == null || to.shader == null) return;

            Shader shader = to.shader;
            int count = shader.GetPropertyCount();
            for (int p = 0; p < count; p++)
            {
                if (shader.GetPropertyType(p) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;

                string name = shader.GetPropertyName(p);
                string lower = name.ToLowerInvariant();
                if (lower.Contains("jitter")) continue; // noise texture of the glow flicker, keep the game's
                string sourceName = null;
                if (name == "_MainTex") sourceName = "_MainTex";
                else if (lower.Contains("normal") || lower.Contains("bump")) sourceName = "_BumpMap";
                else if (lower.Contains("emi")) sourceName = "_EmissionMap";
                else if (lower.Contains("ms") || lower.Contains("metal")) sourceName = "_MetallicGlossMap";
                if (sourceName == null || !from.HasProperty(sourceName)) continue;

                Texture tex = from.GetTexture(sourceName);
                if (tex == null)
                {
                    // Do not let the vanilla accumulator's glow or relief show up on the portal.
                    if (sourceName == "_EmissionMap") to.SetTexture(name, Texture2D.blackTexture);
                    else if (sourceName == "_BumpMap") to.SetTexture(name, Texture2D.normalTexture);
                    Log.LogInfo($"PING: material {index}: no '{sourceName}' in the bundle, '{name}' set to {(sourceName == "_MetallicGlossMap" ? "the vanilla texture" : "a neutral texture")}.");
                    continue;
                }
                to.SetTexture(name, tex);
                Log.LogInfo($"PING: material {index}: '{name}' <- bundle texture '{tex.name}' ({sourceName}).");
            }
        }

        static void LogPower(string label, PrefabDesc d)
        {
            Log.LogInfo($"PING power [{label}]: isAccumulator={d.isAccumulator} isPowerNode={d.isPowerNode} isPowerConsumer={d.isPowerConsumer} " +
                        $"powerPoint={d.powerPoint} connectDistance={d.powerConnectDistance} coverRadius={d.powerCoverRadius} " +
                        $"input={d.inputEnergyPerTick} output={d.outputEnergyPerTick} max={d.maxAcuEnergy} hasBuildCollider={d.hasBuildCollider}");
        }

        static void LogMaterial(string label, Material m)
        {
            if (m == null || m.shader == null) return;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Shader shader = m.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string pn = shader.GetPropertyName(i);
                UnityEngine.Rendering.ShaderPropertyType pt = shader.GetPropertyType(i);
                sb.Append(pn).Append(':').Append(pt);
                if (pt == UnityEngine.Rendering.ShaderPropertyType.Color) sb.Append('=').Append(ColorUtility.ToHtmlStringRGBA(m.GetColor(pn)));
                else if (pt == UnityEngine.Rendering.ShaderPropertyType.Float || pt == UnityEngine.Rendering.ShaderPropertyType.Range) sb.Append('=').Append(m.GetFloat(pn));
                sb.Append("  ");
            }
            Log.LogInfo($"PING material [{label}] shader='{shader.name}' properties: {sb}");
        }

        // Logs the size data of the vanilla accumulator, so a custom model can be scaled to match.
        static void LogReferenceSize()
        {
            PrefabDesc d = LDB.items.Select(2206)?.prefabDesc;
            if (d == null)
            {
                Log.LogWarning("PING: reference accumulator has no prefabDesc.");
                return;
            }
            string lods = d.lodDistances == null ? "-" : string.Join(", ", d.lodDistances);
            Log.LogInfo($"PING ref accumulator: buildCollider.ext={d.buildCollider.ext} buildCollider.pos={d.buildCollider.pos} " +
                        $"selectCenter={d.selectCenter} selectSize={d.selectSize} cullingHeight={d.cullingHeight} " +
                        $"roughHeight={d.roughHeight} lodCount={d.lodCount} lodDistances=[{lods}]");
        }

        static void AttachRecipeToTech()
        {
            if (PingRecipe == null) return;

            TechProto tech = LDB.techs.Select(UNLOCK_TECH_ID);
            if (tech == null || tech.ID == 0)
            {
                Log.LogWarning($"PING: tech {UNLOCK_TECH_ID} not found. Unlocking the recipe directly instead.");
                TrySetField(PingRecipe, false, "Locked", "locked");
                return;
            }

            FieldInfo unlockField = FindField(typeof(TechProto), "UnlockRecipes", "unlockRecipeIds", "UnlockRecipeIds");
            if (unlockField == null)
            {
                Log.LogError("PING: TechProto has no recipe-unlock field (tried UnlockRecipes/unlockRecipeIds/UnlockRecipeIds). Recipe NOT attached!");
                return;
            }

            List<int> unlocks = new List<int>((unlockField.GetValue(tech) as int[]) ?? new int[0]);
            if (!unlocks.Contains(PingRecipe.ID))
            {
                unlocks.Add(PingRecipe.ID);
                unlockField.SetValue(tech, unlocks.ToArray());
            }
            Log.LogInfo($"PING recipe attached to tech: {tech.name} (id={tech.ID})");
        }

        // Shows whether the portal really is an accumulator and which model it uses.
        static void LogDiagnostics()
        {
            if (PingItem == null || PingModel == null)
            {
                Log.LogWarning("PING: item or model was not registered.");
                return;
            }

            PrefabDesc desc = PingItem.prefabDesc;
            Log.LogInfo($"PING model prefab: '{PingModel.PrefabPath}', isAccumulator={desc != null && desc.isAccumulator}.");
            if (desc == null || !desc.isAccumulator)
                Log.LogWarning("PING: the portal prefab is not an accumulator. It will not charge from the grid!");
        }

        // ------------------------------------------------------------------
        // Savegame (DSPModSave)
        // ------------------------------------------------------------------

        // Version 3 layout per portal: planetId, entityId, x, y, z, cooldownUntilTick.
        // The stored energy lives in the game's own accumulator data, not here.
        const byte SAVE_VERSION = 3;

        public void Export(BinaryWriter w)
        {
            w.Write(SAVE_VERSION);
            w.Write(PingTicker.AllPortals.Count);
            foreach (PortalData portal in PingTicker.AllPortals.Values)
            {
                w.Write(portal.planetId);
                w.Write(portal.entityId);
                w.Write(portal.localPos.x);
                w.Write(portal.localPos.y);
                w.Write(portal.localPos.z);
                w.Write(portal.cooldownUntilTick);
            }
        }

        public void Import(BinaryReader r)
        {
            PingTicker.Reset();

            byte version = r.ReadByte();
            if (version > SAVE_VERSION)
            {
                Log.LogWarning($"PING: save data version {version} is newer than supported ({SAVE_VERSION}), ignoring it.");
                return;
            }

            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                PortalData portal = new PortalData();
                if (version >= 3)
                {
                    portal.planetId = r.ReadInt32();
                    portal.entityId = r.ReadInt32();
                    portal.localPos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    portal.posKnown = true;
                    portal.cooldownUntilTick = r.ReadInt64();
                }
                else if (version == 2)
                {
                    // planetId, entityId, energy, target, cooldown: energy and target no longer exist.
                    portal.planetId = r.ReadInt32();
                    portal.entityId = r.ReadInt32();
                    r.ReadDouble();
                    r.ReadInt32();
                    portal.cooldownUntilTick = r.ReadInt64();
                }
                else
                {
                    // Version 1: entityId, energy, target, cooldown, planetId.
                    portal.entityId = r.ReadInt32();
                    r.ReadDouble();
                    r.ReadInt32();
                    portal.cooldownUntilTick = r.ReadInt64();
                    portal.planetId = r.ReadInt32();
                }
                PingTicker.AllPortals[PingTicker.Key(portal.planetId, portal.entityId)] = portal;
            }
        }

        public void IntoOtherSave()
        {
            PingTicker.Reset();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        internal static void Guard(string phase, Action action)
        {
            try { action(); }
            catch (Exception ex) { SafeError("PING - " + phase + " failed", ex); }
        }

        internal static void SafeError(string msg, Exception ex)
        {
            try
            {
                if (Log != null)
                {
                    Log.LogError(msg + ": " + ex.Message);
                    Log.LogWarning(ex.StackTrace);
                }
                else
                {
                    Debug.LogError("[PING] " + msg + ": " + ex);
                }
            }
            catch { }
        }

        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static FieldInfo FindField(Type type, params string[] names)
        {
            foreach (string name in names)
            {
                FieldInfo field = type.GetField(name, AnyInstance);
                if (field != null) return field;
            }
            return null;
        }

        // Diagnostics only: writes down which icon path and sprite the game has for an item or recipe.
        internal static void LogIcon(string label, object proto, string path)
        {
            if (proto == null) return;
            // "iconSprite" is a property in this game version (a field in older ones).
            PropertyInfo property = proto.GetType().GetProperty("iconSprite", AnyInstance);
            FieldInfo field = property == null ? FindField(proto.GetType(), "iconSprite") : null;
            Sprite sprite = null;
            try
            {
                if (property != null) sprite = property.GetValue(proto, null) as Sprite;
                else if (field != null) sprite = field.GetValue(proto) as Sprite;
            }
            catch (Exception ex) { Log.LogWarning("PING icon: reading the sprite failed: " + ex.Message); }
            string info = "-";
            if (sprite != null && sprite.texture != null)
                info = sprite.texture.width + "x" + sprite.texture.height + ", format " + sprite.texture.format + ", mips " + sprite.texture.mipmapCount + ", rect " + sprite.rect;
            Log.LogInfo($"PING icon [{label}]: path='{path}', member found={(property != null || field != null)}, sprite={(sprite != null ? sprite.name : "null")}, texture={info}.");
        }

        static void TrySetField(object target, object value, params string[] names)
        {
            FieldInfo field = FindField(target.GetType(), names);
            if (field != null) field.SetValue(target, value);
        }
    }

    public class PortalData
    {
        public int planetId;
        public int entityId;
        // Position on the planet (local planet coordinates). Used to place the player next to it on arrival.
        public Vector3 localPos;
        public bool posKnown;
        public long cooldownUntilTick;
    }

    internal static class PatchHelper
    {
        public static HarmonyMethod Method(Type type, string name)
        {
            return new HarmonyMethod(AccessTools.Method(type, name));
        }

        // Patches one method; logs and returns false instead of throwing when it cannot be patched.
        public static bool TryPatch(Harmony harmony, Type type, string method, Type[] args = null,
            HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod transpiler = null)
        {
            string label = type.Name + "." + method;
            try
            {
                MethodInfo original = AccessTools.Method(type, method, args);
                if (original == null)
                {
                    PingPlugin.Log.LogWarning($"PING: patch skipped, method not found: {label}");
                    return false;
                }
                harmony.Patch(original, prefix, postfix, transpiler);
                return true;
            }
            catch (Exception ex)
            {
                PingPlugin.SafeError($"PING: patch FAILED for {label}", ex);
                return false;
            }
        }
    }

    public static class PingTicker
    {
        // 1 light year in universe units (meters, see uPosition).
        const double UnitsPerLightYear = 2_400_000.0;
        // Minimum cost of any teleport (same-star hops).
        const double MinimumCostJoules = 192_000_000.0;
        // How often each factory is re-scanned for PING portals.
        const long SyncIntervalTicks = 60;

        // Entity ids are only unique per planet, so portals are keyed by (planetId, entityId).
        public static readonly Dictionary<long, PortalData> AllPortals = new Dictionary<long, PortalData>();
        static readonly Dictionary<int, long> LastSyncTick = new Dictionary<int, long>();

        // Set when a PING teleport starts, consumed when the player arrives.
        static int pendingPlanetId;
        static bool pendingHasPos;
        static Vector3 pendingPos;

        static bool recipeChecked;
        static readonly HashSet<int> decommissioned = new HashSet<int>();

        public static long Key(int planetId, int entityId)
        {
            return ((long)planetId << 32) | (uint)entityId;
        }

        public static void Reset()
        {
            AllPortals.Clear();
            LastSyncTick.Clear();
            decommissioned.Clear();
            batchLogged = false;
            pendingPlanetId = 0;
            recipeChecked = false;
        }

        public static void Install(Harmony harmony)
        {
            PatchHelper.TryPatch(harmony, typeof(FactorySystem), "GameTick", new[] { typeof(long), typeof(bool) },
                postfix: PatchHelper.Method(typeof(PingTicker), nameof(FactoryTickPostfix)));
        }

        public static double DistanceLy(PlanetData a, PlanetData b)
        {
            return (a.uPosition - b.uPosition).magnitude / UnitsPerLightYear;
        }

        // Energy needed for a jump of 'distLy' light years.
        public static long CostJoules(double distLy)
        {
            return (long)Math.Ceiling(Math.Max(MinimumCostJoules, distLy * PingPlugin.EnergyPerLightYearJoules));
        }

        public static PortalData GetPortal(int planetId, int entityId)
        {
            PortalData portal;
            return AllPortals.TryGetValue(Key(planetId, entityId), out portal) ? portal : null;
        }

        // Ids of all planets that have at least one PING portal.
        public static HashSet<int> PlanetsWithPortals()
        {
            HashSet<int> result = new HashSet<int>();
            foreach (PortalData portal in AllPortals.Values) result.Add(portal.planetId);
            return result;
        }

        // The portal behind an entity of the local factory (e.g. the one the player just clicked), or null.
        public static PortalData FindPortalForEntity(int entityId)
        {
            Player player = GameMain.mainPlayer;
            PlanetFactory factory = player?.factory;
            if (factory == null || factory.entityPool == null) return null;
            if (entityId <= 0 || entityId >= factory.entityPool.Length) return null;
            if (factory.entityPool[entityId].id != entityId || factory.entityPool[entityId].protoId != PingPlugin.PING_ITEM_ID) return null;

            PortalData portal = GetPortal(factory.planetId, entityId);
            if (portal == null)
            {
                SyncPortals(factory);
                portal = GetPortal(factory.planetId, entityId);
            }
            return portal;
        }

        // Energy currently stored in the portal's accumulator.
        public static bool TryGetStored(PortalData portal, out long stored)
        {
            stored = 0;
            PowerSystem power;
            int accId;
            if (!TryGetAccumulator(portal, out power, out accId)) return false;
            stored = power.accPool[accId].curEnergy;
            return true;
        }

        public static double CooldownSecondsLeft(PortalData portal)
        {
            long left = portal.cooldownUntilTick - GameMain.gameTick;
            return left > 0 ? left / 60.0 : 0.0;
        }

        // ------------------------------------------------------------------
        // Portal registry
        // ------------------------------------------------------------------

        // Picks the portal on a planet that the player arrives next to (lowest entity id, position known).
        static PortalData FindPartner(int planetId)
        {
            PortalData best = null;
            foreach (PortalData portal in AllPortals.Values)
            {
                if (portal.planetId != planetId) continue;
                if (best == null || (portal.posKnown && !best.posKnown) ||
                    (portal.posKnown == best.posKnown && portal.entityId < best.entityId))
                    best = portal;
            }
            return best;
        }

        // Closest portal on the local planet within gate range, or null.
        public static PortalData FindNearestPortalInRange()
        {
            Player player = GameMain.mainPlayer;
            PlanetData planet = GameMain.localPlanet;
            if (player == null || planet == null || planet.factory == null) return null;

            EntityData[] pool = planet.factory.entityPool;
            if (pool == null) return null;

            PortalData best = null;
            float bestDist = PingPlugin.GateRange;
            foreach (PortalData portal in AllPortals.Values)
            {
                if (portal.planetId != planet.id) continue;
                int id = portal.entityId;
                if (id <= 0 || id >= pool.Length || pool[id].id != id) continue;

                float dist = Vector3.Distance(player.position, pool[id].pos);
                if (dist <= bestDist)
                {
                    bestDist = dist;
                    best = portal;
                }
            }
            return best;
        }

        // Registers new PING entities of this factory, drops portals whose entity no longer exists,
        // and applies the configured accumulator values.
        internal static void SyncPortals(PlanetFactory factory)
        {
            EntityData[] pool = factory.entityPool;
            PowerSystem power = factory.powerSystem;
            int planetId = factory.planetId;
            HashSet<long> found = new HashSet<long>();

            // Uninstall helper: dismantle all portals of this planet once per session.
            if (PingPlugin.RemoveAllPortals != null && PingPlugin.RemoveAllPortals.Value && decommissioned.Add(planetId))
                DecommissionPortals(factory);

            for (int i = 1; i < factory.entityCursor; i++)
            {
                if (pool[i].id != i || pool[i].protoId != PingPlugin.PING_ITEM_ID) continue;

                long key = Key(planetId, i);
                found.Add(key);

                PortalData portal;
                if (!AllPortals.TryGetValue(key, out portal))
                {
                    portal = new PortalData { planetId = planetId, entityId = i };
                    AllPortals[key] = portal;
                    PingPlugin.Log.LogInfo($"PING portal registered: planet {planetId}, entityId {i}.");
                    LogRenderBatch();
                }
                portal.localPos = pool[i].pos;
                portal.posKnown = true;

                ConfigureAccumulator(power, pool[i].powerAccId);
            }

            List<long> stale = new List<long>();
            foreach (KeyValuePair<long, PortalData> kv in AllPortals)
            {
                if (kv.Value.planetId == planetId && !found.Contains(kv.Key)) stale.Add(kv.Key);
            }
            foreach (long key in stale) AllPortals.Remove(key);
        }

        static bool batchLogged;

        // Diagnostic: what the game's instancing renderer holds for the portal model (used for the hover outline).
        static void LogRenderBatch()
        {
            if (batchLogged) return;
            batchLogged = true;
            try
            {
                PrefabDesc desc = PingPlugin.PingItem?.prefabDesc;
                ObjectRenderer renderer = desc == null || GameMain.gpuiManager == null ? null : GameMain.gpuiManager.GetObjectRenderer(desc.modelIndex);
                if (renderer == null || renderer.lodBatches == null || renderer.lodBatches.Length == 0 || renderer.lodBatches[0] == null)
                {
                    PingPlugin.Log.LogInfo("PING render batch: not available yet.");
                    return;
                }
                PingPlugin.LogIcon("item", PingPlugin.PingItem, PingPlugin.PingItem.IconPath);
                if (PingPlugin.PingRecipe != null) PingPlugin.LogIcon("recipe", PingPlugin.PingRecipe, PingPlugin.PingRecipe.IconPath);
                if (desc.lodMeshes != null && desc.lodMeshes.Length > 0 && desc.lodMeshes[0] != null)
                {
                    Mesh lodMesh = desc.lodMeshes[0];
                    PingPlugin.Log.LogInfo($"PING lod mesh: vertices={lodMesh.vertexCount}, bounds center={lodMesh.bounds.center}, size={lodMesh.bounds.size}, cullingHeight={desc.cullingHeight}.");
                }
                BatchRenderer batch = renderer.lodBatches[0];
                int subMeshes = batch.mesh != null ? batch.mesh.subMeshCount : -1;
                int materials = batch.materials != null ? batch.materials.Length : -1;
                PingPlugin.Log.LogInfo($"PING render batch: lodBatches={renderer.lodBatches.Length}, subMeshes={subMeshes}, materials={materials}, " +
                                       $"castShadow={desc.castShadow}, recvShadow={desc.recvShadow}, lodMeshSubMeshes={(desc.lodMeshes != null && desc.lodMeshes.Length > 0 && desc.lodMeshes[0] != null ? desc.lodMeshes[0].subMeshCount : -1)}.");
            }
            catch (Exception ex) { PingPlugin.SafeError("LogRenderBatch failed", ex); }
        }

        // Dismantles every PING portal of the factory like the player would, and gives the portal items back.
        static void DecommissionPortals(PlanetFactory factory)
        {
            Player player = GameMain.mainPlayer;
            if (player == null) return;

            List<int> ids = new List<int>();
            EntityData[] pool = factory.entityPool;
            for (int i = 1; i < factory.entityCursor; i++)
            {
                if (pool[i].id == i && pool[i].protoId == PingPlugin.PING_ITEM_ID) ids.Add(i);
            }

            foreach (int id in ids)
            {
                try
                {
                    int protoId = PingPlugin.PING_ITEM_ID;
                    factory.DismantleFinally(player, id, ref protoId);
                    player.TryAddItemToPackage(PingPlugin.PING_ITEM_ID, 1, 0, true, 0, false);
                    PingPlugin.Log.LogInfo($"PING: portal dismantled (planet {factory.planetId}, entity {id}) and returned to the inventory.");
                }
                catch (Exception ex) { PingPlugin.SafeError("decommissioning a portal failed", ex); }
            }
        }

        // The portal is an accumulator that only charges: configured capacity and charge rate, no output into the grid.
        static void ConfigureAccumulator(PowerSystem power, int accId)
        {
            if (power == null || power.accPool == null || accId <= 0 || accId >= power.accPool.Length) return;
            if (power.accPool[accId].id != accId) return;

            long max = PingPlugin.MaxEnergyJoules;
            power.accPool[accId].maxEnergy = max;
            power.accPool[accId].inputEnergyPerTick = PingPlugin.ChargeJoulesPerTick;
            power.accPool[accId].outputEnergyPerTick = 0;
            if (power.accPool[accId].curEnergy > max) power.accPool[accId].curEnergy = max;
        }

        static bool TryGetAccumulator(PortalData portal, out PowerSystem power, out int accId)
        {
            power = null;
            accId = 0;

            PlanetData planet = GameMain.data?.galaxy?.PlanetById(portal.planetId);
            PlanetFactory factory = planet?.factory;
            if (factory == null || factory.entityPool == null) return false;
            if (portal.entityId <= 0 || portal.entityId >= factory.entityPool.Length) return false;
            if (factory.entityPool[portal.entityId].id != portal.entityId) return false;

            power = factory.powerSystem;
            accId = factory.entityPool[portal.entityId].powerAccId;
            return power != null && power.accPool != null && accId > 0 && accId < power.accPool.Length
                && power.accPool[accId].id == accId;
        }

        // ------------------------------------------------------------------
        // Teleport
        // ------------------------------------------------------------------

        // Starts a teleport from 'origin' to 'target'. Returns an error message for the player, or null on success.
        public static string TryStartTeleport(PortalData origin, PlanetData target)
        {
            Player player = GameMain.mainPlayer;
            if (player == null) return "PING: no player.";
            if (player.warping || player.fastTravelling) return "PING: not available while travelling.";

            PlanetData current = GameMain.data?.galaxy?.PlanetById(origin.planetId);
            if (current == null) return "PING: portal is not ready.";
            if (target.id == current.id) return "PING: this is the planet you are on.";

            double distLy = DistanceLy(current, target);
            double maxLy = PingPlugin.MaxTravelDistanceLightYears;
            if (distLy > maxLy) return $"PING: destination is {distLy:F1} LY away (maximum {maxLy:F0} LY).";

            PortalData partner = FindPartner(target.id);
            if (PingPlugin.RequirePaired && partner == null) return "PING: the destination planet has no PING portal.";

            if (GameMain.gameTick < origin.cooldownUntilTick) return "PING: portal is cooling down.";

            PowerSystem power;
            int accId;
            if (!TryGetAccumulator(origin, out power, out accId)) return "PING: portal is not ready.";

            long required = CostJoules(distLy);
            long stored = power.accPool[accId].curEnergy;
            if (stored < required) return $"PING: not enough energy ({stored / 1_000_000} / {required / 1_000_000} MJ).";

            power.accPool[accId].curEnergy -= required;
            origin.cooldownUntilTick = GameMain.gameTick + PingPlugin.CooldownTicks;

            pendingPlanetId = target.id;
            pendingHasPos = partner != null && partner.posKnown;
            pendingPos = partner != null ? partner.localPos : Vector3.zero;

            try
            {
                player.controller.actionSail.StartFastTravelToPlanet(target);
            }
            catch (Exception ex)
            {
                power.accPool[accId].curEnergy += required;
                origin.cooldownUntilTick = 0;
                pendingPlanetId = 0;
                PingPlugin.SafeError("PING teleport failed", ex);
                return "PING: teleport failed, see log.";
            }

            PingPlugin.Log.LogInfo(
                $"PING teleport to {target.name}: distance={distLy:F2} LY, cost={required / 1e6:F0} MJ, left={power.accPool[accId].curEnergy / 1e6:F0} MJ");
            return null;
        }

        // Called after the game finished a fast travel to 'planet'. Moves the player next to the partner portal.
        public static void OnArrived(PlanetData planet)
        {
            if (planet == null || pendingPlanetId != planet.id) return;
            pendingPlanetId = 0;
            if (!pendingHasPos) return;

            Player player = GameMain.mainPlayer;
            if (player == null) return;

            // Slightly above the ground and a few meters beside the portal.
            Vector3 up = pendingPos.normalized;
            Vector3 side = Vector3.Cross(up, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(up, Vector3.right);
            Vector3 lpos = pendingPos + up * 1.5f + side.normalized * 3f;
            Quaternion lrot = Maths.SphericalRotation(lpos, 0f);

            player.transform.localPosition = lpos;
            player.transform.localRotation = lrot;
            player.uPosition = planet.uPosition + (VectorLF3)(planet.runtimeRotation * lpos);
            player.uRotation = planet.runtimeRotation * lrot;
            player.uVelocity = VectorLF3.zero;
        }

        // ------------------------------------------------------------------
        // Per-tick work
        // ------------------------------------------------------------------

        // Old savegames: if the unlocking tech is already researched, the recipe was never unlocked. Fix it once per load.
        static void EnsureRecipeUnlocked()
        {
            if (recipeChecked || PingPlugin.PingRecipe == null || GameMain.history == null) return;
            recipeChecked = true;

            GameHistoryData history = GameMain.history;
            int recipeId = PingPlugin.PingRecipe.ID;
            if (history.TechUnlocked(PingPlugin.UNLOCK_TECH_ID) && !history.RecipeUnlocked(recipeId))
            {
                history.UnlockRecipe(recipeId);
                PingPlugin.Log.LogInfo("PING: recipe unlocked retroactively (tech already researched in this save).");
            }
        }

        public static void FactoryTickPostfix(FactorySystem __instance, long time, bool isActive)
        {
            try
            {
                PlanetFactory factory = __instance?.factory;
                if (factory == null || factory.entityPool == null || factory.planetId <= 0 || GameMain.data == null) return;

                EnsureRecipeUnlocked();

                int planetId = factory.planetId;
                long lastSync;
                if (!LastSyncTick.TryGetValue(planetId, out lastSync) || time < lastSync || time - lastSync >= SyncIntervalTicks)
                {
                    SyncPortals(factory);
                    LastSyncTick[planetId] = time;
                }
            }
            catch (Exception ex) { PingPlugin.SafeError("FactoryTickPostfix failed", ex); }
        }
    }
}