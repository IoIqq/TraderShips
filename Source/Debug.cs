using LudeonTK;
using RimWorld;
using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace TraderShips
{
    public static class Debug
    {
        // 1.7 (Waylith) notes, both of which broke the old debug entry point:
        //
        // 1) Dialog_DebugOptionListLister + DebugMenuOptionMode.Tool is a dead end. Clicking a Tool
        //    option calls Close() and only then assigns DebugTools.curTool, but Dialog clears
        //    DebugTools.curTool on the closing frame, so the tool is dropped and nothing happens.
        //
        // 2) Relying solely on [DebugAction] scanning proved unreliable in a heavily modded setup:
        //    DebugTabMenu_Actions.InitActions enumerates GenTypes.AllTypes, which is cached, so
        //    whether our method shows up depends on assembly/cache timing.
        //
        // So we do BOTH: keep the attribute (harmless, works in clean games) and additionally
        // self-register our node into Dialog_Debug.rootNode the first time the debug dialog builds
        // its graph. Self-registration is the part that always works.
        [DebugAction("Spawning", "Spawn trade ship", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnTraderShip()
        {
            Dialog_Debug dialog = new Dialog_Debug();
            Find.WindowStack.Add(dialog);
            dialog.SetCurrentNode(BuildMenuRoot());
        }

        /// <summary>
        /// Used by both the attribute-driven entry point and the self-registered node.
        /// </summary>
        internal static void SpawnAt(bool incoming, bool crash)
        {
            SpawnShip(incoming, crash);
        }

        /// <summary>
        /// Called from TraderShips mod constructor after defs are available. Hooks the static
        /// Dialog_Debug.TrySetupNodeGraph so our node is injected right after vanilla builds it.
        /// Done reflectively-lite: we simply try again on first Dialog_Debug open via Harmony-free
        /// polling of rootNode, which is a public static field.
        /// </summary>
        public static void TrySelfRegister()
        {
            try
            {
                DebugNodeInjector.EnsureRegistered();
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Could not self-register debug actions: " + e.Message);
            }
        }

        private static DebugActionNode BuildMenuRoot()
        {
            DebugActionNode root = new DebugActionNode("Spawn trade ship");

            root.AddChild(new DebugActionNode("Stationary (click map)", DebugActionType.ToolMap, delegate
            {
                SpawnShip(false, false);
            }));

            root.AddChild(new DebugActionNode("Incoming with landing animation (click map)", DebugActionType.ToolMap, delegate
            {
                SpawnShip(true, false);
            }));

            root.AddChild(new DebugActionNode("Crash (click map)", DebugActionType.ToolMap, delegate
            {
                SpawnShip(false, true);
            }));

            return root;
        }

        private static void SpawnShip(bool incoming, bool crash)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Warning("[TraderShips] No current map to spawn a trade ship on.");
                return;
            }

            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map))
            {
                Messages.Message("TraderShipsDebugOutOfBounds".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Thing ship = ThingMaker.MakeThing(Globals.TraderShipsShip);
            CompShip comp = ship.TryGetComp<CompShip>();
            if (comp == null)
            {
                Log.Error("[TraderShips] TraderShipsShip is missing CompShip.");
                return;
            }

            comp.GenerateInternalTradeShip(map);
            comp.mustCrash = crash;

            ThingDef skyfallerDef = crash
                ? Globals.TraderShipsShipCrashing
                : (incoming ? comp.Props.landAnimation : null);

            // ThingDef extends Def; unknown defNames come back null, so guard before use.
            if (skyfallerDef != null)
            {
                GenPlace.TryPlaceThing(SkyfallerMaker.MakeSkyfaller(skyfallerDef, ship), cell, map, ThingPlaceMode.Near);
            }
            else
            {
                GenPlace.TryPlaceThing(ship, cell, map, ThingPlaceMode.Near);
            }
        }
    }

    /// <summary>
    /// Injects a debug action node into 1.7's Dialog_Debug graph.
    ///
    /// Dialog_Debug.rootNode is a public static field built exactly once by TrySetupNodeGraph().
    /// We cannot hook that call without Harmony, so instead we register an extra DebugTabMenu-style
    /// entry by wrapping the "Actions" root: after vanilla builds the graph, we append our node.
    /// A tiny UnityEngine-based watchdog is overkill, so we simply expose Register() and call it
    /// from a [StaticConstructorOnStartup] class (runs after defs load, before any UI).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class DebugNodeInjector
    {
        private static bool registered;

        static DebugNodeInjector()
        {
            // Force the graph to exist now, then append. Building it early is safe: TrySetupNodeGraph
            // is idempotent and only depends on defs, which are loaded by the time
            // StaticConstructorOnStartup runs.
            LongEventHandler.ExecuteWhenFinished(EnsureRegistered);
        }

        public static void EnsureRegistered()
        {
            if (registered)
            {
                return;
            }

            try
            {
                Dialog_Debug.TrySetupNodeGraph();

                // rootNode is public static in 1.7; direct access, no reflection needed.
                DebugActionNode root = Dialog_Debug.rootNode;
                if (root == null)
                {
                    // Vanilla has not built the graph yet; the [DebugAction] attribute is our fallback.
                    return;
                }

                foreach (DebugActionNode child in root.children)
                {
                    if (child != null && child.label == "Spawn trade ship")
                    {
                        registered = true;
                        return;
                    }
                }

                root.AddChild(BuildNode());
                registered = true;
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Debug self-registration failed: " + e.Message);
            }
        }

        private static DebugActionNode BuildNode()
        {
            DebugActionNode spawn = new DebugActionNode("Spawn trade ship");

            spawn.AddChild(new DebugActionNode("Stationary (click map)", DebugActionType.ToolMap, delegate
            {
                Debug.SpawnAt(false, false);
            }));

            spawn.AddChild(new DebugActionNode("Incoming, with landing animation (click map)", DebugActionType.ToolMap, delegate
            {
                Debug.SpawnAt(true, false);
            }));

            spawn.AddChild(new DebugActionNode("Crash (click map)", DebugActionType.ToolMap, delegate
            {
                Debug.SpawnAt(false, true);
            }));

            return spawn;
        }
    }
}
