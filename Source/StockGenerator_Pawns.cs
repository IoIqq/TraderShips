using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 按指定 PawnKindDef 供货"人"的生成器。
    ///
    /// 为什么不用原版 StockGenerator_Slaves：
    ///   它的 GenerateThings 在构造 PawnGenerationRequest 时会读
    ///   this.trader.orbital（IL: ldfld StockGenerator::trader; ldfld Trader::orbital），
    ///   而我们这些在 `[StaticConstructorOnStartup]` 里凭空创建、直接塞进
    ///   DefDatabase 的 TraderKindDef 没有经过 Trader 的初始化流程，
    ///   gen.trader 是 null —— 原版生成器直接抛 NullReferenceException，
    ///   结果是整船货一件都生成不出来（日志："Exception while generating thing set"）。
    ///
    ///   所以这里自己实现：不碰 trader 字段，只用 PawnGenerator 造人。
    ///
    /// 顺带支持"人才"与"奴隶"的差异：
    ///   boostSkills = true  → 生成后把几项技能拉高（人才船）
    ///   boostSkills = false → 普通奴工（奴隶船）
    /// </summary>
    public class StockGenerator_Pawns : StockGenerator
    {
        public PawnKindDef pawnKindDef;

        /// <summary>是否把随机几项技能拉高（人才船用）。</summary>
        public bool boostSkills;

        /// <summary>成交后是否转成普通殖民者（人才船用）。奴隶船留 false。</summary>
        public bool promoteToColonist;

        /// <summary>生成时是否标成奴隶（奴隶船用）。</summary>
        public bool asSlave;

        /// <summary>技能拉高的项数与区间。</summary>
        public int boostedSkillCount = 3;
        public IntRange boostedSkillLevel = new IntRange(12, 20);

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            if (pawnKindDef == null) yield break;

            int count = countRange.RandomInRange;
            if (count <= 0) yield break;

            // 奴隶要有归属派系，没有就退到任意非敌对派系。
            Faction owner = faction ?? Find.FactionManager.RandomNonHostileFaction(false, false, true);

            for (int i = 0; i < count; i++)
            {
                Pawn pawn = null;

                try
                {
                    PawnGenerationRequest request = new PawnGenerationRequest(
                        pawnKindDef,
                        owner,
                        PawnGenerationContext.NonPlayer,
                        forTile,
                        forceGenerateNewPawn: true,
                        canGeneratePawnRelations: false);

                    pawn = PawnGenerator.GeneratePawn(request);
                }
                catch (System.Exception e)
                {
                    Log.WarningOnce("[TraderShips] Could not generate pawn " + pawnKindDef.defName
                        + ": " + e.Message, pawnKindDef.shortHash ^ 0x51A7E);
                }

                if (pawn == null) continue;

                if (boostSkills) Boost(pawn);

                // 这里绝对不要碰 pawn.guest。
                //
                // 上一版在生成时调 SetGuestStatus(null, Slave/Guest)，
                // 结果 GenerateThings 直接抛 NullReferenceException
                // （SetGuestStatus 内部会访问 HostFaction，host 为 null 就炸），
                // 整船货一件都生成不出来 —— 玩家看到的就是"奴隶船里空的"。
                //
                // 正确做法：生成时什么都不设，等小人真的加入玩家之后，
                // 由 GameComponent_PawnShipJoiners 带着正确的 host 去改状态。
                if (promoteToColonist) GameComponent_TalentPawns.MarkColonist(pawn);
                else if (asSlave) GameComponent_TalentPawns.MarkSlave(pawn);

                yield return pawn;
            }
        }

        private void Boost(Pawn pawn)
        {
            if (pawn.skills == null) return;

            List<SkillDef> all = DefDatabase<SkillDef>.AllDefsListForReading;
            if (all.Count == 0) return;

            for (int i = 0; i < boostedSkillCount; i++)
            {
                SkillDef def = all.RandomElement();
                if (def == null) continue;

                SkillRecord rec = pawn.skills.GetSkill(def);
                if (rec == null) continue;

                rec.Level = boostedSkillLevel.RandomInRange;
                rec.passion = Passion.Major;   // 有热情才能继续练上去
            }
        }

        /// <summary>
        /// 与原版 StockGenerator_Slaves 一致：只有"人类likе、可交易的 Pawn 类 def"才算本生成器处理。
        /// 不实现的话交易窗口会认为这些货没人管，出现"有货但窗口是空的"。
        /// </summary>
        public override bool HandlesThingDef(ThingDef thingDef)
        {
            return thingDef != null
                && thingDef.category == ThingCategory.Pawn
                && thingDef.race != null
                && thingDef.race.Humanlike
                && thingDef.tradeability != Tradeability.None;
        }

        public override Tradeability TradeabilityFor(ThingDef def)
        {
            return HandlesThingDef(def) ? Tradeability.All : Tradeability.None;
        }
    }
}