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

        /// <summary>
        /// 悬停提示缓存。
        ///
        /// FullKindTooltip 每次要新建 StringBuilder + List，再对最多 8 件货各取一次
        /// LabelCap（TaggedString → Translate → StripTags）。鼠标停在行上时是每帧一次，
        /// 而船型的货单在运行期不会变，缓存一次就够。
        /// </summary>
        private readonly Dictionary<TraderKindDef, string> tooltipCache
            = new Dictionary<TraderKindDef, string>();

        /// <summary>
        /// 是否选中了「独立商船」。
        ///
        /// 其它模组自带的轨道商船（如 Misc.Robots 的 AIRobotsTrader）没有 faction，
        /// 按派系查船型永远查不到。这里给它们一个独立入口：selected 为 null，
        /// 但 standaloneSelected 为 true。
        /// </summary>
        private bool standaloneSelected;

        private List<TraderKindDef> standaloneCache;

        /// <summary>
        /// 左栏派系行缓存。
        ///
        /// Factions() 每次都 new List + 四层 LINQ + ToList，而它在 DoWindowContents 里每帧跑。
        /// 只有筛选开关和搜索词变化时结果才会变，所以用脏标记缓存。
        /// </summary>
        private List<Faction> factionRows;
        private bool factionRowsDirty = true;

        /// <summary>卖家与自有飞船状态：一帧内只查一次（两者都要遍历全图）。</summary>
        private int sellerFrame = -1;
        private Thing cachedSeller;
        private int ownerShipFrame = -1;
        private bool cachedOwnerShip;

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

        /// <summary>
        /// 卖家飞船：一帧查一次。
        /// FindSeller 要 listerThings.ThingsOfDef + 逐个 TryGetComp，而它和
        /// "玩家是否已有飞船"都在每帧的停靠行里被调用。
        /// </summary>
        private Thing CachedSeller()
        {
            int frame = Time.frameCount;
            if (sellerFrame != frame)
            {
                sellerFrame = frame;
                cachedSeller = ShipPurchase.FindSeller(negotiator.Map, selected);
            }

            return cachedSeller;
        }

        private bool CachedOwnerShipExists()
        {
            int frame = Time.frameCount;
            if (ownerShipFrame != frame)
            {
                ownerShipFrame = frame;
                cachedOwnerShip = ShipPurchase.OwnerShipExists();
            }

            return cachedOwnerShip;
        }

        /// <summary>独立商船（无派系的 orbital 船型），窗口内算一次。</summary>
        private List<TraderKindDef> CachedStandalone()
        {
            if (standaloneCache == null)
            {
                standaloneCache = IncidentWorkerTraderShip.StandaloneKinds(negotiator.Map);
            }

            return standaloneCache;
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

        /// <summary>
        /// 子窗口（预订货物）扣过定金之后要通知主窗 —— 否则底部的"可用银两"
        /// 会停在扣款前的旧值，一直到关窗为止。
        /// </summary>
        internal void InvalidateSilverCache()
        {
            silverCache = -1;
        }

        /// <summary>窗口关闭时清空，下次打开重新算。</summary>
        public override void PreClose()
        {
            base.PreClose();
            silverCache = -1;
            kindsCache.Clear();
            cashCache.Clear();
            tooltipCache.Clear();
            standaloneCache = null;
            factionRows = null;
            factionRowsDirty = true;
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
                factionRowsDirty = true;
                factionScroll = Vector2.zero;
                if (selected != null && selected.HostileTo(Faction.OfPlayer)) selected = null;
            }

            if (Widgets.ButtonText(new Rect(rect.x + w + 6f, rect.y, w, rect.height),
                (filter == Filter.All ? "★ " : "") + "TraderShipsDispatchFilterAll".Translate()))
            {
                filter = Filter.All;
                factionRowsDirty = true;
                factionScroll = Vector2.zero;
            }

            // 搜索框（40 多个派系时必备）
            Rect searchRect = new Rect(rect.x + w * 2f + 20f, rect.y, rect.width - w * 2f - 20f, rect.height);
            string before = search;
            search = Widgets.TextField(searchRect, search ?? "");
            if (before != search)
            {
                factionRowsDirty = true;
                factionScroll = Vector2.zero;
            }
        }

        private void DrawFactionList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.6f));

            if (factionRowsDirty || factionRows == null)
            {
                factionRows = Factions();
                factionRowsDirty = false;
            }

            List<Faction> factions = factionRows;

            // 其它模组的独立商船单独占一行，固定在列表顶部（不参与滚动），
            // 免得玩家得在 40 多个派系里翻找。
            // 宽度要和 DrawFactionRow 的行宽一致，否则右侧的数字与高亮会错开十几像素。
            // 搜索派系名时不该继续显示这一行，否则玩家会以为过滤没生效。
            bool showStandalone = search.NullOrEmpty();

            Rect standaloneRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 20f, RowHeight);
            if (showStandalone)
            {
                DrawStandaloneRow(standaloneRect);
            }

            float topOffset = showStandalone ? RowHeight + 4f : 0f;
            float scrollHeight = rect.height - topOffset;
            Rect scrollRect = new Rect(rect.x, rect.y + topOffset, rect.width, scrollHeight);

            // 高度不够时直接不画，而不是用一个 Mathf.Max 把负数悄悄变成 40 —— 那会让滚动区
            // 压到 Close 按钮上，问题被藏起来反而更难发现。
            if (scrollHeight <= 0f) return;

            if (factions.Count == 0)
            {
                Widgets.Label(scrollRect.ContractedBy(12f), "TraderShipsDispatchNoFactions".Translate());
                return;
            }

            Rect view = new Rect(0f, 0f, scrollRect.width - 18f, factions.Count * RowHeight);
            Widgets.BeginScrollView(scrollRect.ContractedBy(2f), ref factionScroll, view);

            float y = 0f;
            float clipTop = factionScroll.y;
            float clipBottom = factionScroll.y + scrollRect.height;

            foreach (Faction faction in factions)
            {
                // 滚出视野的行不必绘制（Label / Translate / Colorize 都是白活）。
                if (y + RowHeight >= clipTop && y <= clipBottom)
                {
                    DrawFactionRow(new Rect(0f, y, view.width, RowHeight), faction);
                }

                y += RowHeight;
            }

            Widgets.EndScrollView();
        }

        private void DrawStandaloneRow(Rect rect)
        {
            int count = CachedStandalone().Count;

            Widgets.DrawBoxSolid(rect, new Color(0.17f, 0.17f, 0.20f, 0.9f));

            if (standaloneSelected) Widgets.DrawHighlightSelected(rect);
            else if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

            if (Widgets.ButtonInvisible(rect))
            {
                standaloneSelected = true;
                selected = null;
                kindScroll = Vector2.zero;
                sellerFrame = -1;
            }

            Rect nameRect = new Rect(rect.x + 12f, rect.y + 3f, rect.width - 60f, 18f);
            Widgets.Label(nameRect, "TraderShipsDispatchStandalone".Translate());

            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 20f, rect.width - 60f, 16f),
                "TraderShipsDispatchStandaloneDesc".Translate());
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = count > 0 ? Color.white : Color.gray;
            Widgets.Label(new Rect(rect.xMax - 32f, rect.y + 10f, 24f, 18f),
                count > 0 ? count.ToString() : "—");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
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
                standaloneSelected = false;
                kindScroll = Vector2.zero;
                sellerFrame = -1;
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

            if (standaloneSelected)
            {
                DrawStandaloneDetail(rect);
                return;
            }

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
                float clipTop = kindScroll.y;
                float clipBottom = kindScroll.y + listRect.height;

                foreach (TraderKindDef kind in kinds)
                {
                    if (y + 52f >= clipTop && y <= clipBottom)
                    {
                        DrawKindRow(new Rect(0f, y, view.width, 48f), kind);
                    }

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
            Thing seller = CachedSeller();
            int silver = CachedSilver();

            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

            // 预订入口和"买船"挤在同一行。
            //
            // 它是独立按钮而不是挂在某条船型上：预订针对的是"该派系的某件货"，
            // 与买船、叫船是三条互不相干的路径，塞进船型行里会让人以为是买那条船。
            Rect orderRect = new Rect(rect.xMax - RightPad - 72f - 6f - 104f, rect.y + 7f, 104f, 30f);

            // 该派系一条船都派不出来时里面必然是空列表，不如直接灰掉按钮。
            if (CachedKinds(selected).Count == 0)
            {
                Widgets.ButtonText(orderRect, "TraderShipsOrderButton".Translate(), false, false, Color.gray);
            }
            else if (Widgets.ButtonText(orderRect, "TraderShipsOrderButton".Translate()))
            {
                Find.WindowStack.Add(new Dialog_OrderGoods(negotiator, selected, this));
            }

            Widgets.Label(new Rect(rect.x + 6f, rect.y + 3f, rect.width - 200f, 20f),
                "TraderShipsBuyShip".Translate(price.ToString().Named("PRICE")));

            // 说明走哪一条禁用原因，避免玩家猜。
            string reason = null;
            Color reasonColor = Color.gray;

            if (CachedOwnerShipExists())
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
        /// 独立商船的详情：没有派系图标、没有好感、也没有买船入口（无主可买）。
        /// 船型行复用 DrawKindRow —— 它内部的 PriceFor(selected, ...) 对 null 会退到原价。
        /// </summary>
        private void DrawStandaloneDetail(Rect rect)
        {
            // 背景由 DrawFactionDetail 统一画过了，这里再画一层会让独立商船页明显更暗。
            Rect inner = rect.ContractedBy(14f);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y + 2f, inner.width - 20f, 26f),
                "TraderShipsDispatchStandalone".Translate());
            Text.Font = GameFont.Small;

            GUI.color = Color.gray;
            Widgets.Label(new Rect(inner.x, inner.y + 26f, inner.width - 20f, 18f),
                "TraderShipsDispatchStandaloneHint".Translate());
            GUI.color = new Color(1f, 0.85f, 0.5f);
            Widgets.Label(new Rect(inner.x, inner.y + 42f, inner.width - 20f, 18f),
                "TraderShipsDispatchStandaloneOwner".Translate());
            GUI.color = Color.white;

            Widgets.DrawLineHorizontal(inner.x, inner.y + 50f, inner.width);

            List<TraderKindDef> kinds = CachedStandalone();

            Rect listRect = new Rect(inner.x, inner.y + 58f, inner.width, inner.height - 58f);

            if (kinds.Count == 0)
            {
                Widgets.Label(new Rect(listRect.x, listRect.y, listRect.width, 24f),
                    "TraderShipsDispatchStandaloneEmpty".Translate());
                return;
            }

            Rect view = new Rect(0f, 0f, listRect.width - 18f, kinds.Count * 52f);
            Widgets.BeginScrollView(listRect, ref kindScroll, view);

            float y = 0f;
            float clipTop = kindScroll.y;
            float clipBottom = kindScroll.y + listRect.height;

            foreach (TraderKindDef kind in kinds)
            {
                if (y + 52f >= clipTop && y <= clipBottom)
                {
                    DrawKindRow(new Rect(0f, y, view.width, 48f), kind);
                }

                y += 52f;
            }

            Widgets.EndScrollView();
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
                string tip;
            if (!tooltipCache.TryGetValue(kind, out tip))
            {
                tip = FullKindTooltip(kind);
                tooltipCache[kind] = tip;
            }

            TooltipHandler.TipRegion(rect, new TipSignal(tip, kind.shortHash));
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