using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 通过通讯台购买一艘属于玩家的运输飞船。
    ///
    /// 买到的不是 Odyssey 的引力船（那需要 GravEngine + 连通的 Substructure 地形），
    /// 而是走原版 Core 穿梭机框架的运输船（TraderShipsPlayerShip）：
    /// 装载、发射、飞行、降落、卸载全部由 CompShuttle / CompTransporter 接管。
    /// 依赖链已核实全部在 Core，不依赖任何 DLC。
    ///
    /// 入口在通讯台：需要有该派系的飞船停靠在当前地图，才有船可卖。
    /// </summary>
    public static class ShipPurchase
    {
        /// <summary>默认售价。</summary>
        public const int DefaultPrice = 8000;

        private static ThingDef playerShipDefInt;

        private static ThingDef PlayerShipDef
        {
            get
            {
                if (playerShipDefInt == null)
                    playerShipDefInt = DefDatabase<ThingDef>.GetNamedSilentFail("TraderShipsPlayerShip");
                return playerShipDefInt;
            }
        }

        /// <summary>当前地图上属于该派系、且正在停靠的商船。</summary>
        public static Thing FindSeller(Map map, Faction faction)
        {
            if (map == null || faction == null) return null;

            ThingDef shipDef = DefDatabase<ThingDef>.GetNamedSilentFail("TraderShipsShip");
            if (shipDef == null) return null;

            foreach (Thing t in map.listerThings.ThingsOfDef(shipDef))
            {
                CompShip comp = t.TryGetComp<CompShip>();
                if (comp?.tradeShip == null) continue;
                if (comp.tradeShip.Faction != faction) continue;
                return t;
            }

            return null;
        }

        /// <summary>已经拥有一艘就不要再卖第二艘，避免地上堆一片船。</summary>
        public static bool OwnerShipExists()
        {
            ThingDef def = PlayerShipDef;
            if (def == null) return false;

            foreach (Map map in Find.Maps)
            {
                if (map.listerThings.ThingsOfDef(def).Count > 0) return true;
            }
            return false;
        }

        /// <summary>往通讯台对话里追加「购买飞船」选项。</summary>
        public static void AddCommsOption(DiaNode node, Pawn negotiator, Faction faction)
        {
            try
            {
                if (node == null || faction == null || negotiator == null) return;

                Map map = negotiator.Map;
                if (map == null) return;
                if (PlayerShipDef == null) return;

                // 与呼叫飞船共用同一套好感定价公式。
                int price = CommsShipRequest.PriceFor(faction, DefaultPrice);
                DiaOption option = new DiaOption("TraderShipsBuyShip".Translate(
                    price.ToString().Named("PRICE")));

                Thing seller = FindSeller(map, faction);

                if (OwnerShipExists())
                {
                    option.Disable("TraderShipsBuyShipAlreadyOwn".Translate().CapitalizeFirst());
                }
                else if (seller == null)
                {
                    // 没有该派系的船停靠，就没有船可卖。
                    option.Disable("TraderShipsBuyShipNoSeller".Translate().CapitalizeFirst());
                }
                else if (CommsShipRequest.AvailableSilver(map) < price)
                {
                    option.Disable("TraderShipsShipNotEnoughSilver".Translate(
                        price.ToString().Named("COST")).CapitalizeFirst());
                }
                else
                {
                    Thing localSeller = seller;
                    option.action = delegate { BuyShip(localSeller, price); };
                }

                node.options.Add(option);
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Could not add ship purchase option: " + e);
            }
        }

        /// <summary>供贸易台界面调用。</summary>
        internal static void BuyShipPublic(Thing seller, int price)
        {
            BuyShip(seller, price);
        }

        private static void BuyShip(Thing seller, int price)
        {
            try
            {
                Map map = seller?.Map;
                if (map == null) return;

                ThingDef def = PlayerShipDef;
                if (def == null)
                {
                    Log.Error("[TraderShips] TraderShipsPlayerShip def missing; cannot sell a ship.");
                    return;
                }

                if (CommsShipRequest.AvailableSilver(map) < price)
                {
                    Messages.Message("TraderShipsShipNotEnoughSilver".Translate(price.ToString().Named("COST")),
                        MessageTypeDefOf.RejectInput, false);
                    return;
                }

                IntVec3 spot = FindBuyerSpot(map, seller, def);
                if (!spot.IsValid)
                {
                    Messages.Message("TraderShipsBuyShipNoRoom".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                // 扣钱必须验足额。
                //
                // 以前只判 AvailableSilver >= price 就扣、就生成，而 AvailableSilver
                // 取的是"实物银与 HUD 计数中的较大值" —— 银装在运输舱里时
                // HUD 显示 8000、实物只有 3000，结果扣了 3000 就把船送出去了。
                int paid = CommsShipRequest.PaySilverFrom(map, price);
                map.resourceCounter.UpdateResourceCounts();

                if (paid < price)
                {
                    // 差额回滚：把已扣的部分原样退回，不发货。
                    CommsShipRequest.RefundSilverTo(map, paid);
                    map.resourceCounter.UpdateResourceCounts();

                    Messages.Message("TraderShipsShipNotEnoughSilver".Translate(
                        price.ToString().Named("COST")), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                Thing playerShip = null;
                try
                {
                    playerShip = ThingMaker.MakeThing(def);
                    GenSpawn.Spawn(playerShip, spot, map, Rot4.North);
                }
                catch (Exception ex)
                {
                    // 生成失败 → 全额退款，绝不"扣了钱什么都没给"。
                    Log.Warning("[TraderShips] Ship spawn failed, refunding " + price + ": " + ex.Message);
                    if (playerShip != null && playerShip.Spawned) playerShip.Destroy(DestroyMode.Vanish);
                    CommsShipRequest.RefundSilverTo(map, paid);
                    map.resourceCounter.UpdateResourceCounts();
                    Messages.Message("TraderShipsBuyShipFailed".Translate(), MessageTypeDefOf.NegativeEvent);
                    return;
                }

                Messages.Message("TraderShipsBuyShipDone".Translate(price.ToString().Named("PRICE")),
                    playerShip, MessageTypeDefOf.PositiveEvent, true);

                // 卖家交接后起飞离开。
                CompShip comp = seller.TryGetComp<CompShip>();
                comp?.SendAwayPublic();

                // 交易完成，关掉整个通讯对话链。
                CommsShipRequest.CloseCommsPublic();
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Buying a ship failed: " + e);
            }
        }

        /// <summary>优先着陆区，其次飞船附近的空地。</summary>
        private static IntVec3 FindBuyerSpot(Map map, Thing near, ThingDef def)
        {
            IntVec2 size = def.size;

            Area_LandingZone zone = map.areaManager.LandingZone();
            if (zone != null && zone.TryFindShipLandingArea(size, out IntVec3 center, out Thing blocker))
            {
                return center;
            }

            for (int i = 0; i < 40; i++)
            {
                IntVec3 candidate = CellFinder.RandomClosewalkCellNear(near.Position, map, 12, null);
                if (candidate.IsValid && CanPlaceShipAt(map, candidate, size))
                {
                    return candidate;
                }
            }

            return IntVec3.Invalid;
        }

        private static bool CanPlaceShipAt(Map map, IntVec3 center, IntVec2 size)
        {
            CellRect rect = GenAdj.OccupiedRect(center, Rot4.North, size);
            if (!rect.InBounds(map)) return false;

            foreach (IntVec3 c in rect)
            {
                if (!c.Walkable(map)) return false;
                if (c.GetThingList(map).Any(t => t.def.category == ThingCategory.Building)) return false;
            }
            return true;
        }
    }
}