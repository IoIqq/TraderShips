using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// Adds a standalone "Request trade ship" entry to the comms console faction dialog.
    ///
    /// Why a separate option instead of reusing the vanilla orbital-trader one:
    /// vanilla only shows "Request orbital trader" when the faction's FactionDef has
    /// canRequestOrbitalTrader = true, and in 1.7 Core not a single faction sets it
    /// (only two Odyssey defs do). So for ordinary factions the button never appears and
    /// the player has no way to call a ship on demand.
    ///
    /// We patch FactionDialogMaker.FactionDialogFor(Pawn negotiator, Faction faction) which
    /// returns the root DiaNode, and append our own option to its option list.
    ///
    /// Payment is in silver, not goodwill: the ship is a paid charter, so relations are not
    /// touched. The player then picks which orbital trader kind they want.
    /// </summary>
    public static class CommsShipRequest
    {
        /// <summary>
        /// 两次呼叫之间的最小间隔，由模组设置控制（默认 1 天）。
        /// 与派系的 lastTraderRequestTick 共用，因此和原版"请求商队"两条路径不会互相绕过。
        /// </summary>
        private static int CooldownTicks
        {
            get
            {
                TraderShipsSettings settings = TraderShips.settings;
                float days = settings == null ? 1f : settings.shipRequestCooldownDays;
                if (days < 0f) days = 0f;
                return UnityEngine.Mathf.RoundToInt(days * 60000f);
            }
        }

        /// <summary>
        /// 通讯台入口：把呼叫/购买收进一个子菜单，避免和外交往来选项混在一起。
        ///
        /// 真正的入口是 Building_TradeDispatch（贸易台）。通讯台这条保留是为了
        /// 老存档和"还没造塔"的过渡期，但不再直接铺在派系对话里。
        /// </summary>
        public static void AppendAsSubmenu(DiaNode node, Pawn negotiator, Faction faction)
        {
            try
            {
                if (node == null || faction == null || negotiator == null) return;

                TraderShipsSettings settings = TraderShips.settings;
                if (settings == null || !settings.enableShipRequest) return;

                DiaOption submenu = new DiaOption("TraderShipsCommsSubmenu".Translate());
                submenu.action = delegate
                {
                    DiaNode inner = new DiaNode("TraderShipsCommsSubmenu".Translate());
                    AppendOption(inner, negotiator, faction);
                    inner.options.Add(new DiaOption("GoBack".Translate()) { resolveTree = true });
                    Find.WindowStack.Add(new Dialog_NodeTree(inner, true, true, null));
                };

                node.options.Add(submenu);
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Could not add comms submenu: " + e);
            }
        }

        public static void AppendOption(DiaNode node, Pawn negotiator, Faction faction)
        {
            try
            {
                if (node == null || faction == null || negotiator == null) return;
                Map map = negotiator.Map;
                if (map == null) return;

                TraderShipsSettings settings = TraderShips.settings;
                if (settings == null || !settings.enableShipRequest) return;

                List<TraderKindDef> kinds = RequestableKinds(map, faction);
                if (kinds.Count == 0)
                {
                    // 不静默隐藏：这个派系没有自己的可派遣船型时，明确告诉玩家。
                    // 敌对派系要先给出正确原因 —— 否则会显示成"没有可派遣的飞船类型"，
                    // 让玩家以为是配置问题。
                    DiaOption none = new DiaOption("TraderShipsRequestShipOption".Translate(
                        Mathf.RoundToInt(settings.shipRequestSilverCost).ToString().Named("COST"),
                        faction.def.LabelCap.Named("FACTION")).CapitalizeFirst());

                    if (faction.HostileTo(Faction.OfPlayer))
                    {
                        none.Disable("TraderShipsRequestShipHostile".Translate().CapitalizeFirst());
                    }
                    else
                    {
                        none.Disable("TraderShipsRequestShipNoKinds".Translate().CapitalizeFirst());
                    }

                    node.options.Add(none);

                    ShipPurchase.AddCommsOption(node, negotiator, faction);
                    return;
                }

                int cost = PriceFor(faction, Mathf.RoundToInt(settings.shipRequestSilverCost));
                if (cost < 0) cost = 0;

                // cost.ToString() instead of cost.Named(...): Named() on an int formats with
                // thousands separators ("1,000"), which looks alien next to vanilla's
                // "cost: 30 goodwill" wording.
                string label = "TraderShipsRequestShipOption".Translate(
                    cost.ToString().Named("COST"),
                    faction.def.LabelCap.Named("FACTION")).CapitalizeFirst();

                DiaOption option = new DiaOption(label.ToString());

                int last = faction.lastTraderRequestTick;
                int now = Find.TickManager.TicksGame;
                int remaining = CooldownTicks - (now - last);
                if (faction.HostileTo(Faction.OfPlayer))
                {
                    option.Disable("TraderShipsRequestShipHostile".Translate().CapitalizeFirst());
                }
                else if (settings.shipRequestRequireAlly && faction.PlayerRelationKind != FactionRelationKind.Ally)
                {
                    // Show the entry greyed out instead of hiding it: a silently missing option
                    // makes it look like the feature is broken.
                    option.Disable("TraderShipsRequestShipAllyOnly".Translate().CapitalizeFirst());
                }
                else if (remaining > 0)
                {
                    option.Disable("TraderShipsRequestShipCooldown".Translate(
                        GenDate.ToStringTicksToPeriod(remaining, false, true)).CapitalizeFirst());
                }
                else if (settings.maxLandedShips > 0
                    && IncidentWorkerTraderShip.CountLandedShips(map) >= settings.maxLandedShips)
                {
                    option.Disable("TraderShipsRequestShipAtCapacity".Translate(
                        settings.maxLandedShips.ToString().Named("MAX")).CapitalizeFirst());
                }
                else if (AvailableSilver(map) < cost)
                {
                    option.Disable("TraderShipsRequestShipLowSilver".Translate(
                        cost.ToString().Named("COST")).CapitalizeFirst());
                }
                else
                {
                    option.action = delegate
                    {
                        ShowKindPicker(map, faction, negotiator, kinds, cost);
                    };
                }

                node.options.Add(option);
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Could not add ship request option: " + e);
            }

            // 买船也走通讯台：需要有该派系的船停靠才有货可卖。
            ShipPurchase.AddCommsOption(node, negotiator, faction);
        }

        /// <summary>供买船流程调用，完成交易后关闭整个对话链。</summary>
        internal static void CloseCommsPublic()
        {
            try
            {
                while (Find.WindowStack.TryRemoveAssignableFromType(typeof(Dialog_NodeTree), true))
                {
                }
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Could not close the comms dialog: " + e);
            }
        }

        private static void ShowKindPicker(Map map, Faction faction, Pawn negotiator,
            List<TraderKindDef> kinds, int cost)
        {
            DiaNode picker = new DiaNode("TraderShipsRequestShipPickKind".Translate(
                faction.Name.Named("FACTION")).CapitalizeFirst());
            List<DiaOption> built = new List<DiaOption>();

            foreach (TraderKindDef kind in kinds)
            {
                TraderKindDef local = kind;
                DiaOption o = new DiaOption(DescribeKind(local, cost));
                o.action = delegate
                {
                    RequestShip(map, faction, negotiator, local, cost);
                };
                built.Add(o);
            }

            picker.options.AddRange(built);
            DiaOption back = new DiaOption("GoBack".Translate());
            back.resolveTree = true;
            picker.options.Add(back);

            Find.WindowStack.Add(new Dialog_NodeTree(picker, true, true, null));
        }

        /// <summary>
        /// Label plus the type's own category, which is what actually distinguishes the
        /// vanilla orbital kinds from one another (Bulk goods / Combat supplier / Exotic / Pirate).
        /// </summary>
        private static string DescribeKind(TraderKindDef kind, int cost)
        {
            string label = kind.LabelCap.ToString();
            string price = "(" + cost + " " + "TraderShipsSilver".Translate() + ")";
            // kind.category is a plain string (e.g. "Slaver"), not a Def: no LabelCap here.
            return kind.category.NullOrEmpty()
                ? label + "  " + price
                : label + "  " + price + "  -  " + kind.category.CapitalizeFirst();
        }

        /// <summary>
        /// Every TraderKindDef that could plausibly supply a ship: orbital kinds, plus any
        /// non-orbital kind flagged requestable (a mod may add those). Faction-bound kinds
        /// are included only for the faction we are talking to.
        /// </summary>
        public static List<TraderKindDef> RequestableKinds(Map map, Faction faction)
        {
            List<TraderKindDef> result = new List<TraderKindDef>();
            if (faction?.def == null) return result;

            HashSet<TraderKindDef> seen = new HashSet<TraderKindDef>();

            // 1) 派系标签（FactionShipExtension）。这是主要途径：
            //    原版 Core 里没有任何 FactionDef 填过 orbitalTraderKinds，
            //    而四个 Orbital_* 船型在装 Odyssey 后全绑在 TradersGuild 上，
            //    所以必须由标签系统来决定"这个派系能派哪些船"。
            FactionShipExtension tag = faction.def.GetModExtension<FactionShipExtension>();

            if (tag != null)
            {
                if (tag.noTradeShips) return result;

                if (tag.shipKinds != null)
                {
                    foreach (TraderKindDef kind in tag.shipKinds)
                    {
                        if (kind == null) continue;
                        if (seen.Add(kind)) result.Add(kind);
                    }
                }
            }

            // 2) 没有标签时走兜底：
            //    - 先用派系自己声明的 orbitalTraderKinds（模组可能填）
            //    - 再用绑定到该派系的类型
            //    - 最后用全部轨道商类型（科技等级够才行）
            //
            //    注意最后这步不按 kind.faction 过滤：装了 Odyssey 后它们都绑在
            //    TradersGuild 上，过滤就等于所有派系都没船。飞船归属由
            //    CompShip.GetFaction 的 forcedFaction 保证，不依赖类型绑定。
            if (result.Count == 0 && faction.def.orbitalTraderKinds != null)
            {
                foreach (TraderKindDef kind in faction.def.orbitalTraderKinds)
                {
                    if (kind == null) continue;
                    if (seen.Add(kind)) result.Add(kind);
                }
            }

            if (result.Count == 0)
            {
                foreach (TraderKindDef kind in DefDatabase<TraderKindDef>.AllDefs)
                {
                    if (kind == null) continue;
                    if (kind.faction != faction.def) continue;
                    if (!kind.orbital && !kind.requestable) continue;
                    if (seen.Add(kind)) result.Add(kind);
                }
            }

            // 3) 兜底按科技阶段分档（原版 TechLevel 枚举，等价于石器/中世纪/电气/太空）。
            if (result.Count == 0)
            {
                foreach (TraderKindDef kind in DefaultKindsForTechLevel(faction.def.techLevel))
                {
                    if (kind == null) continue;
                    if (seen.Add(kind)) result.Add(kind);
                }
            }

            // 最后过滤掉当前无法真正生成飞船的（没小人有交易能力等）。
            result.RemoveAll(k => !ShipCanSpawn(map, k));

            return result
                .OrderByDescending(k => k.commonality)
                .ToList();
        }

        /// <summary>工业时代能派的基础贸易船型。</summary>
        private static readonly string[] IndustrialKinds =
        {
            "Orbital_BulkGoods",
            "Orbital_Exotic",
        };

        /// <summary>
        /// 按派系科技阶段决定它能派哪些贸易飞船。
        ///
        /// 原版 FactionDef 强制要求填 techLevel（不填会报 "has no tech level"），
        /// 所以这里可以直接依赖它。派系本身不做研究，研究是玩家的系统，
        /// 因此"科技阶段"是唯一可信的分档依据。
        ///
        ///   石器/部落/中世纪 → 没有贸易飞船
        ///   工业时代         → 批发商、异域商
        ///   太空及以上       → 全部轨道商类型（含武器商、海盗商）
        /// </summary>
        public static List<TraderKindDef> DefaultKindsForTechLevel(TechLevel techLevel)
        {
            List<TraderKindDef> result = new List<TraderKindDef>();

            if (techLevel < TechLevel.Industrial) return result;

            if (techLevel <= TechLevel.Industrial)
            {
                foreach (string defName in IndustrialKinds)
                {
                    TraderKindDef kind = DefDatabase<TraderKindDef>.GetNamedSilentFail(defName);
                    if (kind != null) result.Add(kind);
                }
                return result;
            }

            // Spacer / Ultra / Archotech：全部轨道商类型。
            foreach (TraderKindDef kind in DefDatabase<TraderKindDef>.AllDefs)
            {
                if (kind == null) continue;
                if (!kind.orbital && !kind.requestable) continue;
                result.Add(kind);
            }

            return result;
        }

        /// <summary>
        /// 按好感度调整价格：+100 好感约 0.6 倍，0 好感 1.0 倍，-100 好感约 1.4 倍。
        /// 呼叫飞船和购买飞船共用同一套公式。
        /// </summary>
        public static int PriceFor(Faction faction, int basePrice)
        {
            if (faction == null) return basePrice;

            int goodwill = Mathf.Clamp(faction.PlayerGoodwill, -100, 100);
            float multiplier = 1f - goodwill * 0.004f;

            int price = Mathf.RoundToInt(basePrice * multiplier);
            return Mathf.Max(0, price);
        }

        /// <summary>Mirrors IncidentWorkerTraderShip.CanSpawn, which is protected static.</summary>
        private static bool ShipCanSpawn(Map map, TraderKindDef kind)
        {
            if (!kind.orbital) return false;
            if (kind.faction == null) return true;

            Faction owner = Find.FactionManager.AllFactions
                .FirstOrDefault(f => f.def == kind.faction);
            if (owner == null) return false;

            foreach (Pawn pawn in map.mapPawns.FreeColonists)
            {
                if (pawn.CanTradeWith(owner, kind)) return true;
            }
            return false;
        }

        /// <summary>
        /// Silver the colony can actually hand over.
        ///
        /// Deliberately NOT TradeUtility.ColonyHasEnoughSilver: that only counts silver sitting in
        /// the tradeable cells of a POWERED orbital trade beacon (TradeUtility.cs:84), so a colony
        /// with thousands of silver and no beacon was told it could not afford anything. The
        /// affordability check and the payment must use the same set of stacks, or the button lies;
        /// both go through this method.
        /// </summary>
        internal static List<Thing> PayableSilver(Map map)
        {
            List<Thing> result = new List<Thing>();
            if (map == null) return result;

            // 递归搜索：listerThings 只含"已生成"的物品，银放进储存容器就查不到，
            // 于是界面上有 8 万银却被判成"银不够"。GetAllThingsRecursively 会把
            // 容器内的东西一并取出来。
            List<Thing> all = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(
                map, ThingRequest.ForDef(ThingDefOf.Silver), all,
                allowUnreal: true, passCheck: null, alsoGetSpawnedThings: true);

            foreach (Thing t in all)
            {
                if (t == null || t.Destroyed) continue;
                // Respect the player's "don't touch this" forbids.
                if (t.IsForbidden(Faction.OfPlayer)) continue;
                result.Add(t);
            }

            return result;
        }

        /// <summary>
        /// 玩家可见的银数。
        ///
        /// listerThings 只含"已生成"的物品，银放进了储存容器（inner container）时会漏掉，
        /// 于是界面上有 8 万银却被判成"银不够"。resourceCounter 是左上角那个数字，
        /// 两者取大，避免这种误判。
        /// </summary>
        /// <summary>
        /// 玩家"真正能付得出"的银两。
        ///
        /// 这里**不能**再退到 map.resourceCounter.Silver：
        /// HUD 的计数包含玩家已经禁用（forbid）的银，也包含容器里那些
        /// PaySilver 实际不一定能摸到的银。旧实现取两者的最大值，
        /// 于是出现"界面判定 8000、实扣 3000、照样放行"。
        ///
        /// PayableSilver 本来就会递归容器，所以口径统一到它即可。
        /// </summary>
        internal static int AvailableSilver(Map map)
        {
            if (map == null) return 0;
            return CountPayableSilver(map);
        }

        internal static int CountPayableSilver(Map map)
        {
            int sum = 0;
            foreach (Thing t in PayableSilver(map))
            {
                if (t.stackCount > 0) sum += t.stackCount;
            }
            return sum;
        }

        /// <summary>
        /// Removes exactly <paramref name="cost"/> silver from the map.
        /// Caller must have verified CountPayableSilver first.
        /// </summary>
        /// <summary>Public entry point so other systems (ship damage compensation) reuse the same
        /// payment rules instead of inventing their own.</summary>
        /// <summary>
        /// 扣银，返回**实际扣掉的数量**。
        ///
        /// 调用方必须用返回值判断是否足额 —— 以前这里是 void，
        /// 只靠事前 CountPayableSilver 判断"够不够"，一旦两次读取之间
        /// 银两发生了变化（玩家在对话框开着时把银搬走、别的模组消耗银），
        /// 就会变成"判定通过 → 只扣到一部分 → 照样放行"，白送一艘船。
        /// </summary>
        internal static int PaySilverFrom(Map map, int cost)
        {
            return PaySilver(map, cost);
        }

        /// <summary>
        /// 把银子退回殖民地（落在殖民地中心附近的空地）。
        ///
        /// 只用于"钱已经扣了但事情没办成"的补偿路径。
        /// </summary>
        internal static void RefundSilverTo(Map map, int amount)
        {
            if (map == null || amount <= 0) return;

            int remaining = amount;
            while (remaining > 0)
            {
                int chunk = Mathf.Min(remaining, 500);
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = chunk;

                if (!GenPlace.TryPlaceThing(silver, map.Center, map, ThingPlaceMode.Near))
                {
                    silver.Destroy(DestroyMode.Vanish);
                    Log.Warning("[TraderShips] Could not refund " + chunk + " silver - no space.");
                }

                remaining -= chunk;
            }

            map.resourceCounter?.UpdateResourceCounts();
        }

        /// <summary>扣银，返回实际扣掉的数量（= cost - 剩余未扣足部分）。</summary>
        private static int PaySilver(Map map, int cost)
        {
            if (cost <= 0) return 0;

            int remaining = cost;

            // Iterate over a copy: Destroy() mutates listerThings during the loop.
            foreach (Thing stack in PayableSilver(map))
            {
                if (remaining <= 0) break;
                if (stack == null || stack.Destroyed || stack.stackCount <= 0) continue;

                if (stack.stackCount <= remaining)
                {
                    remaining -= stack.stackCount;
                    stack.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    Thing taken = stack.SplitOff(remaining);
                    remaining = 0;
                    if (taken != null) taken.Destroy(DestroyMode.Vanish);
                }
            }

            int paid = cost - remaining;

            if (remaining > 0)
            {
                Log.Warning("[TraderShips] Silver payment short by " + remaining
                    + " (paid " + paid + " of " + cost + ").");
            }

            return paid;
        }

        /// <summary>
        /// Closes the whole conversation: the kind picker we opened, plus the faction dialogue
        /// behind it.
        ///
        /// The faction window is a Dialog_Negotiation (Faction.cs:1221 TryOpenComms), which derives
        /// from Dialog_NodeTree. WindowStack.TryRemove(Type) matches with "==", so it would only
        /// have removed our plain Dialog_NodeTree and left the negotiation window on screen —
        /// TryRemoveAssignableFromType is required to catch the subclass.
        /// </summary>
        private static void CloseCommsWindows()
        {
            try
            {
                while (Find.WindowStack.TryRemoveAssignableFromType(typeof(Dialog_NodeTree), true))
                {
                    // One window per call; loop until every dialogue in the chain is gone.
                }
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Could not close the comms dialog: " + e);
            }
        }

        /// <summary>
        /// 供贸易台界面直接下单用。返回 false 表示没叫成（已弹出原因），
        /// 调用方据此决定要不要关窗口。
        /// </summary>
        internal static bool RequestShipPublic(Map map, Faction faction, Pawn negotiator,
            TraderKindDef kind, int cost)
        {
            return RequestShip(map, faction, negotiator, kind, cost);
        }

        private static bool RequestShip(Map map, Faction faction, Pawn negotiator,
            TraderKindDef kind, int cost)
        {
            try
            {
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(
                    Globals.TraderShipsArrival.category, map);
                parms.faction = faction;
                parms.target = map;
                parms.forced = true;

                IncidentWorkerTraderShip worker = Globals.TraderShipsArrival.Worker as IncidentWorkerTraderShip;
                if (worker == null)
                {
                    Log.Error("[TraderShips] TraderShipsArrival has no IncidentWorkerTraderShip worker.");
                    return false;
                }

                // Spawn first, charge only on success, so a failed call is never billed.
                bool ok = worker.TryExecuteWorkerPub(parms, kind);
                if (!ok)
                {
                    Messages.Message("TraderShipsRequestShipFailed".Translate(), MessageTypeDefOf.NegativeEvent);
                    return false;
                }

                // 扣款必须验足额。
                //
                // 船已经落下来了才发现银不够，没法"不扣就取消"——
                // 所以这里把差额当作白送的损失记一笔，并明确告诉玩家实扣了多少，
                // 而不是像以前那样静默放行。
                int paid = PaySilver(map, cost);
                if (paid < cost)
                {
                    Messages.Message("TraderShipsRequestShipUnderpaid".Translate(
                        paid.ToString().Named("PAID"),
                        cost.ToString().Named("COST")),
                        MessageTypeDefOf.NegativeEvent);
                }
                faction.lastTraderRequestTick = Find.TickManager.TicksGame;

                // The resource readout is driven by ResourceCounter, which refreshes on its own
                // schedule; without this the silver number in the corner appeared to drop a
                // moment after the purchase.
                map.resourceCounter.UpdateResourceCounts();

                Messages.Message("TraderShipsRequestShipArrived".Translate(
                    cost.ToString().Named("COST"), kind.LabelCap.Named("KIND")),
                    MessageTypeDefOf.PositiveEvent);

                // Close the comms conversation once the deal is done: leaving the player parked in
                // a dialog after the ship is already inbound reads as "nothing happened".
                CloseCommsWindows();
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Ship request failed: " + e);
                return false;
            }
        }
    }

    /// <summary>
    /// Postfix target for FactionDialogMaker.FactionDialogFor(Pawn negotiator, Faction faction).
    ///
    /// Deliberately a plain static class with no [HarmonyPatch] attribute: attribute-driven
    /// patching would make HarmonyLib a hard compile-time (and therefore load-time) dependency.
    /// See TraderShips.ApplyHarmonyPatches, which binds this reflectively so the mod still
    /// loads when Harmony is absent.
    ///
    /// Vanilla builds the root DiaNode and returns it, so appending to node.options leaves the
    /// existing options (and anything other mods added to them) untouched.
    /// </summary>
    internal static class CommsShipRequestPatches
    {
        // Signature must mirror the original exactly:
        //   public static DiaNode FactionDialogFor(Pawn negotiator, Faction faction)
        // (FactionDialogMaker.cs:16) — it takes NO Map; the map comes from negotiator.Map.
        public static void FactionDialogFor_Postfix(DiaNode __result, Pawn negotiator, Faction faction)
        {
            // 通讯台不再是主入口（主入口是贸易台），这里收成子菜单。
            CommsShipRequest.AppendAsSubmenu(__result, negotiator, faction);
        }
    }
}