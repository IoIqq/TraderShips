using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TraderShips.Quest;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace TraderShips
{

    [StaticConstructorOnStartup]
    class CompShip : ThingComp
    {
        public CompPropertiesShip Props => props as CompPropertiesShip;
        public LandedShip tradeShip;
        public bool mustCrash = false;
        public QuestPart_ShipsTradeRequest tradeRequest;

        /// <summary>索赔对话只弹一次，避免连续受击时反复打断玩家。</summary>
        public bool demandMade;

        /// <summary>当前打开的索赔对话框；只关它，不误关别的窗口。</summary>
        private Dialog_NodeTree demandDialog;

        /// <summary>
        /// 这艘船属于哪个派系。
        ///
        /// 不能只靠 tradeShip.Faction —— 随机到访的船如果 TraderKindDef.faction
        /// 在场上找不到活的 Faction 实例，那个属性就是 null，
        /// 于是"拒绝赔偿不扣好感""击毁不敌对"这类后果全部静默失效。
        /// 所以自己在生成时记一份，并随存档保存。
        /// </summary>
        public Faction ownerFaction;

        /// <summary>船名是否已经拼过船型后缀，避免重复追加。</summary>
        private bool nameDecorated;

        /// <summary>对外统一取派系：优先自己记的，再退到原版属性。</summary>
        public Faction OwnerFaction => ownerFaction ?? (tradeShip != null ? tradeShip.Faction : null);

        /// <summary>由 Crash() 发起的自毁，不应算作玩家击毁（不掉货、不敌对）。</summary>
        private bool crashing;

        ShipSprite sprite;

        public ShipSprite Sprite
        {
            get
            {
                if (sprite == null)
                {
                    sprite = Props.shipgen.CreateSprite();
                }

                return sprite;
            }
            set
            {
                sprite = value;
            }
        }

        public override void PostExposeData()
        {
            Scribe_Deep.Look(ref sprite, "shipSprite");
            Scribe_Deep.Look(ref tradeShip, "ship");
            Scribe_Values.Look(ref mustCrash, "mustCrash");
            Scribe_Values.Look(ref demandMade, "demandMade", false);
            Scribe_Deep.Look(ref tradeRequest, "tradeRequest");
            Scribe_References.Look(ref ownerFaction, "ownerFaction");
            Scribe_Values.Look(ref nameDecorated, "nameDecorated", false);
        }

        public override void PostDraw()
        {
            if (mustCrash && parent.Spawned) return;
            if (sprite == null) return;

            // 若父物体是 Building_Ship 且烘焙成功，绘制已经由 Graphic 接管，
            // 这里再画一次就会重叠。只有烘焙不可用（设置关闭 / 失败）时才手绘。
            if (parent is Building_Ship ship && ship.HasBakedGraphic) return;

            Sprite.Draw(parent.DrawPos);
        }

        /// <summary>
        /// 给 Building_Ship 用的烘焙 Graphic。失败返回 null，调用方退回标准绘制。
        /// </summary>
        internal Graphic_ShipBaked TryBuildBakedGraphic(ThingDef def)
        {
            // 派系专属美术优先：如果模组作者为这个派系画了整船贴图，就用它，
            // 不再走随机拼装。没有就退回随机外观。
            Graphic_ShipBaked custom = TryBuildFactionGraphic(def);
            if (custom != null) return custom;

            TraderShipsSettings settings = TraderShips.settings;
            if (settings != null && !settings.bakeShipGraphics) return null;

            ShipSpriteBaker.Baked baked = ShipSpriteBaker.TryBake(Sprite);
            if (baked == null || baked.texture == null) return null;

            Graphic_ShipBaked graphic = new Graphic_ShipBaked();
            graphic.data = def.graphicData;
            graphic.path = def.graphicData?.texPath;
            graphic.color = Color.white;
            graphic.colorTwo = Color.white;
            // 画布以绘制原点为中心，世界尺寸 = 像素数 * sprite.scale
            graphic.SetBaked(baked.texture, new Vector2(
                baked.size.x * Sprite.scale,
                baked.size.y * Sprite.scale));
            return graphic;
        }

        /// <summary>
        /// 派系专属整船贴图。
        ///
        /// 约定路径：Textures/TraderShips/Factions/&lt;FactionDefName&gt;.png
        /// 例如帝国是 Textures/TraderShips/Factions/Empire.png。
        /// 贴图按 100 像素 = 1 格换算世界尺寸，与 RimWorld 常规美术比例一致。
        /// 找不到就返回 null，调用方退回随机拼装。
        /// </summary>
        private Graphic_ShipBaked TryBuildFactionGraphic(ThingDef def)
        {
            Faction faction = tradeShip?.Faction;
            if (faction?.def == null) return null;

            string path = "TraderShips/Factions/" + faction.def.defName;
            Texture2D texture = ContentFinder<Texture2D>.Get(path, false);
            if (texture == null) return null;

            Graphic_ShipBaked graphic = new Graphic_ShipBaked();
            graphic.data = def.graphicData;
            graphic.path = path;
            graphic.color = Color.white;
            graphic.colorTwo = Color.white;

            const float PixelsPerCell = 100f;
            graphic.SetBaked(texture, new Vector2(
                texture.width / PixelsPerCell,
                texture.height / PixelsPerCell));
            return graphic;
        }

        public override void CompTick()
        {
            // 玩家自有的船（穿梭机船体）没有 tradeShip —— 装货、发射、飞行全部交给
            // CompShuttle / CompTransporter，这里不需要任何按船逻辑。
            if (tradeShip == null) return;

            if (tradeShip.Departed)
            {
                if (parent.Spawned) SendAway();
            }
            else
            {
                tradeShip.PassingShipTick();
            }

        }

        public override string CompInspectStringExtra()
        {
            if (tradeShip == null) return null;

            string res =
                tradeShip.def.LabelCap + "\n" +
                "TraderShipsLeavingIn".Translate(GenDate.ToStringTicksToPeriod(tradeShip.ticksUntilDeparture));

            if (tradeRequest != null)
            {
                res += "\n" + "TraderShipsQuestRequestInfo".Translate(TradeRequestUtility.RequestedThingLabel(tradeRequest.requestedThingDef, tradeRequest.requestedCount).CapitalizeFirst(), (tradeRequest.requestedThingDef.GetStatValueAbstract(StatDefOf.MarketValue, null) * (float)tradeRequest.requestedCount).ToStringMoney(null));
            }

            return res;
        }


        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (respawningAfterLoad) return;
            if (mustCrash) return;
            // 穿梭机船体没有外来商船，不播落地音效、不发"商船抵达"信件、不生成偷渡客。
            if (tradeShip == null) return;

            if (Props.soundThud != null)
                Props.soundThud.PlayOneShot(parent);

            TaggedString info = "TraderShipsArrival".Translate(tradeShip.name, tradeShip.def.label, (tradeShip.Faction == null) ? "TraderArrivalNoFaction".Translate() : "TraderArrivalFromFaction".Translate(tradeShip.Faction.Named("FACTION")));
            Find.LetterStack.ReceiveLetter(tradeShip.def.LabelCap, info, LetterDefOf.PositiveEvent, parent, tradeShip.Faction);

            TrySpawnStowaway();
        }

        /// <summary>
        /// 偷渡客：有机会从船上溜下来一个人。
        ///
        /// 不再直接编入殖民地 —— 那样玩家会得到一个无法拒绝、还能派上战场的小人。
        /// 现在他会以访客身份在殖民地徘徊，并发一封「请求收留」的信件让玩家决定。
        ///
        /// 徘徊靠 LordJob_VisitColony：访客必须有一个 Lord 才会走动，
        /// 单纯设置 guest 状态的话小人会一直站着不动。
        /// </summary>
        private void TrySpawnStowaway()
        {
            TraderShipsSettings settings = TraderShips.settings;
            if (settings == null || settings.stowawayChancePercent <= 0) return;
            if (Rand.Range(0, 100) >= settings.stowawayChancePercent) return;

            try
            {
                Map map = parent.Map;
                if (map == null) return;

                // 偷渡客是「无阵营」的。
                //
                // 以前这里的派系取的是 tradeShip.Faction —— 一旦这艘船的派系跟玩家敌对，
                // 溜下来的偷渡客也直接是敌人，还会跟着打殖民地。
                // 现在统一不给他任何派系：无主的小人不会主动敌视玩家，
                // 收留时才 SetFaction(玩家)。
                IntVec3 spot = CellFinder.RandomClosewalkCellNear(parent.Position, map, 4);
                Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Villager, null);
                GenSpawn.Spawn(pawn, spot, map, Rot4.Random);

                // PawnGenerator 传 null 有时仍会给出默认阵营，这里强制清掉。
                if (pawn.Faction != null) pawn.SetFaction(null);

                int stayTicks = Mathf.RoundToInt(settings.stowawayStayDays * 60000f);

                ChoiceLetter_Stowaway letter = new ChoiceLetter_Stowaway();
                letter.def = LetterDefOf.NeutralEvent;
                letter.ID = Find.UniqueIDsManager.GetNextLetterID();
                letter.title = "TraderShipsStowawayLetterLabel".Translate();
                letter.Text = "TraderShipsStowawayLetterText".Translate(
                    pawn.Named("PAWN"),
                    Mathf.Max(1, Mathf.RoundToInt(settings.stowawayStayDays)).ToString().Named("DAYS"));
                // 小人本身也要有离场时间。
                //
                // 以前只给信件设了 StartTimeout，玩家一直不点这封信时，
                // 信件会到点消失，但小人没有 exitMapAfterTick，于是永久滞留在殖民地。
                pawn.mindState.exitMapAfterTick = Find.TickManager.TicksGame + stayTicks;

                letter.pawn = pawn;
                letter.lookTargets = pawn;
                // 信件到时自动消失；小人本人由访客 Lord 的时长负责离场。
                letter.StartTimeout(stayTicks);

                Find.LetterStack.ReceiveLetter(letter);
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Could not spawn stowaway: " + e.Message);
            }
        }

        public override string TransformLabel(string label)
        {
            // 玩家自有的船（穿梭机船体）没有 tradeShip。
            // Thing.Label 在 UI 里每帧都会被查询，这里一旦空引用就是"每帧一条报错"。
            if (tradeShip == null) return label;

            return tradeShip.name;
        }

        /// <summary>血量掉到这个比例以下时，船长要求赔偿。</summary>
        public const float DemandHpThreshold = 0.6f;

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            if (!parent.Spawned || tradeShip == null) return;
            if (mustCrash || crashing) return;

            // 谁打的？
            //
            // 以前是"掉血到 60% 就找玩家赔"，不管伤害来自谁 ——
            // 火灾、雷击、自己人的爆炸、别的派系交火，统统算到玩家头上。
            // 现在必须先确认肇事者：
            //   玩家殖民者/玩家所属建筑 → 找玩家赔
            //   别的派系 → 不惊动玩家，只在两个派系之间扣好感
            //   环境伤害（无肇事者）→ 完全忽略
            Faction attackerFaction = AttackerFactionOf(dinfo);
            if (attackerFaction == null) return;

            if (!attackerFaction.IsPlayer)
            {
                // 别家派系打的：扣"攻击者 ↔ 船主"这一对的好感，玩家不赔钱也不弹窗。
                Faction owner = OwnerFaction;
                if (owner != null && owner != attackerFaction)
                {
                    attackerFaction.TryAffectGoodwillWith(owner, -15, true, true, null, null);
                }
                return;
            }

            float hpPct = (float)parent.HitPoints / parent.MaxHitPoints;
            if (hpPct > DemandHpThreshold) return;

            // One demand per ship. The old behaviour was a flat 25-damage / 90% HP check that
            // simply made the ship flee, with no consequence for the player at all.
            if (demandMade) return;
            demandMade = true;

            int cost = UnityEngine.Mathf.RoundToInt((1f - hpPct) * 2000f);
            if (cost < 100) cost = 100;

            ShowDemandDialog(cost);
        }

        /// <summary>
        /// 从伤害信息里取出肇事者的派系。
        ///
        /// Instigator 可能是小人，也可能是炮塔、机械体这类建筑 —— 两者都有 Faction。
        /// 返回 null 表示"没人负责"（火灾蔓延、雷击、塌方、随机爆炸），
        /// 这种情况不该由玩家掏钱。
        /// </summary>
        private static Faction AttackerFactionOf(DamageInfo dinfo)
        {
            Thing instigator = dinfo.Instigator;

            if (instigator == null)
            {
                // 有些伤害（例如破片）只带 InstigatorGuilty 标记而没有实体，
                // 这种同样不追究。
                return null;
            }

            Pawn pawn = instigator as Pawn;
            if (pawn != null)
            {
                return pawn.Faction;
            }

            return instigator.Faction;
        }

        /// <summary>
        /// The captain stops the fight and demands compensation. Three outcomes, all of them
        /// ending with the ship leaving: pay silver, refuse (goodwill hit), or declare hostility.
        /// </summary>
        private void ShowDemandDialog(int cost)
        {
            Faction faction = OwnerFaction;

            DiaNode node = new DiaNode("TraderShipsShipDamagedText".Translate(
                tradeShip.name.Named("SHIP"),
                cost.ToString().Named("COST"),
                (faction == null ? "TraderShipsAnyFaction".Translate().ToString() : faction.Name).Named("FACTION")).CapitalizeFirst());

            DiaOption pay = new DiaOption("TraderShipsShipPayOption".Translate(cost.ToString().Named("COST")));
            bool canPay = faction != null && CommsShipRequest.CountPayableSilver(parent.Map) >= cost;
            if (canPay)
            {
                pay.action = delegate
                {
                    int paid = parent.Map != null
                        ? CommsShipRequest.PaySilverFrom(parent.Map, cost)
                        : 0;

                    // 对话框开着的这段时间里，银两可能被搬走或被别的模组消耗掉。
                    // 扣不满就不能让它当"赔过了"过去，否则等于白拿一次免费赔偿。
                    if (paid < cost)
                    {
                        if (paid > 0 && parent.Map != null)
                        {
                            CommsShipRequest.RefundSilverTo(parent.Map, paid);
                        }

                        Messages.Message(
                            "TraderShipsShipNotEnoughSilver".Translate(cost.ToString().Named("COST")),
                            MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    Messages.Message("TraderShipsShipCompensated".Translate(
                        cost.ToString().Named("COST")), parent, MessageTypeDefOf.NeutralEvent, true);
                    SendAway();
                    CloseDemandDialog();
                };
            }
            else
            {
                pay.Disable("TraderShipsShipNotEnoughSilver".Translate(cost.ToString().Named("COST")));
            }
            node.options.Add(pay);

            DiaOption refuse = new DiaOption("TraderShipsShipRefuseOption".Translate());
            refuse.action = delegate
            {
                if (faction != null)
                {
                    int before = faction.PlayerGoodwill;
                    Faction.OfPlayer.TryAffectGoodwillWith(faction, -40, true, false, null, null);
                    int after = faction.PlayerGoodwill;
                    int lost = before - after;

                    // 好感变化必须让玩家看见。
                    // 之前是静默扣分（连提示都没有），玩家根本不知道发生过什么。
                    NotifyGoodwillLost(faction, lost);
                }

                SendAway();
                CloseDemandDialog();
            };
            node.options.Add(refuse);

            DiaOption hostile = new DiaOption("TraderShipsShipDeclareHostileOption".Translate());
            hostile.action = delegate
            {
                // 已敌对 / 已消灭就不要再设一次：否则每点一次都刷一条状态变更。
                if (faction != null && !faction.defeated && !faction.HostileTo(Faction.OfPlayer))
                    faction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, true, "TraderShipsAttackedShip");
                SendAway();
                CloseDemandDialog();
            };
            node.options.Add(hostile);

            demandDialog = new Dialog_NodeTree(node, true, true, null);
            Find.WindowStack.Add(demandDialog);
        }

        /// <summary>
        /// 好感损失通知：一条消息 + 一封信，避免玩家错过。
        ///
        /// delta 是实际扣掉的点数（可能因为下限被截断，小于预期的 40）。
        /// delta 为 0 说明已经到底了，这时也明确告诉玩家"关系已无法更差"。
        /// </summary>
        private void NotifyGoodwillLost(Faction faction, int delta)
        {
            if (faction == null) return;

            string text = delta > 0
                ? "TraderShipsGoodwillLost".Translate(faction.Name.Named("FACTION"), delta.ToString().Named("DELTA")).ToString()
                : "TraderShipsGoodwillAlreadyLow".Translate(faction.Name.Named("FACTION")).ToString();

            Messages.Message(text, MessageTypeDefOf.NegativeEvent, true);

            Find.LetterStack.ReceiveLetter(
                "TraderShipsGoodwillLostLabel".Translate(),
                text,
                LetterDefOf.NegativeEvent,
                new LookTargets(parent));
        }

        /// <summary>
        /// 关掉赔偿对话框。
        ///
        /// DiaOption 正常会自己关窗，但如果我们已经在 action 里把船送走了，
        /// 窗口有时会残留一帧并再次响应点击 —— 手动关一次最稳。
        /// </summary>
        private void CloseDemandDialog()
        {
            WindowStack stack = Find.WindowStack;
            if (stack == null) return;

            // 只关我们自己那个对话框。
            // 以前是"关掉栈顶第一个 Dialog_NodeTree" ——
            // 通讯台对话同时开着时，关掉的是通讯台，赔偿框留在那儿还能再点一次「宣战」。
            if (demandDialog != null)
            {
                stack.TryRemove(demandDialog, true);
                demandDialog = null;
            }
        }

        /// <summary>
        /// Player destroyed the ship outright: its whole cargo hits the ground and the owning
        /// faction turns hostile. Crash-driven destruction is explicitly excluded.
        /// </summary>
        public override void PostDestroy(DestroyMode mode, Map prevMap)
        {
            base.PostDestroy(mode, prevMap);

            if (crashing || mustCrash || tradeShip == null) return;
            if (mode != DestroyMode.KillFinalize) return;

            Map map = prevMap;
            IntVec3 pos = parent.PositionHeld;
            if (map == null) return;

            try
            {
                // 击毁时来一发小型爆炸：既是对"船被打爆"的视觉反馈，也让周围稍微有点代价。
                // 爆炸挪到撒货之后（见下方），否则刚掉出来的货会被自家爆炸烧掉一半。

                // 整船货物全部落地。
                //
                // 之前用 tradeShip.Goods，它会把 soldPrisoners 里的人剔掉，
                // 而且掉落失败时直接 Destroy(Vanish) —— 玩家看到的
                // 就是"船炸了，货只掉了一半"。
                //
                // 现在改走 IThingHolder.GetDirectlyHeldThings()：
                // 那是船真正的库存容器，里面有什么就掉什么。
                List<Thing> cargo = new List<Thing>();

                // 两个来源都收，去重后再掉。
                // GetDirectlyHeldThings() 就是 TradeShip.things（IThingHolder 的原话），
                // 但万一某个子类换了实现，Goods 这条还能兜住。
                ThingOwner held = tradeShip.GetDirectlyHeldThings();
                if (held != null)
                {
                    foreach (Thing t in held)
                    {
                        if (t != null && !cargo.Contains(t)) cargo.Add(t);
                    }
                }

                try
                {
                    foreach (Thing t in tradeShip.Goods)
                    {
                        if (t != null && !cargo.Contains(t)) cargo.Add(t);
                    }
                }
                catch (Exception)
                {
                    // Goods 是迭代器，库存被清空时可能抛，忽略。
                }

                int dropped = 0;
                foreach (Thing t in cargo)
                {
                    if (t == null || t.Destroyed) continue;

                    // Pawn 不能用 GenPlace：那是"往格子里塞物品"的路径，
                    // 小人需要走 GenSpawn 才会正确注册到 mapPawns / 派系名单。
                    Pawn pawn = t as Pawn;
                    if (pawn != null)
                    {
                        IntVec3 pawnCell = CellFinder.RandomClosewalkCellNear(pos, map, 6);
                        if (!pawnCell.InBounds(map)) pawnCell = pos;

                        try
                        {
                            GenSpawn.Spawn(pawn, pawnCell, map, Rot4.Random);
                            dropped++;
                        }
                        catch (Exception e)
                        {
                            Log.WarningOnce("[TraderShips] Could not spawn dropped pawn: " + e.Message, pawn.thingIDNumber);
                        }
                        continue;
                    }

                    // 每件都撒在船周围，避免所有东西堆在一格上互相挤压失败。
                    IntVec3 dropCell = CellFinder.RandomClosewalkCellNear(pos, map, 6);
                    if (!dropCell.InBounds(map)) dropCell = pos;

                    bool placed = GenPlace.TryPlaceThing(t, dropCell, map, ThingPlaceMode.Near);

                    if (!placed)
                    {
                        // 换一格再试；再失败就强制掉在地上，绝不让货物凭空消失。
                        IntVec3 retry = CellFinder.RandomClosewalkCellNear(pos, map, 9);
                        if (retry.InBounds(map))
                        {
                            placed = GenPlace.TryPlaceThing(t, retry, map, ThingPlaceMode.Near);
                        }
                    }

                    if (!placed)
                    {
                        GenDrop.TryDropSpawn(t, pos, map, ThingPlaceMode.Near, out Thing _);
                        placed = t.Spawned;
                    }

                    if (placed) dropped++;
                }

                if (Prefs.DevMode)
                {
                    Log.Message("[TraderShips] destroyed ship dropped " + dropped + "/" + cargo.Count + " things");
                }

                // 先撒货、后爆炸。
                //
                // 以前顺序反了：爆炸半径 3.5、30 点伤害，会把刚落地的一堆货
                // （还有掉出来的小人）炸毁一部分。玩家看到的"掉落"和实际不符。
                GenExplosion.DoExplosion(
                    pos, map, radius: 3.5f, damType: DamageDefOf.Bomb,
                    instigator: null, damAmount: 30, armorPenetration: 1f,
                    explosionSound: DefDatabase<SoundDef>.GetNamedSilentFail("Explosion_Bomb"),
                    damageFalloff: true);

                Faction faction = OwnerFaction;
                if (faction != null && !faction.defeated && !faction.HostileTo(Faction.OfPlayer))
                {
                    faction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, true, "TraderShipsDestroyedShip");
                    Messages.Message("TraderShipsShipDestroyedHostile".Translate(faction.Name.Named("FACTION")),
                        MessageTypeDefOf.NegativeEvent, true);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[TraderShips] Could not drop destroyed ship cargo: " + e);
            }
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn negotiator)
        {
            if (tradeShip == null) yield break;

            string label = "TradeWith".Translate(tradeShip.GetCallLabel());

            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, delegate ()
            {
                Job job = JobMaker.MakeJob(Globals.TraderShipsTradeWithShip, parent);
                negotiator.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }, MenuOptionPriority.InitiateSocial, null, null, 0f, null, null), negotiator, parent);

            yield break;
        }

        Faction GetFaction(TraderKindDef trader, Faction forcedFaction)
        {
            // 强制指定优先：玩家从哪个派系呼叫，来的船就属于哪个派系。
            // 原版四个 Orbital_* 船型在装了 Odyssey 时都绑定到 TradersGuild，
            // 若按 kind.faction 走，从别的派系呼叫会来一艘 TradersGuild 的船。
            if (forcedFaction != null) return forcedFaction;

            if (trader.faction == null) return null;

            return (from f in Find.FactionManager.AllFactions where f.def == trader.faction select f).RandomElementWithFallback();
        }

        public void GenerateInternalTradeShip(Map map, TraderKindDef traderKindDef = null, Faction forcedFaction = null)
        {
            if (traderKindDef == null) traderKindDef = DefDatabase<TraderKindDef>.AllDefs.RandomElementByWeightWithFallback(x => x.CalculatedCommonality);

            Faction faction = GetFaction(traderKindDef, forcedFaction);

            // 兜底：船型自己绑了 FactionDef 但场上没有实例时，随便挑一个非敌对派系，
            // 至少保证后面的好感/敌对逻辑有对象可用。
            if (faction == null && traderKindDef.faction != null)
            {
                foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
                {
                    if (f.def == traderKindDef.faction) { faction = f; break; }
                }
            }

            ownerFaction = faction;

            tradeShip = new LandedShip(map, traderKindDef, faction);
            tradeShip.passingShipManager = map.passingShipManager;
            tradeShip.GenerateThings();

            // 把船型写进船名。
            //
            // 交易窗口右上角只显示 trader.TraderName（= TradeShip.name），
            // 玩家因此看不出自己正在跟"杂货船"还是"武器船"做买卖 ——
            // 呼叫时明明选过，进了交易窗就全忘了。
            // 直接拼进名字里最稳，不会碰任何原版 UI。
            try
            {
                // 只拼一次：复用同一艘船时会变成「A · 杂货船 · 杂货船」。
                if (!nameDecorated && !traderKindDef.label.NullOrEmpty())
                {
                    tradeShip.name = tradeShip.name + " · " + traderKindDef.label;
                    nameDecorated = true;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[TraderShips] Could not decorate ship name: " + e.Message, 771234102);
            }
        }


        private void FulfillTradeRequest()
        {
            tradeRequest.FulfillRequest(tradeShip);

            QuestUtility.SendQuestTargetSignals(parent.questTags, "TradeRequestFulfilled", parent.Named("SUBJECT"));
            tradeRequest = null;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 玩家自有的船没有外来商船，「让船离开」对它没有意义（会把它拆掉）。
            if (tradeShip != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "TraderShipsSendAway".Translate(),
                    defaultDesc = "TraderShipsSendAwayDesc".Translate(),
                    action = new Action(SendAway),
                    icon = SendAwayTexture,
                };
            }

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dev: 击毁本船",
                    defaultDesc = "测试用：直接把飞船打死，验证货物掉落与派系敌对。",
                    action = delegate
                    {
                        parent.Kill(new DamageInfo(DamageDefOf.Bomb, 99999f));
                    },
                };
            }

            if (tradeRequest != null)
            {
                Command_Action command_Action = new Command_Action();
                command_Action.defaultLabel = "CommandFulfillTradeOffer".Translate();
                command_Action.defaultDesc = "CommandFulfillTradeOfferDesc".Translate();
                command_Action.icon = TradeCommandTex;
                command_Action.action = delegate ()
                {
                    if (tradeRequest == null)
                    {
                        Log.Error("Attempted to fulfill an unavailable request");
                        return;
                    }

                    WindowStack windowStack = Find.WindowStack;
                    TaggedString text = "CommandFulfillTradeOfferConfirm".Translate(GenLabel.ThingLabel(tradeRequest.requestedThingDef, null, tradeRequest.requestedCount));

                    windowStack.Add(Dialog_MessageBox.CreateConfirmation(text, delegate () { FulfillTradeRequest(); }, false, null));
                };

                if (!tradeRequest.CanFulfillRequest(tradeShip))
                {
                    command_Action.Disable("CommandFulfillTradeOfferFailInsufficient".Translate(TradeRequestUtility.RequestedThingLabel(tradeRequest.requestedThingDef, tradeRequest.requestedCount)));
                }

                yield return command_Action;
            }

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dev: randomize sprite",
                    action = delegate ()
                    {
                        Color color = sprite.color;
                        sprite = Props.shipgen.CreateSprite();
                        sprite.color = color;
                        sprite.FinishCreating();
                    },
                };
                yield return new Command_Action
                {
                    defaultLabel = "Dev: randomize color",
                    action = delegate ()
                    {
                        sprite.color = ShipSprite.RandomColor();
                        sprite.FinishCreating();
                    },
                };
            }

            yield break;
        }

        /// <summary>给购买流程用：卖完船后让卖家起飞离开。</summary>
        public void SendAwayPublic()
        {
            SendAway();
        }

        private void SendAway()
        {
            // 幂等：船已经飞走/已经被打爆时直接返回。
            //
            // 以前这里会 Log.Error("Tried to send ... away, but it's unspawned.")，
            // 而赔偿对话框在船离场之后仍然开着，玩家再点一次「拒绝」
            // 就会每秒刷十几条红字。这种事没有任何信息量，静默返回即可。
            if (parent == null || !parent.Spawned || parent.Destroyed) return;

            Map map = parent.Map;
            IntVec3 position = parent.Position;

            parent.DeSpawn();

            Skyfaller skyfaller = ThingMaker.MakeThing(Props.takeoffAnimation, null) as Skyfaller;
            if (!skyfaller.innerContainer.TryAdd(parent, true))
            {
                Log.Error("Could not add " + parent.ToStringSafe<Thing>() + " to a skyfaller.");
                parent.Destroy(DestroyMode.QuestLogic);
            }

            GenSpawn.Spawn(skyfaller, position, map, WipeMode.Vanish);
        }

        IntVec3 RandomNearby(IntVec3 loc)
        {
            int radius = Props.crashScatterRadius;
            return new IntVec3(loc.x + Rand.Range(-radius, radius), loc.y, loc.z + Rand.Range(-radius, radius));
        }

        public void Crash()
        {
            if (!mustCrash || !parent.Spawned) return;

            mustCrash = false;
            crashing = true;
            IntVec3 loc = parent.Position;
            Map map = parent.Map;

            parent.Destroy(DestroyMode.KillFinalize);

            if (Props.crashScatterChunk != null)
            {
                for (int i = 0; i < Props.crashScatterChunkCount.RandomInRange; i++)
                {
                    GenSpawn.Spawn(Props.crashScatterChunk, RandomNearby(loc), map);
                }
            }

            if (Props.crashPilotKind != null)
            {
                for (int i = 0; i < Props.crashPilotCount.RandomInRange; i++)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Props.crashPilotKind, tradeShip.Faction));
                    GenPlace.TryPlaceThing(pawn, RandomNearby(loc), map, ThingPlaceMode.Near);
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.Bomb, 250, 0, -1, parent));
                    if (!pawn.Dead) HealthUtility.DamageUntilDowned(pawn);
                }
            }

            foreach (Thing thing in tradeShip.Goods)
            {
                GenPlace.TryPlaceThing(thing, RandomNearby(loc), map, ThingPlaceMode.Near);

                Pawn pawn = thing as Pawn;
                if (pawn != null)
                {
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.Bomb, 250, 0, -1, parent));
                }
            }

            Find.LetterStack.ReceiveLetter("TraderShipsCrashName".Translate(), "TraderShipsCrash".Translate(tradeShip.name, tradeShip.def.label), LetterDefOf.NeutralEvent, new LookTargets(loc, map), tradeShip.Faction);
        }

        private static readonly Texture2D SendAwayTexture = ContentFinder<Texture2D>.Get("TraderShips/SendAway", true);
        private static readonly Texture2D TradeCommandTex = ContentFinder<Texture2D>.Get("UI/Commands/FulfillTradeRequest", true);
    }
}