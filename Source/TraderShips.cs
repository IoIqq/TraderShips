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

        // Harmony is an *optional* runtime dependency: it is only present when the player has the
        // Harmony mod enabled. Referencing HarmonyLib.Harmony directly in this constructor throws
        // TypeLoadException inside CreateModClasses, and RimWorld then treats the whole load as
        // failed and resets ModsConfig. So resolve everything reflectively and degrade gracefully:
        // without Harmony we leave vanilla orbital traders alone, and the rest of the mod still works.
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

                PatchTarget(patchMethod, harmonyMethodType, harmony,
                    "CanFireNowSub", "CanFireNowSub_Prefix");

                PatchTarget(patchMethod, harmonyMethodType, harmony,
                    "TryExecuteWorker", "TryExecuteWorker_Prefix");
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

        private static void PatchTarget(
            MethodInfo patchMethod, Type harmonyMethodType, object harmonyInstance,
            string originalName, string prefixName)
        {
            MethodInfo original = typeof(IncidentWorker_OrbitalTraderArrival).GetMethod(
                originalName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (original == null)
            {
                Log.Warning($"[TraderShips] IncidentWorker_OrbitalTraderArrival.{originalName} not found; skipping.");
                return;
            }

            MethodInfo prefix = typeof(global::TraderShips.Patch.OrbitalTraderPatches).GetMethod(
                prefixName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (prefix == null)
            {
                Log.Warning($"[TraderShips] prefix {prefixName} not found; skipping.");
                return;
            }

            object harmonyPrefix;
            try
            {
                harmonyPrefix = Activator.CreateInstance(harmonyMethodType, prefix);
            }
            catch (MissingMethodException)
            {
                // Some versions only expose HarmonyMethod(MethodInfo) via a static factory.
                MethodInfo ctor = harmonyMethodType.GetMethod("FromMethod", BindingFlags.Public | BindingFlags.Static);
                if (ctor == null)
                {
                    Log.Warning("[TraderShips] Cannot construct HarmonyMethod; skipping patch.");
                    return;
                }

                harmonyPrefix = ctor.Invoke(null, new object[] { prefix });
            }

            ParameterInfo[] ps = patchMethod.GetParameters();
            object[] args = new object[ps.Length];
            args[0] = original;
            args[1] = harmonyPrefix;
            args[2] = null;
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
                Log.Warning($"[TraderShips] Patch of {originalName} failed: "
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
