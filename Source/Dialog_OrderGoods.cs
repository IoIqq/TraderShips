using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 「预订货物」窗口：列出该派系所有可售物品，勾数量，付定金。
    ///
    /// 目录直接从该派系各船型的 StockGenerator_ThingDefs 里收集 ——
    /// 也就是"这些船实际会带什么货"。不从全表猜，才不会出现
    /// 订了货、船来了却没有这件东西的情况。
    /// </summary>
    public class Dialog_OrderGoods : Window
    {
        /// <summary>一行 = 一种货物。</summary>
        private class Line
        {
            public ThingDef def;
            public ThingDef stuff;

            /// <summary>
            /// 预翻译好的名称。
            /// LabelCap 每次都走 Translate()，列表几百行 × 每帧重绘，是这个窗口里最容易被忽略的开销。
            /// </summary>
            public string label;

            // ── 绘制缓存 ────────────────────────────────────────────────
            //
            // 目录是 9 条船的并集，150~440 行；DoWindowContents 在 Layout、Repaint、
            // 输入事件各跑一趟，所以下面这些东西只要能做到"一次算好"，收益就是几百倍。

            /// <summary>图标：def.uiIcon，不再为每行 MakeThing 造一个商品实例。</summary>
            public Texture2D icon;
            public Color iconColor = Color.white;

            /// <summary>
            /// 截断后的名称。
            /// GenText.Truncate(string, float) 超宽后会逐字符 Substring + CalcSize 回退，
            /// 一个 30 字的名字要做十几次字体测量，不能每帧重算。
            /// </summary>
            public string shortLabel;
            public float shortLabelWidth = -1f;

            public string unitText;
            public string subtotalText;
            private int subtotalFor = -1;

            public int unitPrice;
            public int count;
            /// <summary>TextFieldNumeric 的文本缓冲；置 null 让它按 count 重新生成。</summary>
            public string buffer;

            public string SubtotalText()
            {
                if (subtotalFor != count || subtotalText == null)
                {
                    subtotalFor = count;
                    subtotalText = (unitPrice * count).ToString();
                }

                return subtotalText;
            }
        }

        private const float RowHeight = 34f;

        /// <summary>
        /// 不可堆叠物品（家具等）的单次预订上限。
        ///
        /// 堆叠物品订 9999 只是一个大堆，但 stackLimit == 1 的东西会真的生成
        /// 上千个独立 Thing：存档体积、交易窗格数、塞货循环全都会爆。
        /// </summary>
        private const int MaxCountForUnstackable = 5;
        private const int LeadDays = 3;
        private const int LeadTicks = LeadDays * 60000;
        private const int MinDeposit = 100;

        private readonly Pawn negotiator;
        private readonly Faction faction;
        /// <summary>主窗：扣过定金后要让它把"可用银两"缓存作废。</summary>
        private readonly Dialog_TradeDispatch parent;

        private readonly List<Line> lines = new List<Line>();
        private Vector2 scroll;

        /// <summary>
        /// 可用银两只算一次。
        /// AvailableSilver 会递归遍历全图所有容器找白银，放进每帧重绘里就是每帧一次全图扫描。
        /// </summary>
        private int silverCache = -1;

        public override Vector2 InitialSize => new Vector2(780f, 640f);

        public Dialog_OrderGoods(Pawn negotiator, Faction faction, Dialog_TradeDispatch parent)
        {
            this.negotiator = negotiator;
            this.faction = faction;
            this.parent = parent;

            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            onlyOneOfTypeAllowed = true;

            BuildCatalog();
        }

        // ── 目录 ───────────────────────────────────────────────────────────

        private void BuildCatalog()
        {
            if (negotiator == null || faction == null) return;

            Map map = negotiator.Map;
            if (map == null) return;

            HashSet<ThingDef> seen = new HashSet<ThingDef>();

            foreach (TraderKindDef kind in CommsShipRequest.RequestableKinds(map, faction))
            {
                if (kind == null) continue;

                foreach (StockGenerator gen in kind.stockGenerators)
                {
                    if (!(gen is StockGenerator_ThingDefs ours)) continue;

                    ThingDef def = ours.thingDef;
                    if (def == null) continue;
                    // 白银是结算手段，不是可预订的商品。
                    if (def == ThingDefOf.Silver) continue;
                    if (!seen.Add(def)) continue;

                    // 单件货构造失败不能让整个窗口打不开 —— 那会让父窗每帧刷异常。
                    // StockGenerator_ThingDefs 里同样的调用也是这么包的。
                    try
                    {
                        lines.Add(MakeLine(def));
                    }
                    catch (System.Exception e)
                    {
                        Log.WarningOnce("[TraderShips] order catalog skipped " + def.defName
                            + ": " + e.Message, def.shortHash ^ 0x0D3E7);
                    }
                }
            }

            lines.Sort((a, b) => string.Compare(a.def.label, b.def.label, System.StringComparison.OrdinalIgnoreCase));
        }

        private static Line MakeLine(ThingDef def)
        {
            ThingDef stuff = GameComponent_ShipOrders.StuffFor(def);

            Line line = new Line();
            line.def = def;
            line.stuff = stuff;
            line.label = def.LabelCap.ToString();
            line.icon = def.uiIcon;
            line.iconColor = def.uiIconColor;

            // 单价取单件市值：和贸易窗里的基准价同源，玩家不会觉得"预订比现货贵"。
            // 市值为 0 的东西（原版里确实有）按 1 银算，否则定金永远是 0，订单白送。
            //
            // 以前这里为每件货 MakeThing 一次来读 MarketValue —— 开窗就造 150~440 个
            // 纯展示用的商品实例，还全都要活到窗口关闭。GetStatValueAbstract 直接读 def，
            // 不开实例。
            float value = def.GetStatValueAbstract(StatDefOf.MarketValue, stuff);
            if (value <= 0f) value = def.BaseMarketValue;

            line.unitPrice = Mathf.Max(1, Mathf.RoundToInt(value));
            line.unitText = line.unitPrice.ToString();
            line.count = 0;

            return line;
        }

        // ── 计算 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 全部选中货物的总价。
        ///
        /// 用 long 累加：40 行高价货各订 9999 件时 int 会溢出成负数，
        /// 定金跟着变成 100（最低价），等于白送。
        /// </summary>
        private long TotalCost()
        {
            long total = 0;

            foreach (Line line in lines)
            {
                if (line.count <= 0) continue;
                total += (long)line.unitPrice * line.count;
            }

            return total;
        }

        /// <summary>定金 = 总价 × 10%，不足 100 银按 100 银收。</summary>
        /// <summary>定金 = 总价 × 10%，不足 100 银按 100 银收。超过 int 上限时按上限收，不溢出。</summary>
        private static int DepositFor(long total)
        {
            if (total <= 0) return 0;

            double deposit = total * 0.1;
            if (deposit > int.MaxValue) return int.MaxValue;

            return Mathf.Max(MinDeposit, Mathf.RoundToInt((float)deposit));
        }

        private int CachedSilver()
        {
            if (silverCache < 0)
            {
                silverCache = CommsShipRequest.AvailableSilver(negotiator == null ? null : negotiator.Map);
            }

            return silverCache;
        }

        public override void PreClose()
        {
            base.PreClose();

            if (parent != null) parent.InvalidateSilverCache();
        }

        // ── 绘制 ───────────────────────────────────────────────────────────

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f),
                "TraderShipsOrderTitle".Translate((faction != null ? faction.Name : "").Named("FACTION")));
            Text.Font = GameFont.Small;

            if (lines.Count == 0)
            {
                Widgets.Label(new Rect(0f, 40f, inRect.width, 24f), "TraderShipsOrderEmpty".Translate());
                DrawBottomBar(new Rect(0f, inRect.height - 104f, inRect.width, 104f));
                return;
            }

            const float headerHeight = 26f;
            float bottomHeight = 104f;

            DrawHeader(new Rect(0f, 34f, inRect.width, headerHeight));

            Rect listRect = new Rect(0f, 34f + headerHeight + 2f, inRect.width,
                inRect.height - 34f - headerHeight - 6f - bottomHeight);
            DrawList(listRect);

            DrawBottomBar(new Rect(0f, inRect.height - bottomHeight, inRect.width, bottomHeight));
        }

        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.16f, 0.16f, 0.16f, 0.7f));

            Widgets.Label(new Rect(rect.x + 38f, rect.y + 4f, rect.width - 400f, 20f),
                "TraderShipsOrderColumnGoods".Translate());

            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.xMax - 340f, rect.y + 4f, 80f, 20f),
                "TraderShipsOrderColumnPrice".Translate());
            Widgets.Label(new Rect(rect.xMax - 250f, rect.y + 4f, 120f, 20f),
                "TraderShipsOrderColumnCount".Translate());
            Widgets.Label(new Rect(rect.xMax - 108f, rect.y + 4f, 100f, 20f),
                "TraderShipsOrderColumnSubtotal".Translate());
            GUI.color = Color.white;
        }

        private void DrawList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.13f, 0.6f));

            Rect view = new Rect(0f, 0f, rect.width - 18f, lines.Count * RowHeight);
            Widgets.BeginScrollView(rect.ContractedBy(2f), ref scroll, view);

            // 只画可见行。
            //
            // 目录 150~440 行，可视区只有 12 行左右；不裁剪的话每趟要把所有行的
            // 图标、两个按钮、输入框、文本全画一遍，而 DoWindowContents 每帧要跑 2~3 趟
            // （Layout / Repaint / 输入），95% 以上是屏幕外的白活。
            int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / RowHeight) - 1);
            int last = Mathf.Min(lines.Count - 1,
                Mathf.CeilToInt((scroll.y + rect.height) / RowHeight) + 1);

            for (int i = first; i <= last; i++)
            {
                DrawRow(new Rect(0f, i * RowHeight, view.width, RowHeight), lines[i], i);
            }

            Widgets.EndScrollView();
        }

        /// <summary>当前正在编辑的数量输入框；-1 表示没有。</summary>
        private int focusedIndex = -1;

        /// <summary>该行允许的最大数量：可堆叠看堆叠上限，不可堆叠只给个位数。</summary>
        private static int MaxCountFor(Line line)
        {
            if (line == null || line.def == null) return 0;
            if (line.def.stackLimit > 1) return 9999;
            return Mathf.Min(MaxCountForUnstackable, Mathf.Max(1, line.def.stackLimit));
        }

        private void DrawRow(Rect rect, Line line, int index)
        {
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

            // 从右往左排：小计 | + | 数量 | − | 单价 | 名称图标
            Rect subtotalRect = new Rect(rect.xMax - 6f - 96f, rect.y + 6f, 96f, 22f);
            Rect plusRect = new Rect(subtotalRect.x - 8f - 24f, rect.y + 5f, 24f, 24f);
            Rect fieldRect = new Rect(plusRect.x - 4f - 64f, rect.y + 5f, 64f, 24f);
            Rect minusRect = new Rect(fieldRect.x - 4f - 24f, rect.y + 5f, 24f, 24f);
            Rect unitRect = new Rect(minusRect.x - 8f - 80f, rect.y + 6f, 80f, 22f);

            Rect iconRect = new Rect(rect.x + 6f, rect.y + 4f, 26f, 26f);
            if (line.icon != null)
            {
                GUI.color = line.iconColor;
                Widgets.DrawTextureFitted(iconRect, line.icon, 1f);
                GUI.color = Color.white;
            }
            else
            {
                Widgets.DrawBoxSolid(iconRect, new Color(0.3f, 0.3f, 0.3f));
            }

            float textWidth = Mathf.Max(60f, unitRect.x - 8f - (iconRect.xMax + 8f));
            Rect labelRect = new Rect(iconRect.xMax + 8f, rect.y + 7f, textWidth, 22f);

            // 同一行的截断结果复用，列宽变了才重算（窗口不可缩放，实际上只算一次）。
            if (line.shortLabel == null || !Mathf.Approximately(line.shortLabelWidth, textWidth))
            {
                line.shortLabel = line.label.Truncate(textWidth);
                line.shortLabelWidth = textWidth;
            }

            Widgets.Label(labelRect, line.shortLabel);

            if (Mouse.IsOver(labelRect))
            {
                TooltipHandler.TipRegion(labelRect, new TipSignal(line.label, line.def.shortHash));
            }

            GUI.color = Color.gray;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(unitRect, line.unitText);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            if (Widgets.ButtonText(minusRect, "-"))
            {
                line.count = Mathf.Max(0, line.count - 1);
                line.buffer = null;
            }

            // 只有正在编辑的那一行才画 TextFieldNumeric。
            //
            // 它内部会 GUI.SetNextControlName("TextFieldNumeric"+y+"_"+x) + GUI.TextField
            // + int.TryParse，每行每趟三次字符串分配。400 行 × 每帧 2~3 趟就是上千次，
            // 而玩家同一时刻只会编辑一个格子。
            if (focusedIndex == index)
            {
                Widgets.TextFieldNumeric(fieldRect, ref line.count, ref line.buffer, 0f, MaxCountFor(line));

                if (Event.current.type == EventType.MouseDown
                    && !fieldRect.Contains(Event.current.mousePosition)
                    && !Mouse.IsOver(fieldRect))
                {
                    focusedIndex = -1;
                }
            }
            else if (Widgets.ButtonText(fieldRect, line.count.ToString()))
            {
                focusedIndex = index;
            }

            if (Widgets.ButtonText(plusRect, "+"))
            {
                line.count = Mathf.Min(MaxCountFor(line), line.count + 1);
                line.buffer = null;
            }

            GUI.color = line.count > 0 ? Color.white : Color.gray;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(subtotalRect, line.SubtotalText());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawBottomBar(Rect rect)
        {
            Widgets.DrawLineHorizontal(rect.x, rect.y - 4f, rect.width);

            long total = TotalCost();
            int deposit = DepositFor(total);
            int silver = CachedSilver();

            Widgets.Label(new Rect(rect.x + 4f, rect.y + 6f, rect.width - 200f, 22f),
                "TraderShipsOrderTotal".Translate(total.ToString().Named("TOTAL")));

            Widgets.Label(new Rect(rect.x + 4f, rect.y + 28f, rect.width - 200f, 22f),
                "TraderShipsOrderDeposit".Translate(deposit.ToString().Named("DEPOSIT")));

            bool affordable = silver >= deposit && total > 0;
            GUI.color = silver < deposit ? new Color(1f, 0.55f, 0.55f) : Color.white;
            Widgets.Label(new Rect(rect.x + 4f, rect.y + 50f, rect.width - 200f, 22f),
                "TraderShipsOrderSilver".Translate(silver.ToString().Named("SILVER")));
            GUI.color = Color.white;

            string reason = null;
            if (total <= 0) reason = "TraderShipsOrderNothingSelected".Translate();
            else if (silver < deposit)
                reason = "TraderShipsOrderNotEnoughDeposit".Translate(deposit.ToString().Named("DEPOSIT"));

            if (reason != null)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(rect.x + 4f, rect.y + 72f, rect.width - 260f, 22f), reason);
                GUI.color = Color.white;
            }

            Rect confirmRect = new Rect(rect.xMax - 156f, rect.y + 30f, 150f, 34f);
            Rect cancelRect = new Rect(confirmRect.x - 8f - 100f, rect.y + 30f, 100f, 34f);

            if (!affordable)
            {
                Widgets.ButtonText(confirmRect, "TraderShipsOrderConfirm".Translate(), false, false, Color.gray);
            }
            else if (Widgets.ButtonText(confirmRect, "TraderShipsOrderConfirm".Translate()))
            {
                Confirm(total, deposit);
            }

            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }
        }

        // ── 成交 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 扣定金、建订单。
        ///
        /// 定金是订单唯一的凭据，所以必须先扣、且必须扣足：扣到一半就放行，
        /// 等于白送一船货（玩家在窗口开着的时候把银搬走就能触发）。
        /// 扣不足就整笔撤回，不留半成品订单。
        /// </summary>
        private void Confirm(long total, int deposit)
        {
            Map map = negotiator == null ? null : negotiator.Map;
            if (map == null) return;

            // 没有订单组件就没有地方放这笔订单 —— 先确认能存再收钱。
            if (GameComponent_ShipOrders.Instance == null) return;

            int paid = CommsShipRequest.PaySilverFrom(map, deposit);
            if (paid < deposit)
            {
                CommsShipRequest.RefundSilverTo(map, paid);

                Messages.Message("TraderShipsOrderDepositShort".Translate(
                    paid.ToString().Named("PAID"),
                    deposit.ToString().Named("COST")),
                    MessageTypeDefOf.NegativeEvent, false);

                silverCache = -1;
                return;
            }

            // 从"钱已扣"到"订单已入库"之间任何一步抛异常，都必须把钱退回来。
            // 对照 ShipPurchase.BuyShip 的既有做法：扣了钱没办成事就是玩家的净损失。
            try
            {

            int dueTick = Find.TickManager.TicksGame + LeadTicks;

            List<Line> paying = new List<Line>();
            foreach (Line line in lines)
            {
                if (line.count > 0) paying.Add(line);
            }

            List<ShipOrder> placed = new List<ShipOrder>(paying.Count);

            // 定金按金额比例摊到每一行，最后一行吃零头 ——
            // 保证 ΣdepositPaid 正好等于实扣的定金，将来逐单退款时不会凭空多出或少掉几银。
            int allocated = 0;
            int weightSoFar = 0;

            for (int i = 0; i < paying.Count; i++)
            {
                Line line = paying[i];
                weightSoFar += line.unitPrice * line.count;

                int share = i == paying.Count - 1
                    ? deposit - allocated
                    : (int)((long)deposit * weightSoFar / total) - allocated;

                allocated += share;

                ShipOrder order = new ShipOrder();
                order.factionDefName = faction.def.defName;
                order.thingDefName = line.def.defName;
                order.count = line.count;
                order.dueTick = dueTick;
                order.depositPaid = share;

                placed.Add(order);
            }

            GameComponent_ShipOrders.PlaceOrders(placed);

            Messages.Message("TraderShipsOrderPlaced".Translate(
                LeadDays.ToString().Named("DAYS")), MessageTypeDefOf.PositiveEvent, false);

            silverCache = -1;
            Close();
            }
            catch (System.Exception e)
            {
                // 钱已经扣了但订单没建成 —— 全额退回，窗口留着让玩家重试。
                Log.Warning("[TraderShips] order placement failed, refunding " + paid + ": " + e);
                CommsShipRequest.RefundSilverTo(map, paid);

                Messages.Message("TraderShipsOrderFailedRefund".Translate(
                    paid.ToString().Named("DEPOSIT")), MessageTypeDefOf.NegativeEvent, false);

                silverCache = -1;
            }
        }
    }
}