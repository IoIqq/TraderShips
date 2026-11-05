using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// 贸易台「使用」动作的 Comp。
    ///
    /// 用原版 CompUsable 的原因：
    ///   它有现成的 JobDriver_UseItem 流程 —— 殖民者会走过去、站在交互格、
    ///   播完使用时长，然后调用 UsedBy。这正是"必须有人到场才能操作"的语义。
    ///
    /// 之前用的是 Building 的 GetFloatMenuOptions，右键直接弹窗，不需要人过去，
    /// 和通讯台的使用体验不一致。
    /// </summary>
    public class CompUseEffect_TradeDispatch : CompUseEffect
    {
        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);

            Building_TradeDispatch building = parent as Building_TradeDispatch;
            if (building == null) return;

            if (!building.CanUseTradeNetwork)
            {
                Messages.Message("TraderShipsDispatchNoPower".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_TradeDispatch(usedBy, building));
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            Building_TradeDispatch building = parent as Building_TradeDispatch;
            if (building != null && !building.CanUseTradeNetwork)
            {
                return "TraderShipsDispatchNoPower".Translate();
            }

            return base.CanBeUsedBy(p);
        }
    }
}
