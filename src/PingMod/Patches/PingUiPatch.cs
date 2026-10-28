using System;
using HarmonyLib;
using PingMod.UI;

namespace PingMod.Patches
{
    // Connects the PING target window to the game's UI:
    //  * clicking a PING portal opens the window (instead of the accumulator window),
    //  * any other inspect change or "close all windows" closes it,
    //  * the window gets its per-frame update from UIGame.
    public static class PingUiPatch
    {
        public static void Install(Harmony harmony)
        {
            PatchHelper.TryPatch(harmony, typeof(UIGame), "OnPlayerInspecteeChange", new[] { typeof(EObjectType), typeof(int) },
                postfix: PatchHelper.Method(typeof(PingUiPatch), nameof(InspecteePostfix)));
            PatchHelper.TryPatch(harmony, typeof(UIGame), "ShutAllFunctionWindow",
                postfix: PatchHelper.Method(typeof(PingUiPatch), nameof(ShutAllPostfix)));
            PatchHelper.TryPatch(harmony, typeof(UIGame), "_OnUpdate",
                postfix: PatchHelper.Method(typeof(PingUiPatch), nameof(UpdatePostfix)));
        }

        // Runs after the vanilla handler, which may already have opened the accumulator window for the portal.
        public static void InspecteePostfix(UIGame __instance, EObjectType objType, int objId)
        {
            try
            {
                if (objType == EObjectType.Entity && objId > 0)
                {
                    PortalData portal = PingTicker.FindPortalForEntity(objId);
                    if (portal != null)
                    {
                        __instance.ShutAccumulatorWindow();
                        PingTargetWindow.Open(portal);
                        return;
                    }
                }
                PingTargetWindow.Close();
            }
            catch (Exception ex) { PingPlugin.SafeError("InspecteePostfix failed", ex); }
        }

        public static void ShutAllPostfix()
        {
            PingTargetWindow.Close();
        }

        public static void UpdatePostfix()
        {
            PingTargetWindow.Tick();
        }
    }
}