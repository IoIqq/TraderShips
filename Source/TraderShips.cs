using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace TraderShips
{
    public class TraderShips : Mod
    {
        public static TraderShipsSettings settings;

        public TraderShips(ModContentPack pack) : base(pack)
        {
            settings = GetSettings<TraderShipsSettings>();

            ApplyHarmonyPatches();
        }

        // Harmony is an *optional* runtime dependency, used only for the comms-console
        // "request trade ship" option (it patches the static FactionDialogMaker.FactionDialogFor,
        // which XML cannot reach). Everything else — ships arriving, replacing orbital traders,
        // trading, landing zones — works with no dependencies at all.
        //
        // Referencing HarmonyLib.Harmony directly in this constructor would throw TypeLoadException
        // inside CreateModClasses when Harmony is absent; RimWorld then treats the whole load as
        // failed and resets ModsConfig. So everything is resolved reflectively and degrades gracefully.
        private void ApplyHarmonyPatches()
        {
            try
            {
                Type harmonyType = GenTypes.GetTypeInAnyAssembly("HarmonyLib.Harmony");
                if (harmonyType == null)
                {
                    Log.Warning("[TraderShips] Harmony (brrainz.harmony) is not loaded. "
                        + "Trader ships still arrive; vanilla orbital traders are left untouched.");
                    return;
                }

                Type harmonyMethodType = GenTypes.GetTypeInAnyAssembly("HarmonyLib.HarmonyMethod");
                if (harmonyMethodType == null)
                {
                    Log.Warning("[TraderShips] HarmonyLib.HarmonyMethod not found; skipping patches.");
                    return;
                }

                object harmony = Activator.CreateInstance(harmonyType, "com.github.automatic1111.traderships");

                // Harmony.Patch(MethodBase original, HarmonyMethod prefix, HarmonyMethod postfix)
                MethodInfo patchMethod = FindPatchMethod(harmonyType);
                if (patchMethod == null)
                {
                    Log.Warning("[TraderShips] Harmony is present but no usable Patch(...) signature was found; "
                        + "skipping orbital trader patches.");
                    return;
                }

                // NOTE: the orbital-trader interception no longer uses Harmony. It is done by
                // swapping IncidentDef.workerClass in 1.7/Patches/OrbitalTrader.xml, which points
                // at IncidentWorkerTraderShipArrival. That keeps the core mod (ships arrive and
                // trade) working with zero dependencies; Harmony is only needed for the extra
                // comms-console option below, which patches a static method and cannot be
                // replaced through XML.

                // Comms console: add a standalone "Request trade ship" option to the faction
                // dialog. FactionDialogMaker.FactionDialogFor is public static and returns the
                // root DiaNode, so a postfix can append to it without touching vanilla options.
                PatchFactionDialogFor(patchMethod, harmonyMethodType, harmony);
            }
            catch (Exception e)
            {
                // Never let an optional dependency break mod loading.
                Log.Warning("[TraderShips] Could not apply Harmony patches; orbital traders stay vanilla.\n"
                    + (e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e));
            }
        }

        /// <summary>
        /// Finds the 3-argument Patch overload (original, prefix, postfix) and builds the argument
        /// array, padding the optional trailing parameters from Harmony 3 if needed.
        /// </summary>
        private static MethodInfo FindPatchMethod(Type harmonyType)
        {
            MethodInfo best = null;
            foreach (MethodInfo m in harmonyType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "Patch")
                {
                    continue;
                }

                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length < 3)
                {
                    continue;
                }

                // First three must look like: MethodBase, HarmonyMethod, HarmonyMethod
                if (!typeof(MethodBase).IsAssignableFrom(ps[0].ParameterType))
                {
                    continue;
                }

                if (best == null || ps.Length < best.GetParameters().Length)
                {
                    best = m;
                }
            }

            return best;
        }


        /// <summary>
        /// Postfix FactionDialogMaker.FactionDialogFor(Map, Faction, Pawn).
        /// Done reflectively for the same reason as the orbital patches: no HarmonyLib reference.
        /// </summary>
        private static void PatchFactionDialogFor(MethodInfo patchMethod, Type harmonyMethodType, object harmonyInstance)
        {
            // Vanilla signature (FactionDialogMaker.cs:16) is
            //   public static DiaNode FactionDialogFor(Pawn negotiator, Faction faction)
            // — two parameters, no Map. Getting this wrong makes GetMethod return null and the
            // whole comms option silently disappears.
            MethodInfo original = typeof(FactionDialogMaker).GetMethod(
                "FactionDialogFor",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { typeof(Pawn), typeof(Faction) },
                null);

            if (original == null)
            {
                Log.Warning("[TraderShips] FactionDialogMaker.FactionDialogFor not found; comms ship request unavailable.");
                return;
            }

            MethodInfo postfix = typeof(CommsShipRequestPatches).GetMethod(
                "FactionDialogFor_Postfix",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (postfix == null)
            {
                Log.Warning("[TraderShips] CommsShipRequestPatches.FactionDialogFor_Postfix not found.");
                return;
            }

            object harmonyPostfix;
            try
            {
                harmonyPostfix = Activator.CreateInstance(harmonyMethodType, postfix);
            }
            catch (MissingMethodException)
            {
                MethodInfo ctor = harmonyMethodType.GetMethod("FromMethod", BindingFlags.Public | BindingFlags.Static);
                if (ctor == null)
                {
                    Log.Warning("[TraderShips] Cannot construct HarmonyMethod for FactionDialogFor.");
                    return;
                }
                harmonyPostfix = ctor.Invoke(null, new object[] { postfix });
            }

            ParameterInfo[] ps = patchMethod.GetParameters();
            object[] args = new object[ps.Length];
            args[0] = original;
            args[1] = null;
            args[2] = harmonyPostfix;
            for (int i = 3; i < args.Length; i++)
            {
                args[i] = ps[i].ParameterType.IsValueType
                    ? Activator.CreateInstance(ps[i].ParameterType)
                    : null;
            }

            try
            {
                patchMethod.Invoke(harmonyInstance, args);
            }
            catch (TargetInvocationException tie)
            {
                Log.Warning("[TraderShips] Patch of FactionDialogFor failed: "
                    + (tie.InnerException ?? tie).Message);
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            if (settings != null)
            {
                settings.DoSettingsWindowContents(inRect);
            }
        }

        public override string SettingsCategory()
        {
            return "TraderShipsTitle".Translate();
        }
    }
}