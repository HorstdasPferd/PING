using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace PingMod.Patches
{
    // Makes the Galactic Map's "fast travel" button available next to a PING portal.
    // The button and its click handler are normally sandbox-only (GameMain.sandboxToolsEnabled).
    // Only these two methods are changed, so right-click travel and the globe teleport stay disabled in normal games.
    public static class FastTravelPatch
    {
        static readonly MethodInfo SandboxGetter = typeof(GameMain).GetMethod("get_sandboxToolsEnabled");
        static readonly MethodInfo SandboxReplacement = typeof(FastTravelPatch).GetMethod(nameof(PingSandboxCheck));

        // True in real sandbox mode, or while the player stands next to a PING portal.
        public static bool PingSandboxCheck()
        {
            return GameMain.sandboxToolsEnabled || PingTicker.FindNearestPortalInRange() != null;
        }

        // Redirects every call of GameMain.sandboxToolsEnabled to PingSandboxCheck.
        public static IEnumerable<CodeInstruction> SandboxTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction ins in instructions)
            {
                if (SandboxGetter != null && ins.opcode == OpCodes.Call && ins.operand is MethodInfo method && method == SandboxGetter)
                    ins.operand = SandboxReplacement;
                yield return ins;
            }
        }

        public static void Install(Harmony harmony)
        {
            HarmonyMethod transpiler = PatchHelper.Method(typeof(FastTravelPatch), nameof(SandboxTranspiler));

            int applied = 0;
            if (PatchHelper.TryPatch(harmony, typeof(UIStarmap), "UpdateCursorView", transpiler: transpiler)) applied++;
            if (PatchHelper.TryPatch(harmony, typeof(UIStarmap), "OnFastTravelButtonClick", transpiler: transpiler)) applied++;
            PingPlugin.Log.LogInfo($"PING: {applied}/2 fast-travel button patches applied.");
        }
    }

    // Takes over the fast travel button while the player is next to a PING portal.
    public static class PingStarmapPatch
    {
        public static void Install(Harmony harmony)
        {
            PatchHelper.TryPatch(harmony, typeof(UIStarmap), "StartFastTravelToPlanet",
                prefix: PatchHelper.Method(typeof(PingStarmapPatch), nameof(StartFastTravelToPlanetPrefix)));
            PatchHelper.TryPatch(harmony, typeof(PlayerMove_Sail), "FastTravelToPlanetFinally",
                postfix: PatchHelper.Method(typeof(PingStarmapPatch), nameof(ArrivalPostfix)));
        }

        // Parameter names must match the original method ('destPlanet').
        public static bool StartFastTravelToPlanetPrefix(PlanetData destPlanet)
        {
            try
            {
                // Real sandbox mode keeps the vanilla behaviour.
                if (GameMain.sandboxToolsEnabled || destPlanet == null) return true;

                PortalData portal = PingTicker.FindNearestPortalInRange();
                if (portal == null) return true;

                string error = PingTicker.TryStartTeleport(portal, destPlanet);
                if (error != null)
                {
                    PingPlugin.Log.LogInfo(error);
                    UIRealtimeTip.Popup(error, false, 0);
                }
                return false;
            }
            catch (Exception ex)
            {
                PingPlugin.SafeError("StartFastTravelToPlanetPrefix failed", ex);
                return true;
            }
        }

        // The game calls FastTravelToPlanetFinally when the player has arrived on the planet.
        public static void ArrivalPostfix(PlanetData planet)
        {
            try { PingTicker.OnArrived(planet); }
            catch (Exception ex) { PingPlugin.SafeError("ArrivalPostfix failed", ex); }
        }
    }
}