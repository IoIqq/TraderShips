using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace TraderShips
{
    class IncidentWorkerTraderShip : IncidentWorker
    {
        bool HaveConsole(Map map)
        {
            return map.listerBuildings.allBuildingsColonist.Any((Building b) => b.def.IsCommsConsole && (b.GetComp<CompPowerTrader>() == null || b.GetComp<CompPowerTrader>().PowerOn));
        }

        /// <summary>
        /// Tick of the last ship that actually arrived. Used only for the optional pacing setting;
        /// deliberately not persisted, so loading a save resets the window rather than inheriting a
        /// stale timestamp from whenever the player last quit.
        /// </summary>
        internal static int LastArrivalTick = -999999;

        /// <summary>
        /// Spawned ships currently parked on this map.
        /// Unlike vanilla orbital traders, landed ships occupy real map space and all aim for the
        /// same landing zone, so an unbounded number of them pile up. Vanilla caps orbital traders
        /// at 5 (IncidentWorker_OrbitalTraderArrival.MaxShips); this is the equivalent guard.
        /// </summary>
        public static int CountLandedShips(Map map)
        {
            if (map == null || Globals.TraderShipsShip == null) return 0;
            return map.listerThings.ThingsOfDef(Globals.TraderShipsShip).Count;
        }

        public bool IsAllowed(IncidentParms parms)
        {
            if (TraderShips.settings.requireCommsConsole && !HaveConsole((Map)parms.target)) return false;

            Map targetMap = (Map)parms.target;
            int maxShips = TraderShips.settings.maxLandedShips;
            if (maxShips > 0 && CountLandedShips(targetMap) >= maxShips) return false;

            // Optional pacing: keep random arrivals at least N days apart. 0 disables the check
            // and leaves the vanilla storyteller cadence untouched.
            float minDays = TraderShips.settings.shipArrivalIntervalDays;
            if (minDays > 0f)
            {
                int minTicks = UnityEngine.Mathf.RoundToInt(minDays * 60000f);
                if (Find.TickManager.TicksGame - LastArrivalTick < minTicks) return false;
            }

            return true;
        }

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            return !TraderShips.settings.disableOrbital && IsAllowed(parms);
        }

        public static bool FindAnyLandingSpot(out IntVec3 spot, Faction faction, Map map, IntVec2? size)
        {
            if (DropCellFinder.FindSafeLandingSpot(out spot, faction, map, 0, 15, 25, size))
            {
                return true;
            }

            // 兜底采样要对整艘船校验，否则会返回一个"单格合法、整块放不下"的点，
            // 上层拿去 GenPlace 就会失败（以前就是这样变成"呼叫成功但看不到船"）。
            int w = size.HasValue ? size.Value.x : 1;
            int h = size.HasValue ? size.Value.z : 1;

            for (int i = 0; i < 20; i++)
            {
                IntVec3 probe = DropCellFinder.RandomDropSpot(map);

                if (CanHostShip(map, probe, w, h))
                {
                    spot = probe;
                    return true;
                }

                IntVec3 near;
                if (DropCellFinder.TryFindDropSpotNear(probe, map, out near, false, false, false, size)
                    && CanHostShip(map, near, w, h))
                {
                    spot = near;
                    return true;
                }
            }

            spot = IntVec3.Invalid;
            return false;
        }

        /// <summary>
        /// 这块地能不能放下整艘船（size 格全空、不在雾里、地形可通行）。
        ///
        /// FindAnyLandingSpot 最后的兜底是"随机掉点附近的格子"，对 1×1 的货物够用，
        /// 但船是 7×3 的多格建筑：落点合法不等于整块矩形都空着。
        /// 不校验就会先报"已抵达"、再 GenPlace 失败 —— 玩家什么也看不到。
        /// </summary>
        public static bool CanHostShip(Map map, IntVec3 root, int w, int h)
        {
            if (map == null) return false;

            for (int dx = 0; dx < w; dx++)
            {
                for (int dz = 0; dz < h; dz++)
                {
                    IntVec3 c = new IntVec3(root.x + dx, 0, root.z + dz);

                    if (!c.InBounds(map)) return false;
                    if (c.Fogged(map)) return false;
                    if (c.GetEdifice(map) != null) return false;

                    TerrainDef terrain = c.GetTerrain(map);
                    if (terrain == null) return false;
                    if (terrain.passability == Traversability.Impassable) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 最后手段：在整张地图上找一块放得下船的空地。
        ///
        /// 玩家把着陆区堵死时不再"呼叫失败扣不扣钱"这种二选一 ——
        /// 船会自己找个空地落下来，和现实里飞行员会绕一圈找停机坪一样。
        ///
        /// 先用随机采样（快，通常几十次就中），全部落空再退化成步长扫描（慢但确定）。
        /// </summary>
        public static bool TryFindFallbackSpot(Map map, IntVec2 size, out IntVec3 center)
        {
            center = IntVec3.Invalid;
            if (map == null) return false;

            int w = UnityEngine.Mathf.Max(1, size.x);
            int h = UnityEngine.Mathf.Max(1, size.z);

            // 随机采样：多格建筑的合法落点通常不少，几十次足够。
            for (int i = 0; i < 60; i++)
            {
                IntVec3 probe = DropCellFinder.RandomDropSpot(map);
                if (CanHostShip(map, probe, w, h))
                {
                    center = probe;
                    return true;
                }
            }

            // 步长扫描：按船的尺寸跳着找，避免几万次逐格判定。
            int stride = UnityEngine.Mathf.Max(w, h);
            CellRect rect = CellRect.WholeMap(map);

            for (int x = rect.minX; x <= rect.maxX - w + 1; x += stride)
            {
                for (int z = rect.minZ; z <= rect.maxZ - h + 1; z += stride)
                {
                    IntVec3 probe = new IntVec3(x, 0, z);
                    if (CanHostShip(map, probe, w, h))
                    {
                        center = probe;
                        return true;
                    }
                }
            }

            return false;
        }

        public static void FindCloseLandingSpot(out IntVec3 spot, Faction faction, Map map, IntVec2? size)
        {
            IntVec3 center = default;
            int count = 0;
            foreach (Building building in map.listerBuildings.allBuildingsColonist.Where(x => x.def.size.x > 1 || x.def.size.z > 1))
            {
                center += building.Position;
                count++;
            }

            if (count == 0)
            {
                FindAnyLandingSpot(out spot, faction, map, size);
                return;
            }

            center.x /= count;
            center.z /= count;

            int spotCount = 20;
            float bestDistance = 999999f;

            spot = default;

            for (int i = 0; i < spotCount; i++)
            {
                IntVec3 s;
                FindAnyLandingSpot(out s, faction, map, size);

                if ((s - center).LengthManhattan < bestDistance)
                {
                    bestDistance = (s - center).LengthManhattan;
                    spot = s;
                }
            }
        }
        public static Thing MakeTraderShip(Map map, Faction forcedFaction = null, TraderKindDef forcedKind = null)
        {
            Thing ship = ThingMaker.MakeThing(Globals.TraderShipsShip, null);

            // 玩家在通讯台里选了船型时要用那个，不能随机 —— 否则选"绮罗族贸易飞船"
            // 来的却是别家的货（之前就是这样，绮罗族的船卖哥布林武器）。
            TraderKindDef traderKindDef = forcedKind;
            if (traderKindDef == null)
            {
                traderKindDef = PickRandomKind(map);
            }

            // 以前这里直接 throw，一个没选中的随机事件会把整条事件链打断。
            // 现在返回 null，交给调用方决定是跳过还是重试。
            if (traderKindDef == null) return null;

            CompShip comp = ship.TryGetComp<CompShip>();
            comp.GenerateInternalTradeShip(map, traderKindDef, forcedFaction);
            return ship;
        }

        /// <summary>
        /// 上一艘随机来船的派系定义，用来避免同一个派系连着来。
        ///
        /// 存 FactionDef 而不是 Faction 实例：这个字段不随存档保存，
        /// 跨读档后 Faction 实例会变成野引用（旧存档的 Faction 对象可能已被替换）。
        /// </summary>
        private static FactionDef lastRandomFactionDef;

        /// <summary>
        /// 随机挑一条能派船的船型。
        ///
        /// 两个改进：
        ///   1. 排除上一艘随机船的派系 —— 54 个派系随机抽，连着两次撞同一个
        ///      的概率本来就有 1/54，但玩家体感上"怎么又是这家"特别刺眼。
        ///   2. 排除完如果一条都不剩，退回完整候选集，而不是抛异常。
        /// </summary>
        private static TraderKindDef PickRandomKind(Map map)
        {
            List<TraderKindDef> all = new List<TraderKindDef>();
            List<TraderKindDef> noRepeat = new List<TraderKindDef>();

            foreach (TraderKindDef def in DefDatabase<TraderKindDef>.AllDefs)
            {
                if (!CanSpawn(map, def)) continue;

                all.Add(def);

                if (lastRandomFactionDef == null || def.faction != lastRandomFactionDef)
                {
                    noRepeat.Add(def);
                }
            }

            List<TraderKindDef> pool = noRepeat.Count > 0 ? noRepeat : all;
            if (pool.Count == 0) return null;

            TraderKindDef picked = pool.RandomElementByWeightWithFallback(
                (TraderKindDef d) => d.CalculatedCommonality);

            if (picked != null)
            {
                lastRandomFactionDef = picked.faction;
            }

            return picked;
        }

        /// <summary>
        /// 让船降落。返回 false 表示没落下来（找不到落点、坠落物放不下），
        /// 调用方必须据此判定失败 —— 否则会出现"扣了钱、提示已抵达，玩家却看不到船"。
        /// </summary>
        public static bool LandShip(Map map, Thing ship)
        {
            Thing blockingThing;
            IntVec3 center = IntVec3.Invalid;
            CompShip comp = ship.TryGetComp<CompShip>();

            Area_LandingZone lz = map.areaManager.LandingZone();
            if (lz != null && !lz.TryFindShipLandingArea(ship.def.size, out center, out blockingThing) && blockingThing != null)
            {
                Messages.Message("TraderShipsLandingZoneBlocked".Translate("TraderShipsBlockedBy".Translate(blockingThing)), blockingThing, MessageTypeDefOf.NeutralEvent, true);
            }

            if (!center.IsValid && ThingDefOf.ShipLandingBeacon != null && !DropCellFinder.TryFindShipLandingArea(map, out center, out blockingThing) && blockingThing != null)
            {
                Messages.Message("TraderShipsLandingZoneBlocked".Translate("TraderShipsBlockedBy".Translate(blockingThing)), blockingThing, MessageTypeDefOf.NeutralEvent, true);
            }

            int shipW = ship.def.size.x;
            int shipH = ship.def.size.z;

            // 落点判定必须是"整艘船放得下"，不能只看落点那一格。
            // 着陆区堵死、信标不方便时，先试殖民地附近的空地。
            if (!center.IsValid || !CanHostShip(map, center, shipW, shipH))
            {
                FindCloseLandingSpot(out center, comp.tradeShip.Faction, map, ship.def.size);
            }

            // 还是放不下就去整张图上找空地 —— 不再因为"着陆区被占"就让这次呼叫彻底失败。
            // 只有在整张图都腾不出空间时（比如地图被墙围死）才真的失败。
            if (!center.IsValid || !CanHostShip(map, center, shipW, shipH))
            {
                IntVec3 fallback;
                if (TryFindFallbackSpot(map, ship.def.size, out fallback))
                {
                    center = fallback;
                }
            }

            Thing faller = SkyfallerMaker.MakeSkyfaller(comp.Props.landAnimation, ship);
            if (!GenPlace.TryPlaceThing(faller, center, map, ThingPlaceMode.Near))
            {
                // 放不下就不要再让船"算到达"——否则会扣钱、会占停靠上限，玩家却看不到船。
                Log.Warning("[TraderShips] Could not place landing skyfaller at " + center);
                if (ship.Spawned) ship.DeSpawn();
                ship.Destroy(DestroyMode.Vanish);

                // 坠落物没落地就成了孤儿，它内部还持有 ship —— 一并销毁，别留悬空容器。
                if (!faller.Destroyed) faller.Destroy(DestroyMode.Vanish);
                return false;
            }

            return true;
        }

        /// <summary>通讯台里玩家选定的船型；只在一次 TryExecuteWorkerPub 内有效。</summary>
        private TraderKindDef forcedKindForNextCall;

        public bool TryExecuteWorkerPub(IncidentParms parms, TraderKindDef forcedKind = null)
        {
            forcedKindForNextCall = forcedKind;
            return TryExecuteWorker(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            // 呼叫飞船时 parms.faction 是玩家指定的派系；用它兜底，保证叫来的船确实属于该派系。
            Thing ship = MakeTraderShip(map, parms.faction, forcedKindForNextCall);

            // 没有可派的船型（或生成失败）——当作事件没发生，不要抛异常打断整条链。
            if (ship == null) return false;

            // 没落下来就当这次事件没发生：不更新节奏窗口，也不让调用方误以为成功。
            if (!LandShip(map, ship)) return false;

            // Restart the pacing window. Paid comms requests also go through here, which is
            // intentional: chartering a ship should also stop a free one from arriving right after.
            LastArrivalTick = Find.TickManager.TicksGame;

            return true;
        }

        protected static Faction GetFaction(TraderKindDef trader)
        {
            if (trader.faction == null) return null;
            return (from f in Find.FactionManager.AllFactions where f.def == trader.faction select f).RandomElementWithFallback();
        }

        protected static bool CanSpawn(Map map, TraderKindDef trader)
        {
            if (!trader.orbital)
            {
                return false;
            }
            if (trader.faction == null)
            {
                return true;
            }
            Faction faction = GetFaction(trader);
            if (faction == null)
            {
                return false;
            }

            foreach (Pawn pawn in map.mapPawns.FreeColonists)
            {
                if (pawn.CanTradeWith(faction, trader))
                {
                    return true;
                }
            }

            return false;
        }
    }
}