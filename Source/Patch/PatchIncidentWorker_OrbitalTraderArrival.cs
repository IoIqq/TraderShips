using RimWorld;
using System;

namespace TraderShips.Patch
{
    // These are plain static methods, NOT [HarmonyPatch] classes.
    //
    // Attribute-driven patching would force a hard compile-time dependency on HarmonyLib: the
    // CLR resolves attribute types as soon as it enumerates the type, so with Harmony absent
    // RimWorld threw TypeLoadException for 'HarmonyLib.HarmonyPatch' and then reset ModsConfig.
    // Patching reflectively from TraderShips.ApplyHarmonyPatches keeps Harmony strictly optional.
    internal static class OrbitalTraderPatches
    {
        public static bool CanFireNowSub_Prefix(ref bool __result, IncidentParms parms)
        {
            TraderShipsSettings settings = TraderShips.settings;
            if (settings == null || !settings.disableOrbital)
            {
                return true;
            }

            IncidentWorkerTraderShip worker = Globals.TraderShipsArrival?.Worker as IncidentWorkerTraderShip;
            if (worker == null)
            {
                return true;
            }

            __result = worker.IsAllowed(parms);
            return false;
        }

        public static bool TryExecuteWorker_Prefix(ref bool __result, IncidentParms parms)
        {
            TraderShipsSettings settings = TraderShips.settings;
            if (settings == null || !settings.disableOrbital)
            {
                return true;
            }

            IncidentWorkerTraderShip worker = Globals.TraderShipsArrival?.Worker as IncidentWorkerTraderShip;
            if (worker == null)
            {
                return true;
            }

            __result = worker.TryExecuteWorkerPub(parms);
            return false;
        }
    }
}
