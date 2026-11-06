using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 预订货物的执行端：到期 → 派船 → 落地 → 塞货。
    ///
    /// 为什么放在 GameComponent 而不是订单自己的 tick：
    ///   订单只是数据，船是地图上的东西。只有 GameComponent 能拿到"当前玩家殖民地"，
    ///   而订单可能在玩家换基地、殖民地全灭的那段时间里到期 —— 那时没有可落地的地图，
    ///   订单必须原地等着，不能丢。
    ///
    /// 为什么 250 tick 才查一次：
    ///   到期精度到"天"就够（60000 tick 一天），每 tick 遍历订单纯属浪费；
    ///   250 tick 是原版 rare tick 的量级，一轮检查的误差最多 4 秒。
    /// </summary>
    public class GameComponent_ShipOrders : GameComponent
    {
        /// <summary>检查间隔（tick）。</summary>
        private const int CheckInterval = 250;

        /// <summary>落不了地时顺延多久：1 天。和"到期"同一套时间单位，玩家看到的就是"明天再来"。</summary>
        private const int PostponeTicks = 60000;

        private List<ShipOrder> orders = new List<ShipOrder>();

        public GameComponent_ShipOrders(Game game)
        {
        }

        /// <summary>当前存档的订单组件。没有存档（主菜单）时为 null。</summary>
        public static GameComponent_ShipOrders Instance
        {
            get
            {
                if (Verse.Current.Game == null) return null;
                return Verse.Current.Game.GetComponent<GameComponent_ShipOrders>();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref orders, "traderShipsGoodsOrders", LookMode.Deep);

            // 老存档没有这个字段，读出来是 null；不补上的话下一次 Tick 就是空引用。
            if (Scribe.mode == LoadSaveMode.PostLoadInit && orders == null)
            {
                orders = new List<ShipOrder>();
            }
        }

        /// <summary>下订单成功后由界面调用。</summary>
        public static void PlaceOrders(List<ShipOrder> placed)
        {
            GameComponent_ShipOrders comp = Instance;
            if (comp == null || placed == null || placed.Count == 0) return;

            comp.orders.AddRange(placed);
            comp.InvalidateSchedule();
        }

        /// <summary>最早一单的到期 tick；-1 表示需要重算。</summary>
        private int nextDueTick = -1;

        public override void GameComponentTick()
        {
            if (orders == null || orders.Count == 0) return;
            if (Find.TickManager.TicksGame % CheckInterval != 0) return;

            // 没有快到期的单就不必每 250 tick 全表扫一遍。
            if (nextDueTick < 0) nextDueTick = ComputeNextDueTick();
            if (nextDueTick > 0 && Find.TickManager.TicksGame < nextDueTick) return;

            DeliverDueOrders();
            nextDueTick = ComputeNextDueTick();
        }

        private int ComputeNextDueTick()
        {
            if (orders == null || orders.Count == 0) return -1;

            int best = int.MaxValue;
            for (int i = 0; i < orders.Count; i++)
            {
                ShipOrder o = orders[i];
                if (o == null) continue;
                if (o.dueTick < best) best = o.dueTick;
            }

            return best == int.MaxValue ? -1 : best;
        }

        /// <summary>订单表变动后必须重算下次到期时间。</summary>
        private void InvalidateSchedule()
        {
            nextDueTick = -1;
        }

        /// <summary>
        /// 本轮处理所有到期订单。
        ///
        /// 分两段：先筛出到期且还有救的订单并按"派系 + 船型"分组，
        /// 再逐组派船。分组是为了不让玩家为同一批货收一打船 ——
        /// 一次订五样同主题的货，来一艘船就够了。
        /// </summary>
        private void DeliverDueOrders()
        {
            Map map = Find.AnyPlayerHomeMap;
            if (map == null) return;

            int now = Find.TickManager.TicksGame;
            Dictionary<string, List<ShipOrder>> groups = null;

            for (int i = orders.Count - 1; i >= 0; i--)
            {
                ShipOrder order = orders[i];
                if (order == null)
                {
                    orders.RemoveAt(i);
                    InvalidateSchedule();
                    continue;
                }

                // 数量被外部改成 0/负数的坏数据：塞不进任何货，也不能让它永远顺延。
                if (order.count <= 0)
                {
                    orders.RemoveAt(i);
                    continue;
                }

                if (order.dueTick > now) continue;

                Faction faction = FactionOf(order.factionDefName);
                ThingDef thing = DefDatabase<ThingDef>.GetNamedSilentFail(order.thingDefName);

                // 派系没了、翻脸了、或者卖这件货的模组被拆了 —— 这单永远做不成。
                // 与其让它一天天顺延到存档烂掉，不如退钱收尾，并把原因说清楚。
                if (faction == null || faction.defeated || faction.HostileTo(Faction.OfPlayer) || thing == null)
                {
                    RefundAndDrop(order, map, i);
                    continue;
                }

                TraderKindDef kind = KindFor(faction, thing);
                if (kind == null)
                {
                    RefundAndDrop(order, map, i);
                    continue;
                }

                if (groups == null) groups = new Dictionary<string, List<ShipOrder>>();
                string key = faction.def.defName + "|" + kind.defName;

                List<ShipOrder> bucket;
                if (!groups.TryGetValue(key, out bucket))
                {
                    bucket = new List<ShipOrder>();
                    groups[key] = bucket;
                }

                bucket.Add(order);
            }

            if (groups == null) return;

            // 本轮已经派出去的船也要算进停靠位：天降动画期间船还没生成，
            // CountLandedShips 看不到它们，不自己数就会出现"上限 2 条，一次落 5 条"。
            int pending = 0;

            foreach (List<ShipOrder> bucket in groups.Values)
            {
                if (DeliverGroup(map, bucket, now, pending))
                {
                    // 一次 tick 只派一艘。
                    //
                    // 单船找落点最多要做 20 × 200 次落地判定（FindCloseLandingSpot →
                    // FindSafeLandingSpot），同 tick 连派三艘就是上万次，够让画面卡一下。
                    // 剩下的组顺延到下一次轮询，玩家几乎察觉不到。
                    break;
                }
            }
        }

        /// <summary>
        /// 为一组同派系同船型的订单派一艘船，装好后落地。
        /// 返回 true 表示船已经出发（算一个停靠位），false 表示顺延或放弃。
        /// </summary>
        private bool DeliverGroup(Map map, List<ShipOrder> bucket, int now, int pending)
        {
            Faction faction = FactionOf(bucket[0].factionDefName);
            ThingDef firstThing = DefDatabase<ThingDef>.GetNamedSilentFail(bucket[0].thingDefName);
            if (faction == null || firstThing == null) return false;

            TraderKindDef kind = KindFor(faction, firstThing);
            if (kind == null) return false;

            // 停靠位上限：玩家自己设的上限不该被"已经付过钱的订单"绕过，
            // 否则预定几次就能把地图堆满。顺延而不是取消 —— 定金还在货主手里。
            TraderShipsSettings settings = TraderShips.settings;
            if (settings != null && settings.maxLandedShips > 0
                && IncidentWorkerTraderShip.CountLandedShips(map) + pending >= settings.maxLandedShips)
            {
                Postpone(bucket, now, "landing pads occupied");
                return false;
            }

            Thing ship = IncidentWorkerTraderShip.MakeTraderShip(map, faction, kind);
            if (ship == null)
            {
                Postpone(bucket, now, "no trader kind could be generated for " + faction.def.defName);
                return false;
            }

            CompShip comp = ship.TryGetComp<CompShip>();
            if (comp == null || comp.tradeShip == null)
            {
                ship.Destroy(DestroyMode.Vanish);
                Postpone(bucket, now, "ship has no CompShip/LandedShip");
                return false;
            }

            // 先塞货再落地：落地走的是天降动画（skyfaller），船本身要等动画结束才生成，
            // 那之后再往货舱里放东西，玩家开交易窗会看到"船上凭空多出一批货"。
            ThingOwner hold = comp.tradeShip.GetDirectlyHeldThings();

            bool stocked = true;

            foreach (ShipOrder order in bucket)
            {
                ThingDef thing = DefDatabase<ThingDef>.GetNamedSilentFail(order.thingDefName);
                if (thing == null || !StockInto(hold, thing, order.count)) stocked = false;
            }

            // 货装不进货舱就别落了：落下去也是玩家打开交易窗才发现少货，
            // 那时订单已经算完成，没法补救。
            if (!stocked)
            {
                ship.Destroy(DestroyMode.Vanish);
                Postpone(bucket, now, "the ship's hold could not be filled");
                return false;
            }

            bool landed = IncidentWorkerTraderShip.LandShip(map, ship);

            // LandShip 在放不下时会自己把船销毁掉并返回 false。
            // 没落地就等于没交付，这一组整体顺延，货等下次重新生成。
            if (!landed || ship.Destroyed)
            {
                Postpone(bucket, now, "skyfaller could not be placed");
                return false;
            }

            foreach (ShipOrder order in bucket)
            {
                ThingDef thing = DefDatabase<ThingDef>.GetNamedSilentFail(order.thingDefName);
                string label = thing != null ? thing.LabelCap.ToString() : order.thingDefName;

                Messages.Message("TraderShipsOrderArrived".Translate(
                    label.Named("THING"),
                    order.count.ToString().Named("COUNT"),
                    faction.Name.Named("FACTION")),
                    MessageTypeDefOf.PositiveEvent, false);
            }

            orders.RemoveAll(o => o == null || bucket.Contains(o));
            InvalidateSchedule();
            return true;
        }

        /// <summary>
        /// 把 count 件货塞进货舱。
        ///
        /// 有同 def 同材质的堆叠就直接加数量：这艘船本来就卖这件货（订单目录就是从它的
        /// 供货单里读出来的），再单独开一堆，玩家会在交易窗里看到两条一模一样的商品。
        /// </summary>
        private static bool StockInto(ThingOwner hold, ThingDef thing, int count)
        {
            if (hold == null || thing == null || count <= 0) return false;

            ThingDef stuff = StuffFor(thing);
            int remaining = count;

            foreach (Thing existing in hold)
            {
                if (remaining <= 0) break;
                if (existing == null || existing.def != thing || existing.Stuff != stuff) continue;

                int room = existing.def.stackLimit - existing.stackCount;
                if (room <= 0) continue;

                int moved = Mathf.Min(room, remaining);
                existing.stackCount += moved;
                remaining -= moved;
            }

            while (remaining > 0)
            {
                int chunk = Mathf.Min(remaining, thing.stackLimit > 0 ? thing.stackLimit : remaining);

                Thing made = ThingMaker.MakeThing(thing, stuff);
                if (made == null) return false;

                made.stackCount = chunk;

                if (!hold.TryAdd(made, false))
                {
                    made.Destroy(DestroyMode.Vanish);
                    return false;
                }

                remaining -= chunk;
            }

            return true;
        }

        /// <summary>
        /// 固定材质，和 StockGenerator_ThingDefs 完全一致。
        ///
        /// 两边必须同源：界面上报的单价是按这个材质算的，船上塞的也必须是同一个材质，
        /// 否则玩家付了"钢铁扎甲"的价钱，收到的可能是"布扎甲"。
        /// </summary>
        internal static ThingDef StuffFor(ThingDef thing)
        {
            if (thing == null || !thing.MadeFromStuff) return null;

            ThingDef stuff = GenStuff.DefaultStuffFor(thing);
            if (stuff == null) GenStuff.TryRandomStuffFor(thing, out stuff, TechLevel.Archotech, null);
            return stuff;
        }

        /// <summary>
        /// 这件货该由哪条船送。
        ///
        /// 优先用"主题船"：物品属于哪个主题，这个派系平时卖它的就是那条船。
        /// 手工写 XML 船型的派系没有主题船，退到"真正在卖这件货的船"，
        /// 最后退到该派系的任意一条船 —— 反正货是我们自己塞进货舱的。
        /// </summary>
        private static TraderKindDef KindFor(Faction faction, ThingDef thing)
        {
            if (faction == null || faction.def == null || thing == null) return null;

            TraderKindDef themed = DefDatabase<TraderKindDef>.GetNamedSilentFail(
                FactionShipGenerator.DefNameFor(faction.def, FactionShipGenerator.ThemeOf(thing)));
            if (themed != null) return themed;

            TraderKindDef any = null;

            foreach (TraderKindDef kind in DefDatabase<TraderKindDef>.AllDefsListForReading)
            {
                if (kind == null || kind.faction != faction.def) continue;

                if (Stocks(kind, thing)) return kind;
                if (any == null) any = kind;
            }

            return any;
        }

        private static bool Stocks(TraderKindDef kind, ThingDef thing)
        {
            foreach (StockGenerator gen in kind.stockGenerators)
            {
                if (gen is StockGenerator_ThingDefs ours && ours.thingDef == thing) return true;
            }

            return false;
        }

        private static Faction FactionOf(string defName)
        {
            if (defName.NullOrEmpty()) return null;

            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                if (faction != null && faction.def.defName == defName) return faction;
            }

            return null;
        }

        /// <summary>趁还没删订单，把它顺延一天再试（退款放不下时用）。</summary>
        private void PostponeCurrentOrder(ShipOrder order)
        {
            if (order == null) return;

            order.dueTick = Find.TickManager.TicksGame + PostponeTicks;
            InvalidateSchedule();
            Log.Warning("[TraderShips] Order refund could not be placed; postponing instead of dropping it.");
        }

        /// <summary>做不成的订单：退定金、走人。钱已经扣过，绝不能静默消失。</summary>
        private void RefundAndDrop(ShipOrder order, Map map, int index)
        {
            bool refunded = CommsShipRequest.RefundSilverTo(map, order.depositPaid);

            // 退款失败（地图中心都放不下银两）时保留订单，下次顺延再试，
            // 不要"钱没退成还把订单删了"——那是玩家真金白银的损失。
            if (!refunded)
            {
                PostponeCurrentOrder(order);
                return;
            }

            Messages.Message("TraderShipsOrderFailed".Translate(
                order.depositPaid.ToString().Named("DEPOSIT")),
                MessageTypeDefOf.NegativeEvent, false);

            orders.RemoveAt(index);
        }

        /// <summary>
        /// 落不了地：订单留到明天，不丢。
        ///
        /// 只在开发者模式下写日志 —— 这条顺延消息对玩家没有可操作价值，
        /// 每天早上刷一条只会被当成噪音。
        /// </summary>
        private void Postpone(List<ShipOrder> bucket, int now, string why)
        {
            foreach (ShipOrder order in bucket)
            {
                order.dueTick = now + PostponeTicks;
            }

            // 顺延之后"最早到期"变了，调度缓存必须作废。
            InvalidateSchedule();

            if (Prefs.DevMode)
            {
                Log.Message("[TraderShips] Ship order postponed by one day: " + why);
            }
        }
    }
}