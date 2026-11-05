using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 贸易台界面：左栏派系、右栏该派系的船型与价格（主从式）。
    ///
    /// 主从式的价值是"一次看全"：左边扫派系，右边直接看到该派系全部船型、
    /// 船上现金和价格，点一下就下单，省掉「选派系 → 选船型」的二次跳转。
    /// </summary>
    public class Dialog_TradeDispatch : Window
    {
        private enum Filter
        {
            Tradable,
            All,
        }

        private readonly Pawn negotiator;

        private Filter filter = Filter.Tradable;
        private Faction selected;
        private string search = "";

        private Vector2 factionScroll;
        private Vector2 kindScroll;

        // ── 绘制缓存 ──────────────────────────────────────────────────
        //
        // DoWindowContents 每帧都会跑。以下三项原本每帧重算，代价都很高：
        //   AvailableSilver  → 递归遍历全图所有容器找银币，每行一次
        //   EstimateSilver   → 每船 40~80 个 StockGenerator，还带反射
        //   RequestableKinds → 新建 HashSet/List + 遍历派系与殖民者
        // 现在统一在窗口级缓存，按 (地图, 派系) 失效。
        private int silverCache = -1;
        private readonly Dictionary<Faction, List<TraderKindDef>> kindsCache
            = new Dictionary<Faction, List<TraderKindDef>>();
        private readonly Dictionary<TraderKindDef, IntRange> cashCache
            = new Dictionary<TraderKindDef, IntRange>();

        private const float LeftWidth = 260f;
        private const float RowHeight = 38f;
        private const float Pad = 8f;

        public override Vector2 InitialSize => new Vector2(880f, 640f);

        public Dialog_TradeDispatch(Pawn negotiator, Building_TradeDispatch building)
        {
            this.negotiator = negotiator;

            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            onlyOneOfTypeAllowed = true;
        }

        // ── 数据 ───────────────────────────────────────────────────────────

        private List<Faction> Factions()
        {
            List<Faction> list = new List<Faction>();

            foreach (Faction faction in Find.FactionManager.AllFactionsVisibleInViewOrder)
            {
                if (faction == null || faction.IsPlayer || faction.defeated || faction.Hidden) continue;
                if (filter == Filter.Tradable && faction.HostileTo(Faction.OfPlayer)) continue;

                // 搜索按派系名过滤（不区分大小写，中文直接包含匹配）。
                if (!search.NullOrEmpty()
                    && faction.Name.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                list.Add(faction);
            }

            // 先算一遍 kinds 数量并缓存，排序比较器里只读 Dictionary，
            // 不再对每个比较元素重复调 RequestableKinds（那是每帧上千次 FirstOrDefault）。
            for (int i = 0; i < list.Count; i++)
            {
                CachedKinds(list[i]);
            }

            return list
                .OrderByDescending(f => CachedKinds(f).Count > 0)
                .ThenByDescending(f => f.PlayerGoodwill)
                .ThenBy(f => f.Name)
                .ToList();
        }

        /// <summary>
        /// 原版 StockGenerator_SingleDef 的 thingDef 字段是 private，只能反射读。
        /// 缓存一次 FieldInfo，避免每次开窗都做反射查找。
        /// </summary>
        private static System.Reflection.FieldInfo singleDefThingField;
        private static bool singleDefFieldResolved;

        /// <summary>
        /// 公开版：给定一个生成器，返回它实际挂着的 ThingDef（可能为 null）。
        /// 自检探针、外部调试都走这里，保证和窗口里显示的是同一条路径。
        /// </summary>
        public static ThingDef StockedDefOf(StockGenerator gen)
        {
            if (gen == null) return null;
            if (gen is StockGenerator_ThingDefs ours) return ours.thingDef;
            if (gen is StockGenerator_SingleDef single) return ThingDefOfSingleDef(single);
            return null;
        }

        private static ThingDef ThingDefOfSingleDef(StockGenerator_SingleDef single)
        {
            if (!singleDefFieldResolved)
            {
                singleDefFieldResolved = true;
                try
                {
                    singleDefThingField = typeof(StockGenerator_SingleDef).GetField(
                        "thingDef",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Public);
                }
                catch (System.Exception e)
                {
                    Log.WarningOnce("[TraderShips] Could not resolve SingleDef.thingDef: " + e.Message, 771234101);
                }
            }

            if (singleDefThingField == null) return null;

            try
            {
                return singleDefThingField.GetValue(single) as ThingDef;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 该船型大概带多少现金 —— 卖货时最关心的数字。
        ///
        /// 同时支持两种生成器：
        ///   自己的 StockGenerator_ThingDefs（字段公开）
        ///   原版的 StockGenerator_SingleDef（字段私有，走反射）
        /// </summary>
        private static IntRange EstimateSilver(TraderKindDef kind)
        {
            IntRange total = new IntRange(0, 0);

            foreach (StockGenerator gen in kind.stockGenerators)
            {
                if (gen == null) continue;

                if (StockedDefOf(gen) != ThingDefOf.Silver) continue;

                total = new IntRange(total.min + gen.countRange.min, total.max + gen.countRange.max);
            }

            return total;
        }

        /// <summary>全图可用银两，窗口内只算一次。</summary>
        private int CachedSilver()
        {
            if (silverCache < 0)
            {
                silverCache = CommsShipRequest.AvailableSilver(negotiator.Map);
            }

            return silverCache;
        }

        /// <summary>某船型的现金区间，按船型缓存（generator 列表运行时不变）。</summary>
        private IntRange CachedCash(TraderKindDef kind)
        {
            if (kind == null) return new IntRange(0, 0);

            IntRange cached;
            if (cashCache.TryGetValue(kind, out cached)) return cached;

            cached = EstimateSilver(kind);
            cashCache[kind] = cached;
            return cached;
        }

        /// <summary>某派系可派的船型，按派系缓存。</summary>
        private List<TraderKindDef> CachedKinds(Faction faction)
        {
            if (faction == null) return new List<TraderKindDef>();

            List<TraderKindDef> cached;
            if (kindsCache.TryGetValue(faction, out cached)) return cached;

            cached = CommsShipRequest.RequestableKinds(negotiator.Map, faction);
            kindsCache[faction] = cached;
            return cached;
        }

        /// <summary>窗口关闭时清空，下次打开重新算。</summary>
        public override void PreClose()
        {
            base.PreClose();
            silverCache = -1;
            kindsCache.Clear();
            cashCache.Clear();
        }

        // ── 绘制 ───────────────────────────────────────────────────────────

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "TraderShipsDispatchRoot".Translate());
            Text.Font = GameFont.Small;

            DrawTopBar(new Rect(0f, 32f, inRect.width, 30f));

            float top = 68f;
            float bodyHeight = inRect.height - top - 38f;

            Rect leftRect = new Rect(0f, top, LeftWidth, bodyHeight);
            Rect rightRect = new Rect(LeftWidth + Pad, top, inRect.width - LeftWidth - Pad, bodyHeight);

            DrawFactionList(leftRect);
            DrawFactionDetail(rightRect);

            if (Widgets.ButtonText(new Rect(inRect.width / 2f - 60f, inRect.height - 32f, 120f, 30f),
                "Close".Translate()))
            {
                Close();
            }
        }

        private void DrawTopBar(Rect rect)
        {
            float w = 110f;

            if (Widgets.ButtonText(new Rect(rect.x, rect.y, w, rect.height),
                (filter == Filter.Tradable ? "★ " : "") + "TraderShipsDispatchFilterTradable".Translate()))
            {
                filter = Filter.Tradable;
                factionScroll = Vector2.zero;
                if (selected != null && selected.HostileTo(Faction.OfPlayer)) selected = null;
            }

            if (Widgets.ButtonText(new Rect(rect.x + w + 6f, rect.y, w, rect.height),
                (filter == Filter.All ? "★ " : "") + "TraderShipsDispatchFilterAll".Translate()))
            {
                filter = Filter.All;
                factionScroll = Vector2.zero;
            }

            // 搜索框（40 多个派系时必备）
            Rect searchRect = new Rect(rect.x + w * 2f + 20f, rect.y, rect.width - w * 2f - 20f, rect.height);
            string before = search;
            search = Widgets.TextField(searchRect, search ?? "");
            if (before != search) factionScroll = Vector2.zero;
        }

        private void DrawFactionList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.6f));

            List<Faction> factions = Factions();
            if (factions.Count == 0)
            {
                Widgets.Label(rect.ContractedBy(12f), "TraderShipsDispatchNoFactions".Translate());
                return;
            }

            Rect view = new Rect(0f, 0f, rect.width - 18f, factions.Count * RowHeight);
            Widgets.BeginScrollView(rect.ContractedBy(2f), ref factionScroll, view);

            float y = 0f;
            foreach (Faction faction in factions)
            {
                DrawFactionRow(new Rect(0f, y, view.width, RowHeight), faction);
                y += RowHeight;
            }

            Widgets.EndScrollView();
        }

        private void DrawFactionRow(Rect rect, Faction faction)
        {
            bool isSelected = selected == faction;
            bool hostile = faction.HostileTo(Faction.OfPlayer);

            if (isSelected) Widgets.DrawHighlightSelected(rect);
            else if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

            if (Widgets.ButtonInvisible(rect))
            {
                selected = faction;
                kindScroll = Vector2.zero;
            }

            Rect iconRect = new Rect(rect.x + 6f, rect.y + 6f, 26f, 26f);
            Texture2D icon = faction.def?.FactionIcon;
            if (icon != null) Widgets.DrawTextureFitted(iconRect, icon, 1f);
            else Widgets.DrawBoxSolid(iconRect, faction.Color);

            Rect nameRect = new Rect(iconRect.xMax + 8f, rect.y + 3f, rect.width - iconRect.width - 90f, 18f);
            Widgets.Label(nameRect, faction.Name.Colorize(hostile ? Color.gray : faction.Color));

            string relation = faction.PlayerRelationKind.GetLabel() + " " + faction.PlayerGoodwill.ToStringWithSign();
            Rect relRect = new Rect(iconRect.xMax + 8f, rect.y + 20f, nameRect.width, 16f);
            GUI.color = hostile ? new Color(1f, 0.5f, 0.5f) : Color.gray;
            Widgets.Label(relRect, relation);
            GUI.color = Color.white;

            int count = CachedKinds(faction).Count;
            string badge = count > 0 ? count.ToString() : "—";
            Rect badgeRect = new Rect(rect.xMax - 32f, rect.y + 10f, 24f, 18f);
            GUI.color = count > 0 ? Color.white : Color.gray;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(badgeRect, badge);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawFactionDetail(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.6f));

            if (selected == null)
            {
                Widgets.Label(rect.ContractedBy(16f), "TraderShipsDispatchPickFaction".Translate());
                return;
            }

            Rect inner = rect.ContractedBy(14f);

            Rect iconRect = new Rect(inner.x, inner.y, 40f, 40f);
            Texture2D icon = selected.def?.FactionIcon;
            if (icon != null) Widgets.DrawTextureFitted(iconRect, icon, 1f);
            else Widgets.DrawBoxSolid(iconRect, selected.Color);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(iconRect.xMax + 10f, inner.y + 2f, inner.width - 60f, 26f), selected.Name);
            Text.Font = GameFont.Small;

            string relation = selected.PlayerRelationKind.GetLabel() + " " + selected.PlayerGoodwill.ToStringWithSign();
            GUI.color = Color.gray;
            Widgets.Label(new Rect(iconRect.xMax + 10f, inner.y + 26f, inner.width - 60f, 18f), relation);
            GUI.color = Color.white;

            Widgets.DrawLineHorizontal(inner.x, inner.y + 50f, inner.width);

            // 买船入口固定在面板底部。
            //
            // 以前它是跟着船型列表的高度往下排（listRect.y + usedHeight），
            // 船型一多（27 条 × 52）就被推到可见区域之外，
            // 正好压在 Close 按钮上。现在先把底部这条留出来，列表再占用剩下的高度。
            const float PurchaseRowHeight = 54f;

            float listHeight = Mathf.Max(80f, inner.height - 58f - PurchaseRowHeight);
            Rect listRect = new Rect(inner.x, inner.y + 58f, inner.width, listHeight);
            Rect purchaseRect = new Rect(inner.x, inner.y + inner.height - PurchaseRowHeight,
                inner.width, PurchaseRowHeight);

            List<TraderKindDef> kinds = CachedKinds(selected);

            if (kinds.Count == 0)
            {
                string reason = selected.HostileTo(Faction.OfPlayer)
                    ? "TraderShipsRequestShipHostile".Translate().ToString()
                    : "TraderShipsDispatchNoKindsRow".Translate().ToString();
                Widgets.Label(new Rect(listRect.x, listRect.y, listRect.width, 24f), reason);
            }
            else
            {
                Rect view = new Rect(0f, 0f, listRect.width - 18f, kinds.Count * 52f);
                Widgets.BeginScrollView(listRect, ref kindScroll, view);

                float y = 0f;
                foreach (TraderKindDef kind in kinds)
                {
                    DrawKindRow(new Rect(0f, y, view.width, 48f), kind);
                    y += 52f;
                }

                Widgets.EndScrollView();
            }

            // 分隔线，视觉上把"买船"从船型列表里摘出来。
            Widgets.DrawLineHorizontal(purchaseRect.x, purchaseRect.y - 4f, purchaseRect.width);

            // 买船入口：之前只存在于通讯台对话里，自绘窗口把它漏掉了。
            DrawPurchaseRow(purchaseRect);
        }

        private void DrawPurchaseRow(Rect rect)
        {
            int price = CommsShipRequest.PriceFor(selected, ShipPurchase.DefaultPrice);
            Thing seller = ShipPurchase.FindSeller(negotiator.Map, selected);
            int silver = CachedSilver();

            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

            Widgets.Label(new Rect(rect.x + 6f, rect.y + 3f, rect.width - 200f, 20f),
                "TraderShipsBuyShip".Translate(price.ToString().Named("PRICE")));

            // 说明走哪一条禁用原因，避免玩家猜。
            string reason = null;
            Color reasonColor = Color.gray;

            if (ShipPurchase.OwnerShipExists())
            {
                reason = "TraderShipsBuyShipAlreadyOwn".Translate();
            }
            else if (seller == null)
            {
                reason = "TraderShipsBuyShipNoSeller".Translate();
            }
            else if (silver < price)
            {
                reason = "TraderShipsShipNotEnoughSilver".Translate(price.ToString().Named("COST"));
                reasonColor = new Color(1f, 0.55f, 0.55f);
            }

            if (reason != null)
            {
                GUI.color = reasonColor;
                Widgets.Label(new Rect(rect.x + 6f, rect.y + 22f, rect.width - 200f, 18f), reason);
                GUI.color = Color.white;

                Widgets.ButtonText(new Rect(rect.xMax - RightPad - 72f, rect.y + 7f, 72f, 30f),
                    "TraderShipsDispatchBuyShort".Translate(), false, false, Color.gray);
                return;
            }

            if (Widgets.ButtonText(new Rect(rect.xMax - RightPad - 72f, rect.y + 7f, 72f, 30f),
                "TraderShipsDispatchBuyShort".Translate()))
            {
                silverCache = -1;
                Close();
                ShipPurchase.BuyShipPublic(seller, price);
            }
        }

        /// <summary>
        /// 悬停提示内容：完整说明 + 货品清单。
        /// 列表里只有一行短句，这里补全信息。
        /// </summary>
        private static string FullKindTooltip(TraderKindDef kind)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(kind.LabelCap.ToString());

            if (!kind.description.NullOrEmpty())
            {
                sb.AppendLine(kind.description);
            }

            // 该船会卖的具体物品（最多列 8 个，避免提示过长）。
            List<string> items = new List<string>();
            foreach (StockGenerator gen in kind.stockGenerators)
            {
                StockGenerator_ThingDefs ours = gen as StockGenerator_ThingDefs;
                if (ours?.thingDef == null) continue;
                if (ours.thingDef == ThingDefOf.Silver) continue;

                items.Add(ours.thingDef.LabelCap.ToString());
                if (items.Count >= 8) break;
            }

            if (items.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("TraderShipsDispatchTooltipStock".Translate());
                foreach (string item in items) sb.AppendLine("  · " + item);
            }

            return sb.ToString().TrimEnd();
        }

        // 右栏每行的横向分区（从右往左）：
        //   按钮 62 | 间隙 8 | 价格 64 | 间隙 10 | 现金 118 | 间隙 10 | 说明（剩余）
        private const float ButtonWidth = 62f;
        private const float PriceWidth = 64f;
        private const float CashWidth = 118f;
        private const float RightPad = 6f;

        private void DrawKindRow(Rect rect, TraderKindDef kind)
        {
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

            float buttonX = rect.xMax - RightPad - ButtonWidth;
            float priceX = buttonX - 8f - PriceWidth;
            float cashX = priceX - 10f - CashWidth;
            float textWidth = Mathf.Max(80f, cashX - 10f - rect.x - 6f);

            Widgets.Label(new Rect(rect.x + 6f, rect.y + 2f, textWidth, 20f), kind.LabelCap);

            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 20f, textWidth, 18f),
                (kind.description ?? "").Truncate(textWidth - 4f));

            // 列表里只放得下短句；完整说明（"由绮罗族派遣的贸易飞船……"）挂到悬停提示。
            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, new TipSignal(FullKindTooltip(kind), kind.shortHash));
            }
            GUI.color = Color.white;

            float cost = TraderShips.settings == null ? 1000f : TraderShips.settings.shipRequestSilverCost;
            int price = CommsShipRequest.PriceFor(selected, Mathf.RoundToInt(cost));
            int silver = CachedSilver();

            // 船上现金：卖货时最关心的数字。
            IntRange cash = CachedCash(kind);
            string cashText = "TraderShipsDispatchCash".Translate(
                (cash.min.ToString() + "~" + cash.max.ToString()).Named("RANGE")).ToString();
            GUI.color = new Color(0.7f, 0.9f, 0.7f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(cashX, rect.y + 12f, CashWidth, 20f), cashText);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            bool affordable = silver >= price;
            GUI.color = affordable ? Color.white : new Color(1f, 0.55f, 0.55f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(priceX, rect.y + 12f, PriceWidth, 20f), price.ToString());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            Rect buttonRect = new Rect(buttonX, rect.y + 8f, ButtonWidth, 28f);
            if (!affordable)
            {
                Widgets.ButtonText(buttonRect, "TraderShipsDispatchHailShort".Translate(), false, false, Color.gray);
            }
            else if (Widgets.ButtonText(buttonRect, "TraderShipsDispatchHailShort".Translate()))
            {
                // 先下单，成功了再关窗；失败时保留界面并给出提示。
                bool ordered = CommsShipRequest.RequestShipPublic(
                    negotiator.Map, selected, negotiator, kind, price);

                // 成交后银两变了，缓存立刻作废。
                silverCache = -1;

                if (ordered) Close();
            }
        }
    }
}