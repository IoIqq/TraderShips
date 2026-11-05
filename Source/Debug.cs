using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
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
            dialog.SetCurrentNode(BuildNode());
        }

        /// <summary>
        /// Native 1.7 extension point. DebugTabMenu_Actions.InitActions scans every static method
        /// carrying [DebugActionYielder] and adds the returned nodes to the Actions tab:
        ///
        ///     if (!methodInfo.TryGetAttribute<DebugActionYielderAttribute>(out var _)) continue;
        ///     foreach (DebugActionNode item in (IEnumerable<DebugActionNode>)methodInfo.Invoke(null, null))
        ///         myRoot.AddChild(item);
        ///
        /// This replaces the old approach of reaching into Dialog_Debug.rootNode by hand.
        /// DebugNodeInjector is kept as a fallback: InitActions enumerates GenTypes.AllTypes,
        /// which is a cached snapshot, and in heavily modded setups our type can be missing from it.
        /// </summary>
        [DebugActionYielder]
        public static IEnumerable<DebugActionNode> TradeShipDebugActions()
        {
            yield return BuildNode();
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

        /// <summary>
        /// Single source of truth for the debug node tree, shared by the [DebugActionYielder]
        /// path and the DebugNodeInjector fallback so the two can never drift apart.
        /// </summary>
        internal static DebugActionNode BuildNode()
        {
            DebugActionNode root = new DebugActionNode("Spawn trade ship");

            root.AddChild(new DebugActionNode("Stationary (click map)", DebugActionType.ToolMap, delegate
            {
                SpawnShip(false, false);
            }));

            root.AddChild(new DebugActionNode("Incoming, with landing animation (click map)", DebugActionType.ToolMap, delegate
            {
                SpawnShip(true, false);
            }));

            root.AddChild(new DebugActionNode("Crash (click map)", DebugActionType.ToolMap, delegate
            {
                SpawnShip(false, true);
            }));

            root.AddChild(new DebugActionNode("=== Run all self-test probes ===", DebugActionType.Action, delegate
            {
                RunAllProbes();
            }));

            return root;
        }

        // ===================== 自检探针 =====================
        // 这几项读日志判断不了（"现金显示对不对""派系列表拉出来是什么"），
        // 只能在游戏里跑一遍把结果写到磁盘。
        // 输出：<mod>/_autotest/probe.log，由 tools/collect-autotest.ps1 收集。

        /// <summary>只写文件，不弹消息，连续调用也不会刷屏。</summary>
        internal static void Probe(string line)
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                    "_autotest");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "probe.log"),
                    DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Probe write failed: " + e.Message);
            }
        }

        /// <summary>
        /// 一次性导出所有轨道商船型：名称、派系、货物条数、银两区间。
        /// 银两区间走的是 Dialog_TradeDispatch 那条反射路径，
        /// 这里显示 0~0 就说明反射没拿到原版 StockGenerator_SingleDef。
        /// </summary>
        public static void DumpTraderKinds()
        {
            Probe("=== DumpTraderKinds ===");

            int total = 0;
            int themed = 0;
            int emptyCash = 0;

            foreach (TraderKindDef kind in DefDatabase<TraderKindDef>.AllDefsListForReading)
            {
                if (!kind.orbital) continue;
                total++;

                int stockDefs = 0;
                IntRange silver = new IntRange(0, 0);
                foreach (StockGenerator gen in kind.stockGenerators)
                {
                    if (gen is StockGenerator_ThingDefs ours)
                    {
                        if (ours.thingDef != null) stockDefs++;
                    }
                    else if (gen is StockGenerator_SingleDef)
                    {
                        stockDefs++;
                    }

                    ThingDef stocked = Dialog_TradeDispatch.StockedDefOf(gen);
                    if (stocked == null || stocked != ThingDefOf.Silver) continue;
                    silver = new IntRange(silver.min + gen.countRange.min, silver.max + gen.countRange.max);
                }

                if (kind.defName.StartsWith("TraderShipsAuto_")) themed++;
                if (silver.max <= 0) emptyCash++;

                Probe(string.Format("{0,-48} {1,-22} stock={2,-3} silver={3}~{4}",
                    kind.defName,
                    kind.faction != null ? kind.faction.defName : "-",
                    stockDefs,
                    silver.min,
                    silver.max));
            }

            Probe(string.Format("TOTAL orbital kinds={0} themed={1} emptyCash={2}", total, themed, emptyCash));
            Probe("=== DumpTraderKinds end ===");
        }

        public static void DumpDispatchFactions()
        {
            Probe("=== DumpDispatchFactions ===");
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Probe("no current map");
                return;
            }

            int n = 0;
            foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f.def.hidden) continue;
                if (f.def.pawnsPlural.NullOrEmpty()) continue;

                List<TraderKindDef> kinds = CommsShipRequest.RequestableKinds(map, f);
                int price = CommsShipRequest.PriceFor(f, 1000);
                Probe(string.Format("{0,-30} relation={1,-14} kinds={2,-3} price1000={3}",
                    f.def.defName, f.RelationKindWith(Faction.OfPlayer), kinds.Count, price));

                for (int i = 0; i < kinds.Count && i < 4; i++)
                {
                    TraderKindDef k = kinds[i];
                    int defs = 0;
                    foreach (StockGenerator gen in k.stockGenerators)
                    {
                        if (gen is StockGenerator_ThingDefs ours && ours.thingDef != null) defs++;
                    }
                    Probe(string.Format("    - {0,-48} stockDefs={1}", k.defName, defs));
                }
                n++;
            }
            Probe("factions=" + n);
            Probe("=== DumpDispatchFactions end ===");
        }

        public static void DumpBuildings()
        {
            Probe("=== DumpBuildings ===");
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Probe("no current map");
                return;
            }

            int towers = 0;
            int ships = 0;
            foreach (Thing t in map.listerThings.AllThings)
            {
                if (t is Building_TradeDispatch)
                {
                    towers++;
                    CompPowerTrader power = t.TryGetComp<CompPowerTrader>();
                    Probe(string.Format("tower at {0} power={1}", t.Position, power != null && power.PowerOn));
                }
                if (t is Building_Ship)
                {
                    ships++;
                    CompShip cs = t.TryGetComp<CompShip>();
                    Probe(string.Format("player ship at {0} hp={1}/{2} sprite={3}",
                        t.Position, t.HitPoints, t.MaxHitPoints, cs != null && cs.Sprite != null));
                }
            }

            Probe("towers=" + towers + " playerShips=" + ships);
            Probe("=== DumpBuildings end ===");
        }

        /// <summary>
        /// 人才船 / 奴隶船自检：直接调用生成器造人，看能不能出货。
        ///
        /// 这一项必须放进探针，因为原版 StockGenerator_Slaves 曾经在这里抛
        /// NullReferenceException，而抛错只发生在"开船/开交易窗"那一刻，
        /// 不看代码根本发现不了 —— 玩家看到的现象只是"船里空的"。
        /// </summary>
        public static void DumpPawnShips()
        {
            Probe("=== DumpPawnShips ===");

            Map map = Find.CurrentMap;
            PlanetTile tile = map != null ? map.Tile : PlanetTile.Invalid;

            int checkedKinds = 0;
            int emptyKinds = 0;

            foreach (TraderKindDef kind in DefDatabase<TraderKindDef>.AllDefsListForReading)
            {
                if (!kind.defName.StartsWith("TraderShipsAuto_")) continue;
                if (!kind.defName.EndsWith("_Talent") && !kind.defName.EndsWith("_Slave")) continue;

                StockGenerator_Pawns pawns = null;
                foreach (StockGenerator gen in kind.stockGenerators)
                {
                    if (gen is StockGenerator_Pawns p) { pawns = p; break; }
                }

                checkedKinds++;

                if (pawns == null)
                {
                    Probe(kind.defName + "  NO StockGenerator_Pawns");
                    emptyKinds++;
                    continue;
                }

                // TraderKindDef.faction 是 FactionDef；生成器要的是场上的 Faction 实例。
                Faction owner = null;
                if (kind.faction != null)
                {
                    foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
                    {
                        if (f.def == kind.faction) { owner = f; break; }
                    }
                }

                int n = 0;
                string sample = "-";
                try
                {
                    foreach (Thing t in pawns.GenerateThings(tile, owner))
                    {
                        n++;
                        if (sample == "-" && t is Pawn pw)
                        {
                            sample = pw.Name?.ToStringShort ?? pw.KindLabel;
                            if (pw.skills != null)
                            {
                                int best = 0;
                                foreach (SkillRecord rec in pw.skills.skills)
                                {
                                    if (rec.Level > best) best = rec.Level;
                                }
                                sample += " bestSkill=" + best;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Probe(kind.defName + "  EX: " + e.GetType().Name + " " + e.Message);
                    emptyKinds++;
                    continue;
                }

                if (n == 0) emptyKinds++;
                Probe(string.Format("{0,-46} kind={1,-24} count={2} sample={3}",
                    kind.defName, pawns.pawnKindDef != null ? pawns.pawnKindDef.defName : "NULL", n, sample));
            }

            Probe(string.Format("pawnShips={0} empty={1}", checkedKinds, emptyKinds));
            Probe("=== DumpPawnShips end ===");
        }

        /// <summary>自检总入口：一次点击跑完所有探针。</summary>
        public static void RunAllProbes()
        {
            Probe("");
            Probe("########## RunAllProbes " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ##########");
            try { DumpTraderKinds(); } catch (Exception e) { Probe("DumpTraderKinds EX: " + e); }
            try { DumpDispatchFactions(); } catch (Exception e) { Probe("DumpDispatchFactions EX: " + e); }
            try { DumpBuildings(); } catch (Exception e) { Probe("DumpBuildings EX: " + e); }
            try { DumpPawnShips(); } catch (Exception e) { Probe("DumpPawnShips EX: " + e); }
            Probe("########## end ##########");
            Messages.Message("TraderShips probe written to _autotest/probe.log", MessageTypeDefOf.TaskCompletion, false);
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

                root.AddChild(Debug.BuildNode());
                registered = true;
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Debug self-registration failed: " + e.Message);
            }
        }

    }
}